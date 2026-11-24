using System.Globalization;
using Thunderduck.Data;
using Xunit;

public class ParameterRendererTests
{
    static string R(string sql, params (string, object?)[] ps) =>
        SqlParameterRenderer.Render(sql, ps.ToDictionary(p => p.Item1, p => p.Item2, StringComparer.OrdinalIgnoreCase));

    [Fact]
    public void Substitutes_named_parameters() =>
        Assert.Equal("SELECT * FROM t WHERE a = 5 AND b = 'x'", R("SELECT * FROM t WHERE a = @a AND b = @B", ("a", 5), ("b", "x")));

    [Fact]
    public void Leaves_strings_identifiers_and_comments_alone() =>
        Assert.Equal(
            "SELECT '@a', \"@a\", 1 -- @a\n/* @a */ , 1",
            R("SELECT '@a', \"@a\", @a -- @a\n/* @a */ , @a", ("a", 1)));

    [Fact]
    public void Ignores_operators_and_emails_in_strings() =>
        Assert.Equal("SELECT x @> y, 'me@x.io'", R("SELECT x @> y, 'me@x.io'"));

    // The scanner must agree with DuckDB's lexer about where strings and
    // comments end; otherwise a value lands where DuckDB is still inside a
    // string and its quotes close it (injection).
    [Fact]
    public void Escape_strings_honor_backslash_escapes()
    {
        Assert.Equal(@"SELECT E'it\'s @a', 1", R(@"SELECT E'it\'s @a', @a", ("a", 1)));
        Assert.Equal(@"SELECT e'\\', 1", R(@"SELECT e'\\', @a", ("a", 1)));
        // A value cannot escape the string DuckDB sees.
        Assert.Equal(@"SELECT E'\' , @a , '", R(@"SELECT E'\' , @a , '", ("a", "x' OR 1=1 --")));
    }

    [Fact]
    public void Dollar_quoted_strings_are_skipped()
    {
        Assert.Equal("SELECT $$ @a $$, $tag$ @a $ $tag$, 1", R("SELECT $$ @a $$, $tag$ @a $ $tag$, @a", ("a", 1)));
        // $1-style positional references are not dollar quotes.
        Assert.Equal("SELECT $1, 1", R("SELECT $1, @a", ("a", 1)));
    }

    // Each case below was checked against DuckDB's parser (see the commit message).
    [Fact]
    public void E_prefix_after_a_number_param_or_paren_starts_an_escape_string()
    {
        // DuckDB lexes 1E'..', $1E'..', (1)E'..' as an E-string: the \' does not end it.
        Assert.Equal(@"SELECT 1E'\' , @a , '", R(@"SELECT 1E'\' , @a , '", ("a", "x' OR 1=1 --")));
        Assert.Equal(@"SELECT (1)E'\' , @a , '", R(@"SELECT (1)E'\' , @a , '", ("a", "x' OR 1=1 --")));
    }

    [Fact]
    public void E_continuing_an_identifier_is_a_plain_string()
    {
        // a1E'..', a$E'..', _E'..', éE'..' are identifier + standard string (no backslash escapes).
        foreach (var id in new[] { "a1E", "a$E", "_E", "éE" })
            Assert.Equal($@"SELECT {id}'\', 1", R($@"SELECT {id}'\', @a", ("a", 1)));
    }

    [Fact]
    public void Dollar_tags_allow_non_ascii_and_must_not_continue_an_identifier()
    {
        Assert.Equal("SELECT $é$ @a $é$, 1", R("SELECT $é$ @a $é$, @a", ("a", 1)));
        Assert.Equal("SELECT $_1$ @a $_1$, 1", R("SELECT $_1$ @a $_1$, @a", ("a", 1)));
        // After a number, $$ still opens a dollar quote.
        Assert.Equal("SELECT 1$$ @a $$", R("SELECT 1$$ @a $$", ("a", 1)));
        // a$$x is one identifier: what follows is ordinary SQL.
        Assert.Equal("SELECT a$$x, 1", R("SELECT a$$x, @a", ("a", 1)));
    }

    [Fact]
    public void Line_comments_end_at_carriage_return()
    {
        // DuckDB ends -- comments at \r as well as \n; a string opened after \r hides @a.
        Assert.Equal("SELECT 1 -- c\r, ' \n @a ', 1", R("SELECT 1 -- c\r, ' \n @a ', @a", ("a", 1)));
    }

    [Fact]
    public void String_continuation_across_a_newline_keeps_the_literal_open()
    {
        // 'a'<newline>'b' is one literal in DuckDB (also across -- comments), and an
        // E'..' string keeps backslash escapes in its continuation.
        Assert.Equal("SELECT 'a'\n' @a ', 1", R("SELECT 'a'\n' @a ', @a", ("a", 1)));
        Assert.Equal("SELECT 'a' -- x\n ' @a ', 1", R("SELECT 'a' -- x\n ' @a ', @a", ("a", 1)));
        Assert.Equal("SELECT E'a'\n'\\' @a ', 1", R("SELECT E'a'\n'\\' @a ', @a", ("a", 1)));
        // Without a newline, or across a block comment, they are separate tokens.
        Assert.Equal("SELECT 'a' 1 'b'", R("SELECT 'a' @a 'b'", ("a", 1)));
        Assert.Equal("SELECT 'a'/* c */\n'\\', 1", R("SELECT 'a'/* c */\n'\\', @a", ("a", 1)));
    }

    [Fact]
    public void Exponent_is_part_of_the_number()
    {
        // 1.5e1$$..$$ is a number then a dollar quote; 1e1E'..' is 1e1 then an E-string.
        Assert.Equal("SELECT 1.5e1$$ @a $$", R("SELECT 1.5e1$$ @a $$", ("a", 1)));
        Assert.Equal(@"SELECT 1e1E'\' , @a , '", R(@"SELECT 1e1E'\' , @a , '", ("a", 1)));
        // A $1 parameter takes no exponent: $1E1E'..' is $1, identifier E1E, then a standard string.
        Assert.Equal(@"SELECT $1E1E'\', 1", R(@"SELECT $1E1E'\', @a", ("a", 1)));
    }

    [Fact]
    public void Block_comments_nest()
    {
        Assert.Equal("SELECT /* a /* b */ @a */ 1", R("SELECT /* a /* b */ @a */ @a", ("a", 1)));
    }

    [Fact]
    public void Negative_numbers_never_form_a_line_comment()
    {
        // 0-@d with d = -3 must not render as 0--3 (a -- comment that drops the rest of the line).
        foreach (var v in new object[] { -3, -3L, (sbyte)-3, (short)-3, new System.Numerics.BigInteger(-3), -3.5m, -3.5, -3.5f, -0.0 })
        {
            var sql = R("SELECT 0-@d AND user_id = 1", ("d", v));
            Assert.DoesNotContain("--", sql);
            Assert.EndsWith(" AND user_id = 1", sql);
        }
        Assert.Equal("SELECT 0-(-3), -(-3)", R("SELECT 0-@d, -@d", ("d", -3)));
        Assert.Equal("(-3.5)", SqlParameterRenderer.Literal(-3.5m));
        Assert.Equal("3", SqlParameterRenderer.Literal(3));
    }

    [Fact]
    public void Unknown_parameter_throws() =>
        Assert.Throws<ArgumentException>(() => R("SELECT @missing"));

    [Theory]
    [InlineData("O'Brien", "'O''Brien'")]
    [InlineData("a\\'; DROP TABLE t; --", "'a\\''; DROP TABLE t; --'")]
    [InlineData("çöğ — 漢字", "'çöğ — 漢字'")]
    public void Strings_are_quoted_and_injection_safe(string value, string expected) =>
        Assert.Equal(expected, SqlParameterRenderer.Literal(value));

    [Fact]
    public void Literals_for_each_type()
    {
        Assert.Equal("NULL", SqlParameterRenderer.Literal(null));
        Assert.Equal("NULL", SqlParameterRenderer.Literal(DBNull.Value));
        Assert.Equal("TRUE", SqlParameterRenderer.Literal(true));
        Assert.Equal("(-12)", SqlParameterRenderer.Literal(-12L));
        Assert.Equal("1.5", SqlParameterRenderer.Literal(1.5));
        Assert.Equal("'NaN'::DOUBLE", SqlParameterRenderer.Literal(double.NaN));
        Assert.Equal("12345678901234567.890123", SqlParameterRenderer.Literal(12345678901234567.890123m));
        Assert.Equal("'2a6a3006-b4a9-4596-9ac7-1861dc1e9dd8'::UUID", SqlParameterRenderer.Literal(Guid.Parse("2a6a3006-b4a9-4596-9ac7-1861dc1e9dd8")));
        Assert.Equal("DATE '2024-01-31'", SqlParameterRenderer.Literal(new DateOnly(2024, 1, 31)));
        Assert.Equal("TIME '13:45:00.123000'", SqlParameterRenderer.Literal(new TimeOnly(13, 45, 0, 123)));
        Assert.Equal("TIMESTAMP '2024-01-31 13:45:00.000000'", SqlParameterRenderer.Literal(new DateTime(2024, 1, 31, 13, 45, 0)));
        Assert.Equal("TIMESTAMPTZ '2024-01-31 13:45:00.000000+02:00'",
            SqlParameterRenderer.Literal(new DateTimeOffset(2024, 1, 31, 13, 45, 0, TimeSpan.FromHours(2))));
        Assert.Equal("from_hex('DEAD')", SqlParameterRenderer.Literal(new byte[] { 0xde, 0xad }));
        Assert.Throws<NotSupportedException>(() => SqlParameterRenderer.Literal(new object()));
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("de-DE")]
    public void Culture_does_not_change_literals(string culture)
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            Assert.Equal("1.5", SqlParameterRenderer.Literal(1.5m));
            Assert.Equal("1234567.25", SqlParameterRenderer.Literal(1234567.25));
            Assert.Equal("DATE '2024-01-31'", SqlParameterRenderer.Literal(new DateOnly(2024, 1, 31)));
            Assert.Equal("SELECT 1", R("SELECT @I", ("i", 1))); // 'I'/'i' in tr-TR
        }
        finally { CultureInfo.CurrentCulture = old; }
    }
}
