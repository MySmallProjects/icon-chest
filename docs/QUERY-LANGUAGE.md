# IconChest Query Language Reference

IconChest features an advanced, expressive search language inspired by JQL and Wireshark. It operates over library image metadata with positional error diagnostics, two-valued logic, and parameterized SQL compilation.

## Overview & Design Principles

- **Safe & Parameterized**: User text is never concatenated into raw SQL. All query criteria compile to parameterized queries with byte-identical SQL for identical AST shapes.
- **Two-Valued Logic**: Every predicate evaluates strictly to `true` or `false` (never unknown/null). For example, `width != 100` matches images where `width` is null because a null width is indeed not equal to 100.
- **Deterministic Case & Unicode Folding**: Text comparisons and wildcard queries use full Unicode normalization (NFC) and case-folding via custom SQLite functions `iq_eq` and `iq_like`.
- **Timezone-Aware Dates**: Date comparisons evaluate calendar days against local boundaries converted to UTC.

## Lexical Rules

- **Whitespace**: Spaces, tabs, and newlines separate tokens and are otherwise ignored.
- **Symbols**: `(`, `)`, `,`, `==`, `=`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`. Note: A lone `&` or `|` is rejected.
- **Keywords** (case-insensitive): `and`, `or`, `not`, `in`, `is`, `empty`, `like`, `contains`, `under`, `true`, `false`.
- **Words**: Unquoted sequences of Unicode letters, digits, and `_`, `-`, `.`, `*`, `?`.
- **Strings**: Double-quoted `"..."` or single-quoted `'...'`. Supports escape sequences `\\`, `\"`, `\'`, `\*`, and `\?`.

## Grammar

```ebnf
query     = [ orExpr ] ;                 (* blank query matches all images *)
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
          | word                          (* boolean shorthand or bare term *)
          | string ;                      (* bare search term *)
value     = word | string ;
cmpOp     = "==" | "=" | "!=" | "<" | "<=" | ">" | ">=" ;
OR  = "or"  | "||" ;   AND = "and" | "&&" ;   NOT = "not" | "!" ;
```

## Field Reference

| Field | Type | Allowed Operators | Description | Examples |
| --- | --- | --- | --- | --- |
| `type` | `format` | `!=` `=` `==` `in` `like` | Image format (png, jpg, jpeg, gif, webp, svg, bmp, ico). | `type == png`<br>`type in (jpg, png)`<br>`type like "p*"` |
| `filename` | `text` | `!=` `=` `==` `contains` `in` `is empty` `like` | File name with extension. | `filename == "icon.svg"`<br>`filename like "*.png"`<br>`filename contains "logo"` |
| `title` | `text` | `!=` `=` `==` `contains` `in` `is empty` `like` | Editable image title. | `title is empty`<br>`title contains "arrow"`<br>`title like "Search*"` |
| `description` | `text` | `!=` `=` `==` `contains` `in` `is empty` `like` | Editable image description. | `description is not empty`<br>`description contains "button"` |
| `folder` | `path` | `!=` `=` `==` `contains` `in` `like` `under` | Directory path of the image file. | `folder under "/home/user/icons"`<br>`folder == "/home/user/icons/svg"` |
| `tag` | `multitext` | `!=` `=` `==` `contains` `in` `is empty` `like` | Image tags (multi-valued). | `tag == "ui"`<br>`tag in ("icon", "logo")`<br>`tag is empty`<br>`tag like "app*"` |
| `width` | `integer` | `!=` `<` `<=` `=` `==` `>` `>=` `in` `is empty` | Image width in pixels. | `width >= 256`<br>`width in (16, 32, 64)`<br>`width is empty` |
| `height` | `integer` | `!=` `<` `<=` `=` `==` `>` `>=` `in` `is empty` | Image height in pixels. | `height == 128`<br>`height < 500`<br>`height is empty` |
| `size` | `size` | `!=` `<` `<=` `=` `==` `>` `>=` `in` | File size in bytes or formatted units (e.g., 100kb, 1.5mb). | `size < 50kb`<br>`size >= 1mb`<br>`size in (1024, 2048)` |
| `rating` | `rating` | `!=` `<` `<=` `=` `==` `>` `>=` `in` `is empty` | Rating from 0 to 5 (0 means unrated and is empty). | `rating >= 4`<br>`rating == 5`<br>`rating is empty` |
| `favourite` (alias: favorite) | `boolean` | `!=` `=` `==` | Favourite flag. | `favourite`<br>`favorite == true`<br>`!favourite` |
| `hidden` | `boolean` | `!=` `=` `==` | Hidden flag. Mentioning hidden overrides default exclusion. | `hidden`<br>`hidden == true`<br>`hidden == false` |
| `missing` | `boolean` | `!=` `=` `==` | Missing file flag. Mentioning missing overrides default exclusion. | `missing`<br>`missing == false`<br>`missing == true` |
| `duplicate` | `boolean` | `!=` `=` `==` | Duplicate content flag. | `duplicate`<br>`duplicate == true`<br>`duplicate == false` |
| `transparent` | `boolean` | `!=` `=` `==` | Transparency flag (true, false, or null when unknown). | `transparent`<br>`transparent == true`<br>`transparent == false` |
| `date` | `date` | `!=` `<` `<=` `=` `==` `>` `>=` `is empty` | Effective date (taken override, EXIF date, created, or modified). | `date >= 2026-01-01`<br>`date == 2026`<br>`date == 2026-05` |
| `created` | `date` | `!=` `<` `<=` `=` `==` `>` `>=` `is empty` | File creation date. | `created >= 2026-01-01`<br>`created is empty` |
| `modified` | `date` | `!=` `<` `<=` `=` `==` `>` `>=` `is empty` | File modification date. | `modified >= 2026-06-01`<br>`modified < 2026-10-31` |
| `taken` | `date` | `!=` `<` `<=` `=` `==` `>` `>=` `is empty` | Date photo was taken (override or EXIF date). | `taken is not empty`<br>`taken == 2025` |
| `added` | `date` | `!=` `<` `<=` `=` `==` `>` `>=` `is empty` | Date the image was indexed in IconChest. | `added >= 2026-01-01`<br>`added == 2026-11` |

## Operator Semantics & Null Rules

- **`==` / `=`**: Exact equality. For text and tags, NFC-normalized and case-insensitive. For dates, covers the entire day, month, or year.
- **`!=`**: Exact inequality. Strictly `not ==`. Null values satisfy `!=` because a null value is not equal to any concrete value.
- **`<`, `<=`, `>`, `>=`**: Ordering comparisons for integers, sizes, ratings, and dates. Null values satisfy no ordering comparisons.
- **`in (...)` / `not in (...)`**: Matches if the field equals any value in the list. `not in` is its exact negation.
- **`like` / `not like`**: Full wildcard pattern match where `*` matches any sequence of characters and `?` matches single characters. Patterns must match the entire field value.
- **`contains` / `not contains`**: Literal substring match. Wildcard characters (`*`, `?`) have no special meaning.
- **`under` / `not under`** (Folder only): Matches images whose directory is equal to the given path or located in any subfolder under it.
- **`is empty` / `is not empty`**:
  - Text / Format / Path: empty or null string.
  - Numbers / Dates: null.
  - Rating: 0 or null.
  - Tag: image has no tags.
- **Boolean Shorthand**: Specifying a lone boolean field name (e.g. `favourite`) evaluates as `favourite == true`. `!favourite` evaluates as `favourite != true`.

## Dates & Timezones

- Dates can be specified as `yyyy`, `yyyy-mm`, or `yyyy-mm-dd`.
- A date represents the calendar day (or whole month/year) in the active timezone (local machine timezone by default), converted to exact UTC boundary instants `[startUtc, endUtc)`.
- Comparisons across Daylight Saving Time (DST) transitions adjust automatically to the actual length of the local calendar day (e.g. 23 or 25 hours).

## Implicit Filters & The OR Quirk

To maintain compatibility with library browsing defaults:
- **Hidden images** are excluded by default (`is_hidden = 0`) unless the field `hidden` is mentioned anywhere in the query syntax tree.
- **Missing files** are excluded by default (`is_missing = 0`) unless the field `missing` is mentioned anywhere in the query syntax tree.

> [!IMPORTANT]
> **The OR Quirk**: If `hidden` or `missing` is mentioned anywhere in the query (even in one branch of an `or`, or under a `not`), the implicit filter is disabled for the entire query.
> For example, `type == png or hidden == true` will evaluate both branches without the implicit `is_hidden = 0` filter.

## Error Diagnostics

Every error returns exact UTF-16 code unit position, span length, diagnostic code, and helpful suggestions:

| Code | Description | Example Invalid Query |
| --- | --- | --- |
| `query_too_long` | Query exceeds maximum limit of 2,000 characters. | `type == png ... (> 2000 chars)` |
| `unexpected_character` | Encountered a character not permitted by lexical rules. | `width == 100 @` |
| `unterminated_string` | Quoted string has no closing quote. | `tag == "dog` |
| `unexpected_token` | Token appears where grammar does not allow it. | `and tag == "ui"` |
| `unexpected_end` | Query ends abruptly mid-expression. | `tag == "dog" and` |
| `expected_expression` | Expected expression after connective or parenthesis. | `tag == "dog" and (` |
| `expected_operator` | Expected operator after field name. | `tag "dog"` |
| `expected_value` | Expected value after operator. | `tag ==` |
| `missing_connective` | Two adjacent terms with no connective (and/or). | `dog cat` |
| `unbalanced_parenthesis` | Unmatched opening or closing parenthesis. | `(tag == "ui"` |
| `empty_group` | Empty parentheses '()' are not permitted. | `()` |
| `empty_list` | Empty 'in ()' value list is not permitted. | `tag in ()` |
| `nesting_too_deep` | Nesting depth exceeds maximum of 20 levels. | `(((((((((((((((((((((tag == "ui")))))))))))))))))))))` |
| `too_many_terms` | Query exceeds maximum of 200 terms. | `tag == 't1' and tag == 't2' ... (> 200 terms)` |
| `too_many_values` | 'in' list exceeds maximum of 100 values. | `tag in (v1, v2, ... > 100 values)` |
| `pattern_too_long` | Wildcard or like pattern exceeds 200 characters. | `title like "a*... (> 200 chars)"` |
| `unknown_field` | Field name is not recognized. | `tpye == png` |
| `field_needs_operator` | Non-boolean field used as lone word without operator. | `title` |
| `operator_not_allowed` | Operator is not supported for this field type. | `filename > 100` |
| `invalid_value` | Value is not allowed for this field (bad format, relative path, etc.). | `type == mp4` |
| `invalid_number` | Number is malformed, negative, or contains invalid decimals. | `width == 1.5` |
| `invalid_size` | Size string is malformed or has invalid unit. | `size == 500xyz` |
| `invalid_date` | Date format is malformed or not a valid calendar day. | `date == 2026-02-30` |
| `wildcard_not_allowed` | Unquoted wildcards are not allowed after ==, !=, or in. | `title == elep*` |

## Cookbook of Examples

### Format & Dimensions

| Query | Description |
| --- | --- |
| `type == png` | Find all PNG images |
| `type in (jpg, png, webp)` | Find images in common web formats |
| `type like "p*"` | Find formats starting with letter 'p' |
| `width >= 256 and height >= 256` | High-resolution square or rectangular icons |
| `width == height` | Find perfectly square images |
| `width in (16, 32, 48, 64)` | Standard favicon or toolbar sizes |
| `width is empty` | Images where dimensions could not be read |
| `size < 50kb` | Small files under 50 kilobytes |
| `size >= 1mb` | Large image files at or above 1 megabyte |

### Tags & Organization

| Query | Description |
| --- | --- |
| `tag == "ui"` | Images tagged with 'ui' |
| `tag != "archived"` | Images without the 'archived' tag |
| `tag in ("icon", "logo", "badge")` | Images with any of icon, logo, or badge |
| `tag like "app*"` | Tags starting with 'app' (e.g. apple, application) |
| `tag contains "nav"` | Tags containing the substring 'nav' |
| `tag is empty` | Untagged images needing organization |
| `tag is not empty` | All images that have at least one tag |
| `tag == "ui" and tag == "dark"` | Images that have both 'ui' and 'dark' tags |

### Ratings & Favorites

| Query | Description |
| --- | --- |
| `favourite` | All favourite images using boolean shorthand |
| `!favourite and rating >= 4` | High-rated images that are not yet marked favourite |
| `rating == 5` | Five-star images |
| `rating is empty` | Unrated images (rating 0) |
| `rating >= 3 and rating <= 5` | Rated three stars or higher |

### Dates & Timestamps

| Query | Description |
| --- | --- |
| `date == 2026` | Images with effective date in calendar year 2026 |
| `date == 2026-05` | Images with effective date in May 2026 |
| `date >= 2026-01-01 and date <= 2026-12-31` | Images from the year 2026 using explicit bounds |
| `created >= 2026-06-01` | Files created in or after June 2026 |
| `modified < 2025-01-01` | Files not modified since before 2025 |
| `taken is not empty` | Photos having an EXIF or overridden taken date |
| `added == 2026-11` | Images indexed in IconChest during November 2026 |

### Folder & Paths

| Query | Description |
| --- | --- |
| `folder == "/home/user/icons/svg"` | Exact directory match |
| `folder under "/home/user/icons"` | All images inside folder or any subfolder |
| `folder like "*buttons*"` | Folders matching pattern with 'buttons' |

### Text & Metadata

| Query | Description |
| --- | --- |
| `title is empty` | Images without a custom title |
| `title contains "arrow"` | Titles containing the literal word 'arrow' |
| `filename like "*.svg"` | SVG files by filename extension |
| `elephant` | Bare search term: matches 'elephant' in filename, title, or description |
| `"search icon*"` | Search term with space and wildcard |

### Flags & System State

| Query | Description |
| --- | --- |
| `hidden` | Images marked as hidden (overriding implicit hidden exclusion) |
| `missing == true` | Missing files on disk (overriding implicit missing exclusion) |
| `duplicate` | Duplicate content files |
| `transparent` | Images with transparency alpha channel |

### Complex Combinations

| Query | Description |
| --- | --- |
| `(width >= 256 and height >= 256) and size < 100kb` | Large dimensions with compact file size |
| `tag is empty and rating >= 4` | Untagged top-rated images |
| `(type == png or type == svg) and (tag == "ui" or tag == "web")` | Web icon assets in PNG or SVG |
| `not (tag == "draft" or hidden)` | Exclude drafts and hidden images |

## System Limits

- **Max Query Length**: 2,000 characters
- **Max Nesting Depth**: 20 parentheses / not levels
- **Max Terms**: 200 terms
- **Max In-List Values**: 100 values
- **Max Pattern Length**: 200 characters

## Differences from Standard SQL

1. **No Three-Valued Logic (3VL)**: SQL uses NULL propagation where `NULL != 5` evaluates to NULL. IconChest uses two-valued logic where `width != 100` evaluates to true for NULL widths.
2. **Tag Multi-Valued Matching**: In SQL, querying tags requires manual JOIN or EXISTS queries. In IconChest, `tag == "ui"` and `tag in ("a", "b")` transparently target image tag relations.
3. **Directory Hierarchy (`under`)**: `folder under "..."` automatically handles directory key normalization and subfolder tree matching.
4. **Partial Calendar Dates**: Partial dates like `date == 2026-05` seamlessly map to local calendar ranges converted to UTC timestamps.
5. **Strict Connectives**: There is no implicit `AND`. Adjacent terms like `dog cat` produce a clear syntax error suggesting `and`.
