"""Generate lexer_corpus.json: random SQL around one ` @a ` placeholder, labelled
by DuckDB's own tokenizer. `code` is true when DuckDB starts a token at the `@`
(so the placeholder is real SQL and must be substituted) and false when the `@`
is inside a string or comment (and must be left alone).

Run (needs the duckdb package; labels depend on its version):
  python tests/Thunderduck.Data.Tests/data/gen_lexer_corpus.py
"""
import json
import pathlib
import random

import duckdb

FRAGMENTS = [
    "'x'", "'\\'", "E'\\''", "E'\\'", "e'\\\\'", "1E'\\'", "a1E'\\'", "_E'\\'", "éE'\\'", "(1)E'\\'",
    "$$", "$t$", "$é$", "$_1$", "a$", "a$$", "$1", "1$$", "x$t$", "$a$ foo$a$",
    "--c\n", "--c\r", "--c", "/*", "*/", "/* /* */", "\"q\"", "\"", "'", "''", "'it''s'",
    "x", "1", "1.5", "1.5e1", "1e+2", " ", "\n", " \n ", "'a'\n'", "E'a'\n'", "\r", "(", ")", ",", "E", "e", "\\", "-", "*", "/",
]


LEXER_ERRORS = ("NOT IMPLEMENTED", "zero-length delimited identifier")


def label(sql: str, pos: int):
    # Inputs DuckDB's lexer rejects outright (e.g. E'\\é') stop the tokenizer
    # early; DuckDB refuses the whole query, so there is nothing to label.
    try:
        duckdb.connect().extract_statements(sql)
    except Exception as exc:  # noqa: BLE001
        if any(m in str(exc) for m in LEXER_ERRORS):
            return None
    try:
        tokens = duckdb.tokenize(sql)
    except Exception:  # noqa: BLE001 - unlexable input: no label
        return None
    return any(off == pos for off, _ in tokens)


def main() -> None:
    rng = random.Random(20261008)
    seen, cases = set(), []
    while len(cases) < 4000:
        before = "".join(rng.choice(FRAGMENTS) for _ in range(rng.randint(0, 6)))
        after = "".join(rng.choice(FRAGMENTS) for _ in range(rng.randint(0, 4)))
        sql = "SELECT " + before + " @a " + after
        # "" is a DuckDB lexing error (zero-length identifier) that stops the
        # tokenizer, so cases containing it cannot be labelled.
        if sql in seen or '""' in sql:
            continue
        seen.add(sql)
        # duckdb.tokenize reports UTF-8 byte offsets.
        code = label(sql, len(("SELECT " + before).encode()) + 1)
        if code is not None:
            cases.append({"sql": sql, "code": code})
    out = pathlib.Path(__file__).with_name("lexer_corpus.json")
    out.write_text(json.dumps({"duckdb": duckdb.__version__, "cases": cases}, ensure_ascii=False, indent=0))
    print(out, len(cases), sum(c["code"] for c in cases), "code")


if __name__ == "__main__":
    main()
