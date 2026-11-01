# IconChest Phase 5 Prompt: The Query Language

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 4 code. Those phases must be complete and their tests passing.

## Goal

Build the Advanced search language: a precise, documented, JQL/Wireshark-style expression language with a lexer, parser, validator, SQL compiler, formatter, help generator and positional error reporting. It plugs into the Phase 4 image endpoints through the `q` parameter. This is the most test-heavy phase, and **the tests are the specification**. Section 11 of the brief gives the outline. This prompt settles every ambiguity and extends the brief where marked.

## Before you write any code

1. Read section 11 of the brief, the Phase 2 `WildcardMatcher`, the Phase 4 `ImageSearchRequest` and SQL builder, and the schema.
2. Check how dates are stored (the exact text format and the effective-date column) and use one shared formatter for storing and comparing.
3. Reply with a short plan and any assumptions, then build.

## Design overview

A four-stage pipeline in `IconChest.Core/Query`, with no dependency on HTTP:

1. **Lexer** turns text into tokens with exact positions.
2. **Parser** builds an untyped syntax tree. It fails fast at the first syntax error.
3. **Validator (binder)** matches field names, operators and values against the field definitions, converts values, and **collects every semantic error** (up to 10).
4. **Compiler** turns the validated tree into a parameterised SQL `WHERE` fragment.

Also: a **formatter** (tree to canonical text), a **help generator**, and a converter from the Phase 4 simple filter to query text. Positions and lengths are in **UTF-16 code units** (matching JavaScript string indices), zero-based.

## Language specification

### Lexical rules

- **Whitespace** (space, tab, newline) separates tokens and is otherwise ignored.
- **Symbols:** `(` `)` `,` `==` `=` `!=` `<` `<=` `>` `>=` `&&` `||` `!`. A `!` directly followed by `=` is `!=`. A lone `&` or `|` is an error.
- **Keywords** (case-insensitive): `and or not in is empty like contains under true false`. Used as a bare word they must be quoted.
- **Words:** an unquoted run of letters (including Unicode), digits and the characters `_ - . * ?`. Words are interpreted later according to context: a field name, a number, a size such as `10kb`, a date, `true` or `false`, an unquoted text value, or a bare search term.
- **Strings:** `"..."` or `'...'`. Inside a string a backslash escapes the next character for the purpose of finding the end of the string (so `\"` does not close it). The meaning of backslashes inside the string depends on the field (below).
- Any other character is `unexpected_character`.
- **Limits:** query length 2,000 characters; nesting depth 20 (parentheses plus chained `not`); 200 terms; 100 values in an `in` list; 200 characters per pattern.

### Grammar (final)

```
query     = [ orExpr ] ;                 (* empty or blank query matches everything *)
orExpr    = andExpr { OR andExpr } ;
andExpr   = unary { AND unary } ;
unary     = NOT unary | primary ;
primary   = "(" orExpr ")" | predicate ;
predicate = word cmpOp value
          | word [ NOT ] IN "(" value { "," value } ")"
          | word IS [ NOT ] EMPTY
          | word [ NOT ] LIKE string
          | word [ NOT ] CONTAINS string
          | word [ NOT ] UNDER string
          | word                          (* boolean field shorthand, or a bare search term *)
          | string ;                      (* bare search term *)
value     = word | string ;
cmpOp     = "==" | "=" | "!=" | "<" | "<=" | ">" | ">=" ;
OR  = "or"  | "||" ;   AND = "and" | "&&" ;   NOT = "not" | "!" ;
```

Precedence from tightest to loosest: a predicate, then `not`, then `and`, then `or`. So `not tag == "x"` is `not (tag == "x")`. There is **no implicit AND**. Two adjacent terms are the error `missing_connective` with the suggestion `and`. A word followed by a comparison operator, `in`, `is`, `like`, `contains`, `under`, or `not` plus one of `in`, `like`, `contains` and `under` is a field predicate. Otherwise a lone word or string is a bare search term.

**Extensions to the brief (confirm in your reply):** the `not in`, `not like`, `not contains` and `not under` forms; boolean shorthand (`favourite` means `favourite == true`, `not favourite` and `!hidden` work); `contains` is a literal substring match; partial dates (below).

### Fields

| Field | Type | Operators | Column or source |
| --- | --- | --- | --- |
| `type` | format | `==` `=` `!=` `in` `like` | format. Values are validated against known formats: png, jpg (alias `jpeg`), gif, webp, svg, bmp, ico |
| `filename` | text | `==` `=` `!=` `in` `like` `contains` `is empty` | file name with extension |
| `title`, `description` | text | same as `filename` | editable text. Null and empty are both "empty". |
| `folder` | path | `==` `=` `!=` `in` `under` `like` `contains` | directory of the file |
| `tag` | multi-text | `==` `=` `!=` `in` `like` `contains` `is empty` | the image's tags |
| `width`, `height` | integer | all comparisons, `in`, `is empty` | pixels. Null for unreadable files. |
| `size` | size | all comparisons, `in` | bytes |
| `rating` | integer 0-5 | all comparisons, `in`, `is empty` | 0 means unrated, and `is empty` is true for 0 |
| `favourite` (alias `favorite`), `hidden`, `missing`, `duplicate`, `transparent` | boolean | `==` `=` `!=` | flags. `transparent` is null when unknown. |
| `date` | date | all comparisons, `is empty` | the effective date |
| `created`, `modified`, `taken`, `added` | date | same as `date` | `taken` is the override, else the EXIF date, else null |

An operator not listed for a field is `operator_not_allowed`, and the message lists the allowed ones. Field names are case-insensitive.

### Value rules

- **Text:** a quoted string, or an unquoted word that has no `*` or `?`. An unquoted word containing wildcards after `==`, `!=` or `in` is `wildcard_not_allowed` with the suggestion to use `like`. Text comparison is **case-insensitive and Unicode-aware** (NFC-normalise, then invariant lowercase), and `==` matches the whole value.
- **Backslashes in text fields:** `\\` is one backslash, `\"` and `\'` are the quote characters, `\*` and `\?` are a literal `*` and `?` (they matter inside `like`), and a backslash before any other character is a literal backslash. **In `folder` values a backslash is always a path separator, never an escape**, and `/` and `\` are interchangeable. A folder string therefore cannot end with a backslash (strip it). If a string is unterminated because of `\"` at the end, put that hint in the error's suggestion.
- **`like`:** `*` matches any run of characters including none, `?` matches exactly one, and the pattern must match the **whole** value. So `title like "elepha*"` is a prefix match and `title like "*phan*"` is a substring match.
- **`contains`:** a literal substring test. Wildcard characters have no special meaning.
- **Numbers:** non-negative whole numbers. Quotes are optional. A decimal for an integer field is `invalid_number`.
- **Sizes:** a number (decimals allowed) with an optional unit `b`, `kb`, `mb`, `gb` (case-insensitive, 1024-based, no space). No unit means bytes. Round to the nearest whole byte. Anything else is `invalid_size`.
- **Dates:** `yyyy-mm-dd`, unquoted or quoted, and it must be a real calendar date (`invalid_date` otherwise). **Extension:** partial dates `yyyy` and `yyyy-mm` stand for the whole year or month. Comparisons are on the **calendar day in the injected time zone** (the machine's local zone by default), converted to UTC bounds in C# (so a day can be 23 or 25 hours across a DST change). With the period `P` spanning `[start, end)`: `==` means in `P`, `!=` means not in `P`, `<` means before `start`, `<=` means before `end`, `>` means at or after `end`, `>=` means at or after `start`.
- **Booleans:** `true` or `false`.
- **Rating:** 0 to 5, else `invalid_value`.

### Meaning of each operator

- **Two-valued logic.** Every predicate is true or false and never unknown. A null value equals nothing and satisfies no ordering comparison. `!=` is exactly `not ==`, so `width != 100` **includes** images whose width is null. `not (width > 5)` also includes them.
- **`in (a, b)`** is true if the field equals any value. **`not in`** is its negation.
- **`is empty`** is true for null or empty text, a null number or date, and rating 0. **`is not empty`** is its negation.
- **`under`** (folder only): the directory equals the path or is inside it, compared by path key (platform case rules). Folder values must be absolute paths, otherwise `invalid_value`. `folder == "p"` is the exact directory only.
- **Tags:** `tag == "dog"` means the image has that tag. `tag != "dog"` means it does not. `tag like "do*"` and `tag contains "og"` mean any of its tags match. `tag in ("dog","cat")` means it has either. `tag is empty` means untagged. A tag name that does not exist simply matches nothing and is not an error.
- **Bare search term:** a literal case-insensitive substring across filename, title and description. If the word or string contains `*` or `?`, it is a wildcard pattern implicitly wrapped in `*...*`. A quoted string containing spaces is one term.
- **Boolean shorthand:** a lone boolean field name means `== true`. A lone name of any other field is `field_needs_operator`.
- **Implicit filters:** hidden images are excluded unless the query **mentions** the field `hidden` anywhere in the tree (even under `not`, or in one branch of an `or`). Missing images are excluded unless it mentions `missing`. These two rules exist for compatibility with the Phase 4 defaults, and the documentation must state the `or` quirk plainly.

### Errors

Every error has `code`, `message`, `position`, `length` and optional `suggestion`. The parser reports only the first syntax error. The validator reports all semantic errors it finds, up to 10, ordered by position.

| Code | When | Position and length |
| --- | --- | --- |
| `query_too_long` | over 2,000 characters | the first excess character to the end |
| `unexpected_character` | a character outside the lexical rules | that character |
| `unterminated_string` | no closing quote | the opening quote to the end of input |
| `unexpected_token` | a token that cannot appear here | the token |
| `unexpected_end` | input ends mid-expression | end of input, length 0 |
| `expected_expression`, `expected_operator`, `expected_value` | a required part is missing | the offending token, or the end |
| `missing_connective` | two terms with no `and` or `or` | the second term. Suggestion `and`. |
| `unbalanced_parenthesis` | an unmatched `(` or `)` | that parenthesis |
| `empty_group`, `empty_list` | `()` or `in ()` | the span from `(` to `)` |
| `nesting_too_deep`, `too_many_terms`, `too_many_values`, `pattern_too_long` | a limit is exceeded | the token that crossed it |
| `unknown_field` | not a field | the field. Suggestion: the nearest field name (edit distance 2 or less, or a shared prefix). |
| `field_needs_operator` | a lone non-boolean field name | the field |
| `operator_not_allowed` | an operator invalid for that field | the operator token |
| `invalid_value` | wrong kind of value (unknown format, rating out of range, relative folder, bad boolean) | the value |
| `invalid_number`, `invalid_size`, `invalid_date` | malformed number, size or date (including 2026-02-30) | the value |
| `wildcard_not_allowed` | an unquoted wildcard word after `==`, `!=` or `in` | the value |

Messages must be short, plain English, and name the field or operator involved.

## Scope

### 1. Core types

`QueryLexer`, `QueryParser`, `QueryNode` types, `QueryValidator`, `FieldDefinition` and `FieldRegistry` (the single source of truth: name, aliases, type, allowed operators, SQL expression, description, examples), `QueryCompiler`, `QueryFormatter`, `QueryError` and `QueryException`. Provide a facade `QueryEngine.Parse(text)` returning either a compiled query or a list of errors. Never throw for bad user input.

### 2. Compiler

- Output `CompiledQuery { WhereSql, Parameters, MentionsHidden, MentionsMissing }`, with the image table aliased consistently with the Phase 4 builder. Parameter names are generated (`@q0`, `@q1`, and so on).
- SQL text may include only fragments from the field registry and fixed templates. **No user text is ever concatenated into SQL.** The SQL for two queries that differ only in literal values must be byte-identical (test this).
- Register deterministic SQLite functions `iq_eq(a, b)` and `iq_like(value, pattern)` on every connection (extend the Phase 1 connection factory, covering the API, the CLI and tests). They use the shared case-folding and the Phase 2 `WildcardMatcher`. SQLite's own `LIKE` and `NOCASE` are ASCII-only and must not be relied on. An ASCII fast path is allowed only if tests prove identical results.
- Tag, `in` and `is empty` predicates on tags use `EXISTS` sub-queries. Confirm `image_tag` has an index that supports them, and add one in a new migration if not.
- Date predicates compare the stored UTC text against precomputed bounds using the shared date formatter.
- Combine with the Phase 4 filters, sorting and paging without changing them. The implicit hidden and missing filters are added by the compiler or by `ImageSearchService` according to the mention flags.

### 3. Formatter

`QueryFormatter` prints a tree as canonical text (lowercase keywords, single spaces, minimal parentheses, strings re-quoted and re-escaped). `Parse(Format(Parse(x)))` must equal `Parse(x)` for every valid query.

### 4. Simple filter to query

`SimpleFilterToQuery` converts the Phase 4 structured filter into query text (used by the "Convert to query" button in Phase 9). The converted query must return **exactly the same images** as the structured filter (test this on the seeded dataset for many filters). Document any filter that cannot be expressed and make the converter report it.

### 5. API changes (Phase 4 endpoints)

- `q` on `GET /api/images` and `/api/images/ids` is now live. A query with errors returns 400 problem details with an extra member `queryErrors` (the error list). Keep: `q` plus any other filter parameter is 400.
- `POST /api/query/validate` with `{ text }` returns `{ valid, errors, normalized, mentionsHidden, mentionsMissing }`. It must be cheap, since the UI will call it as the user types (debounced).
- `GET /api/query/help` returns the help model (fields with types, operators and descriptions, grammar summary, value formats, error codes, limits, examples).
- `POST /api/query/from-filter` with the structured filter returns `{ text }` or the unsupported-filter report.

### 6. Documentation and help

- `docs/QUERY-LANGUAGE.md`: a complete user and developer reference (overview, lexical rules, final grammar, field table, operator semantics and null rules, dates and time zones, implicit filters, error table with an example for each code, a cookbook of at least 30 examples grouped by task, limits, and notes on differences from SQL). The field, error and example tables are generated from code and kept in sync by a golden test that fails if the checked-in file is stale. A CLI command writes the file.
- The **examples are data**: a list of `{ text, description, expectedIds }` over the seeded test dataset, used for the help model, the docs, and the tests that run each one. Include at least the examples in section 11.7 of the brief.

### 7. Dev CLI additions

| Command | Behaviour |
| --- | --- |
| `query validate "<text>" [--sql]` | Prints OK with the normalised text (and SQL and parameters with `--sql`), or each error with a caret line under the query |
| `query run "<text>" [--sort field] [--dir asc\|desc] [--limit n]` | Runs it through the search service and prints a table |
| `query explain "<text>"` | Prints the syntax tree, SQL, parameters and `EXPLAIN QUERY PLAN` |
| `query docs [--write]` | Prints or rewrites `docs/QUERY-LANGUAGE.md` |

Example error display:

```
tag == "dog" and (
                  ^ unexpected_end: expected an expression after "("
```

## Out of scope

The search UI, tooltips and autocomplete (Phase 9), relative dates (`today`, `7d`), `between`, regular expressions, EXIF fields, saved searches, ranking, natural-language or LLM search, changing the Phase 4 simple filters.

## Required tests

Use xUnit, Linux and Windows safe, with an injected time zone and clock. Data-driven tests (theory data in a readable table) wherever possible.

- **Lexer:** every token kind with exact positions; keywords case-insensitive; Unicode words; strings with each escape form and unterminated cases; a lone `&` or `|`; `!=` versus `!`; positions after multi-byte characters.
- **Parser:** tree shape for each grammar rule; precedence (`a or b and c`, `not a and b`, `!(a or b)`); parentheses; chained `not`; boolean shorthand and bare terms; `missing_connective`; each syntax error code with exact position and length (at least 40 cases).
- **Validator:** a table of at least 60 error cases giving the exact code, position, length and suggestion, covering every code in the error table; several semantic errors reported together and ordered; the cap of 10; every field and operator combination (allowed and not allowed) from the field table; value conversion for each type, including size units and rounding, partial dates, `jpeg` alias, rating range.
- **Semantics against a seeded dataset** of about 40 images with known formats, sizes, dimensions (including nulls), tags (including untagged and Unicode names), dates (including nulls and DST edge days), flags, titles with accents and mixed case, and folders with nested paths: at least 120 queries with expected id lists, covering every field, every operator, `not` forms, `in`, `is empty`, `under` versus `==`, LIKE wildcards and escapes, `contains` literal behaviour, bare terms and wildcard terms, tag semantics (`==`, `!=`, `like`, `in`, `is empty`), null semantics (`width != 100` includes null, `not (width > 5)` includes null), and Unicode case-insensitivity.
- **Dates:** inclusive and exclusive bounds for each comparison on a day, a month and a year; a time zone with DST (for example Europe/London on 2026-03-29 and 2026-10-25); an image just before and just after local midnight.
- **Implicit filters:** hidden and missing excluded by default; `hidden == true`, `hidden == false`, `not hidden`, and `hidden or favourite` change the behaviour as documented, including the `or` quirk.
- **Formatter:** round trip for all examples and for generated random trees; canonical output for messy input.
- **Injection and safety:** a list of hostile inputs (quotes, `--`, `;`, `' OR 1=1 --`, `%`, `_`, backslashes, null characters, very long strings) never alter the SQL text; **two queries that differ only in literals compile to identical SQL**; a test asserts the SQL contains only registry-approved fragments; limits for length, depth, terms, list size and pattern size; a pathological pattern such as many `*` against a long value completes quickly.
- **Fuzz:** a seeded random test (at least 5,000 cases) of random strings and mutations (delete, insert, swap characters) of valid queries. Nothing may throw anything other than handled results, and each case must finish in under 100 ms.
- **Simple filter converter:** for at least 25 filter combinations, the converted query returns the same ids as the structured filter.
- **Registered functions:** `iq_eq` and `iq_like` match the C# reference on a table of Unicode and ASCII cases, including on a fresh connection from the factory.
- **Docs and help:** the golden test for `QUERY-LANGUAGE.md`; every example in the examples data runs and returns its expected ids; the help model includes every field and error code.
- **API:** `q` success with paging and sort; `q` with errors gives 400 with `queryErrors`; `q` plus another filter gives 400; validate endpoint for valid and invalid input; help and from-filter endpoints; authentication required on all new endpoints; the `ids` endpoint accepts `q`.
- **CLI:** `query validate` caret output for a few errors; `query run` smoke test.
- **Performance (slow or explicit):** 50,000 seeded images, run 15 typical queries (tags, text, dates, nested `or`, `like`, `under`) and print timings. Compile time under 1 ms for typical queries. Report and explain any query over 300 ms with its query plan.

## Working rules

- Follow the master brief and earlier conventions. Where silent, choose the simplest option and note the assumption. If this prompt conflicts with the brief, follow this prompt, update the brief's section 11 text in your reply, and tell me.
- Keep the grammar, field registry, docs and help in one source of truth.
- Add any new dependency to `THIRD-PARTY-NOTICES.md`. Prefer none: the lexer and parser are hand-written.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux, with no warnings in Core and Api.
- Every required test exists and passes. List any you could not write and why.
- `docs/QUERY-LANGUAGE.md` is generated and checked in.
- Your final reply includes: new files, the list of extensions and deviations, decisions to confirm, and the manual checklist below with exact commands.

## How I will test Phase 5

1. `iconchest query docs` and read `docs/QUERY-LANGUAGE.md`. Check that it is clear and complete.
2. With the Phase 2 test folder scanned and a few tags created, run `iconchest query run` for: `tag == "dog"`, `type == png or type == jpg`, `date < 2026-10-30`, `title like "elepha*"`, `(width >= 256 and height >= 256) and size < 100kb`, `tag is empty and rating >= 4`, `elephant`, and `favourite`.
3. Run `iconchest query validate` on broken input such as `tag ==`, `tag == "dog" and (`, `tpye == png`, `width == 1.5`, `date == 2026-02-30`, `title == elep*`, `dog cat`, and `"abc`. Check the caret position and the suggestion.
4. Run `query explain` on a nested query and read the SQL. Confirm the user text only appears in parameters.
5. Try odd inputs: Unicode tag names, upper and lower case, a Windows-style folder path in `folder under "C:\Users\me\icons"`, and a very long query.
6. In Swagger, call `/api/images?q=...` with a valid and an invalid query. Call `/api/query/validate` and `/api/query/help`. Combine `q` with another filter and confirm 400.
7. Test `hidden`: hide an image, search with and without mentioning `hidden`.
8. Run `dotnet test`, then the slow performance test, and look at the timings.
