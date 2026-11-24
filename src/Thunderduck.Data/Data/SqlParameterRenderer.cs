using System.Globalization;
using System.Numerics;
using System.Text;

namespace Thunderduck.Data;

/// <summary>Renders @name placeholders as DuckDB literals. The API has no bind
/// parameters, so this is the injection boundary: a tiny scanner skips string
/// literals (incl. E'..' backslash escapes and $tag$ dollar quotes), quoted
/// identifiers and (nested) comments, and every value is a typed literal. The
/// scanner must end every string/comment exactly where DuckDB's lexer does:
/// ending one too early would substitute a value inside what DuckDB still
/// reads as a string, and the value's quotes would then close it.</summary>
internal static class SqlParameterRenderer
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Where the scanner is inside a bare word, which decides whether a following
    // E' opens an escape string and whether a $ opens a dollar quote. This
    // tracks tokens forward, the way DuckDB's lexer does: 1a1E'..' is the
    // number 1, then the identifier a1E, then a standard string.
    private enum Word { None, Identifier, Number, Param }

    public static string Render(string sql, IReadOnlyDictionary<string, object?> parameters)
    {
        var sb = new StringBuilder(sql.Length + 16);
        var word = Word.None;
        var eStartsToken = false; // sql[i - 1] is an E/e that began a new identifier
        var i = 0;
        while (i < sql.Length)
        {
            var c = sql[i];
            var end = -1;
            if (c == '\'' && eStartsToken)
                end = SkipEscapeString(sql, i);
            else if (c == '\'' || c == '"')
                end = SkipQuoted(sql, i, c);
            else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                // DuckDB ends a line comment at \n or \r.
                end = sql.IndexOfAny(LineEnds, i);
                if (end < 0) end = sql.Length;
            }
            else if (c == '/' && i + 1 < sql.Length && sql[i + 1] == '*')
                end = SkipBlockComment(sql, i);
            else if (c == '$' && word != Word.Identifier && DollarTag(sql, i) is { } tag)
            {
                var close = sql.IndexOf(tag, i + tag.Length, StringComparison.Ordinal);
                end = close < 0 ? sql.Length : close + tag.Length;
            }
            else if (c == '@' && i + 1 < sql.Length && IsNameStart(sql[i + 1]) && (i == 0 || !IsNamePart(sql[i - 1])))
            {
                var j = i + 1;
                while (j < sql.Length && IsNamePart(sql[j])) j++;
                var name = sql[(i + 1)..j];
                if (!parameters.TryGetValue(name, out var value))
                    throw new ArgumentException($"The SQL references @{name}, but no parameter with that name was added.");
                sb.Append(Literal(value));
                i = j;
                word = Word.None;
                eStartsToken = false;
                continue;
            }

            if (end >= 0)
            {
                // A string, quoted identifier, comment or dollar quote: copied verbatim.
                sb.Append(sql, i, end - i);
                i = end;
                word = Word.None;
                eStartsToken = false;
                continue;
            }

            var before = word;
            if (before == Word.Number && c is 'e' or 'E' && ExponentDigitsAt(sql, i + 1) is var expEnd and > 0)
            {
                // 1.5e1 / 1e+2: the exponent belongs to the number (1e1E'..' is 1e1, then an E-string).
                sb.Append(sql, i, expEnd - i);
                i = expEnd;
                word = Word.None; // the number is complete: 1e1e1 is 1e1, then identifier e1
                eStartsToken = false;
                continue;
            }
            if (IsIdentStart(c))
            {
                // A letter after a number starts a new identifier (1E'..' is 1, E'..').
                word = Word.Identifier;
                eStartsToken = c is 'E' or 'e' && before != Word.Identifier;
            }
            else
            {
                eStartsToken = false;
                if (c == '$')
                    word = before == Word.Identifier ? Word.Identifier : Word.Param; // $1 (no dollar quote here)
                else if (char.IsAsciiDigit(c))
                    word = before is Word.Identifier or Word.Param ? before : Word.Number; // $1E1 has no exponent
                else if (c == '.' && before == Word.Number)
                    word = Word.Number;
                else
                    word = Word.None;
            }
            sb.Append(c);
            i++;
        }
        return sb.ToString();
    }

    public static string Literal(object? value) => value switch
    {
        null or DBNull => "NULL",
        bool b => b ? "TRUE" : "FALSE",
        sbyte or byte or short or ushort or int or uint or long or ulong => Signed(System.Convert.ToString(value, Inv)!),
        BigInteger bi => Signed(bi.ToString(Inv)),
        decimal m => Signed(m.ToString(Inv)),
        double d => Signed(Floating(d)),
        float f => Signed(Floating(f)),
        string s => Quote(s),
        char ch => Quote(ch.ToString()),
        Guid g => $"'{g:D}'::UUID",
        DateOnly d => $"DATE '{d.ToString("yyyy-MM-dd", Inv)}'",
        TimeOnly t => $"TIME '{t.ToString("HH:mm:ss.ffffff", Inv)}'",
        TimeSpan ts when ts >= TimeSpan.Zero && ts < TimeSpan.FromDays(1)
            => $"TIME '{TimeOnly.FromTimeSpan(ts).ToString("HH:mm:ss.ffffff", Inv)}'",
        DateTime dt => $"TIMESTAMP '{dt.ToString("yyyy-MM-dd HH:mm:ss.ffffff", Inv)}'",
        DateTimeOffset dto => $"TIMESTAMPTZ '{dto.ToString("yyyy-MM-dd HH:mm:ss.ffffffzzz", Inv)}'",
        byte[] bytes => $"from_hex('{System.Convert.ToHexString(bytes)}')",
        _ => throw new NotSupportedException($"Parameters of type {value.GetType().Name} are not supported."),
    };

    // A negative number is parenthesized: after a '-' in the SQL (0-@d), a bare
    // -3 would form "--", a line comment that silently drops the rest of the line.
    private static string Signed(string number) => number.StartsWith('-') ? $"({number})" : number;

    private static string Floating(double d) =>
        double.IsNaN(d) ? "'NaN'::DOUBLE"
        : double.IsPositiveInfinity(d) ? "'Infinity'::DOUBLE"
        : double.IsNegativeInfinity(d) ? "'-Infinity'::DOUBLE"
        : d.ToString("R", Inv);

    // DuckDB string literals have no backslash escapes: doubling quotes is complete.
    private static string Quote(string s) => "'" + s.Replace("'", "''", StringComparison.Ordinal) + "'";

    private static int SkipQuoted(string sql, int start, char q)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == q)
            {
                if (i + 1 < sql.Length && sql[i + 1] == q) { i += 2; continue; } // '' or ""
                if (q == '\'' && ContinuationQuote(sql, i + 1) is var cont and >= 0) { i = cont + 1; continue; }
                return i + 1;
            }
            i++;
        }
        return sql.Length;
    }

    // E'...': backslash escapes the next character (\' and \\ included); '' is also a quote.
    private static int SkipEscapeString(string sql, int start)
    {
        var i = start + 1;
        while (i < sql.Length)
        {
            if (sql[i] == '\\') { i += 2; continue; }
            if (sql[i] == '\'')
            {
                if (i + 1 < sql.Length && sql[i + 1] == '\'') { i += 2; continue; }
                // The continuation of an E-string is still an E-string.
                if (ContinuationQuote(sql, i + 1) is var cont and >= 0) { i = cont + 1; continue; }
                return i + 1;
            }
            i++;
        }
        return sql.Length;
    }

    // Block comments nest in DuckDB (Postgres lexer): /* a /* b */ c */ is one comment.
    private static int SkipBlockComment(string sql, int start)
    {
        var depth = 0;
        var i = start;
        while (i < sql.Length - 1)
        {
            if (sql[i] == '/' && sql[i + 1] == '*') { depth++; i += 2; continue; }
            if (sql[i] == '*' && sql[i + 1] == '/')
            {
                i += 2;
                if (--depth == 0) return i;
                continue;
            }
            i++;
        }
        return sql.Length;
    }

    // "$$" or "$tag$" opening a dollar-quoted string; null for $1 or a lone $.
    // Tag characters follow DuckDB's lexer: an identifier start, then
    // identifier characters other than '$'.
    private static string? DollarTag(string sql, int start)
    {
        var j = start + 1;
        if (j < sql.Length && sql[j] == '$') return "$$";
        if (j >= sql.Length || !IsIdentStart(sql[j])) return null;
        while (j < sql.Length && IsIdentCont(sql[j]) && sql[j] != '$') j++;
        return j < sql.Length && sql[j] == '$' ? sql[start..(j + 1)] : null;
    }

    // DuckDB (Postgres lexer) joins 'a'<ws-with-newline>'b' into one literal:
    // after the closing quote, horizontal space and -- comments, at least one
    // newline, then any whitespace or -- comment lines, then a quote. Returns the
    // index of that continuing quote, or -1. Block comments do not count.
    private static int ContinuationQuote(string sql, int pos)
    {
        var sawNewline = false;
        var i = pos;
        while (i < sql.Length)
        {
            var c = sql[i];
            if (c is '\n' or '\r') { sawNewline = true; i++; }
            else if (c is ' ' or '\t' or '\f' || (sawNewline && c == '\v')) i++;
            else if (c == '-' && i + 1 < sql.Length && sql[i + 1] == '-')
            {
                var end = sql.IndexOfAny(LineEnds, i);
                i = end < 0 ? sql.Length : end;
            }
            else break;
        }
        return sawNewline && i < sql.Length && sql[i] == '\'' ? i : -1;
    }

    // End index of an exponent's digits ([+-]?digit+) starting at pos, or -1.
    private static int ExponentDigitsAt(string sql, int pos)
    {
        var j = pos;
        if (j < sql.Length && sql[j] is '+' or '-') j++;
        if (j >= sql.Length || !char.IsAsciiDigit(sql[j])) return -1;
        while (j < sql.Length && char.IsAsciiDigit(sql[j])) j++;
        return j;
    }

    private static readonly char[] LineEnds = ['\n', '\r'];

    // DuckDB (Postgres lexer) identifier characters; any non-ASCII char counts.
    private static bool IsIdentStart(char c) => char.IsAsciiLetter(c) || c == '_' || c >= '\u0080';
    private static bool IsIdentCont(char c) => IsIdentStart(c) || char.IsAsciiDigit(c) || c == '$';

    private static bool IsNameStart(char c) => char.IsAsciiLetter(c) || c == '_';
    private static bool IsNamePart(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';
}
