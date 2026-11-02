using System.Text;

namespace IconChest.Core.Query;

public static class QueryHelpGenerator
{
    public static readonly IReadOnlyList<ErrorHelpItem> ErrorDefinitions = new List<ErrorHelpItem>
    {
        new(QueryErrorCodes.QueryTooLong, "Query exceeds maximum limit of 2,000 characters.", "type == png ... (> 2000 chars)"),
        new(QueryErrorCodes.UnexpectedCharacter, "Encountered a character not permitted by lexical rules.", "width == 100 @"),
        new(QueryErrorCodes.UnterminatedString, "Quoted string has no closing quote.", "tag == \"dog"),
        new(QueryErrorCodes.UnexpectedToken, "Token appears where grammar does not allow it.", "and tag == \"ui\""),
        new(QueryErrorCodes.UnexpectedEnd, "Query ends abruptly mid-expression.", "tag == \"dog\" and"),
        new(QueryErrorCodes.ExpectedExpression, "Expected expression after connective or parenthesis.", "tag == \"dog\" and ("),
        new(QueryErrorCodes.ExpectedOperator, "Expected operator after field name.", "tag \"dog\""),
        new(QueryErrorCodes.ExpectedValue, "Expected value after operator.", "tag =="),
        new(QueryErrorCodes.MissingConnective, "Two adjacent terms with no connective (and/or).", "dog cat"),
        new(QueryErrorCodes.UnbalancedParenthesis, "Unmatched opening or closing parenthesis.", "(tag == \"ui\""),
        new(QueryErrorCodes.EmptyGroup, "Empty parentheses '()' are not permitted.", "()"),
        new(QueryErrorCodes.EmptyList, "Empty 'in ()' value list is not permitted.", "tag in ()"),
        new(QueryErrorCodes.NestingTooDeep, "Nesting depth exceeds maximum of 20 levels.", "(((((((((((((((((((((tag == \"ui\")))))))))))))))))))))"),
        new(QueryErrorCodes.TooManyTerms, "Query exceeds maximum of 200 terms.", "tag == 't1' and tag == 't2' ... (> 200 terms)"),
        new(QueryErrorCodes.TooManyValues, "'in' list exceeds maximum of 100 values.", "tag in (v1, v2, ... > 100 values)"),
        new(QueryErrorCodes.PatternTooLong, "Wildcard or like pattern exceeds 200 characters.", "title like \"a*... (> 200 chars)\""),
        new(QueryErrorCodes.UnknownField, "Field name is not recognized.", "tpye == png"),
        new(QueryErrorCodes.FieldNeedsOperator, "Non-boolean field used as lone word without operator.", "title"),
        new(QueryErrorCodes.OperatorNotAllowed, "Operator is not supported for this field type.", "filename > 100"),
        new(QueryErrorCodes.InvalidValue, "Value is not allowed for this field (bad format, relative path, etc.).", "type == mp4"),
        new(QueryErrorCodes.InvalidNumber, "Number is malformed, negative, or contains invalid decimals.", "width == 1.5"),
        new(QueryErrorCodes.InvalidSize, "Size string is malformed or has invalid unit.", "size == 500xyz"),
        new(QueryErrorCodes.InvalidDate, "Date format is malformed or not a valid calendar day.", "date == 2026-02-30"),
        new(QueryErrorCodes.WildcardNotAllowed, "Unquoted wildcards are not allowed after ==, !=, or in.", "title == elep*")
    };

    public static QueryHelpModel GetHelpModel()
    {
        var fields = FieldRegistry.Fields.Select(f => new FieldHelpItem(
            f.Name,
            f.Aliases,
            f.Type.ToString().ToLowerInvariant(),
            f.AllowedOperators.OrderBy(x => x).ToList(),
            f.Description,
            f.Examples)).ToList();

        var examples = QueryExamples.All.Select(e => new ExampleHelpItem(
            e.Text,
            e.Description,
            e.Category)).ToList();

        var grammar = new GrammarHelp(
            "IconChest Query Language Grammar (EBNF)",
            new[]
            {
                "query     = [ orExpr ] ;",
                "orExpr    = andExpr { OR andExpr } ;",
                "andExpr   = unary { AND unary } ;",
                "unary     = NOT unary | primary ;",
                "primary   = \"(\" orExpr \")\" | predicate ;",
                "predicate = word cmpOp value",
                "          | word [ NOT ] IN \"(\" value { \",\" value } \")\"",
                "          | word IS [ NOT ] EMPTY",
                "          | word [ NOT ] LIKE string",
                "          | word [ NOT ] CONTAINS string",
                "          | word [ NOT ] UNDER string",
                "          | word",
                "          | string ;",
                "value     = word | string ;",
                "cmpOp     = \"==\" | \"=\" | \"!=\" | \"<\" | \"<=\" | \">\" | \">=\" ;",
                "OR  = \"or\"  | \"||\" ;   AND = \"and\" | \"&&\" ;   NOT = \"not\" | \"!\" ;"
            });

        var limits = new LimitsHelp(
            QueryLexer.MaxQueryLength,
            QueryParser.MaxNestingDepth,
            QueryParser.MaxTerms,
            QueryParser.MaxInListValues,
            QueryParser.MaxPatternLength);

        return new QueryHelpModel
        {
            Fields = fields,
            ErrorCodes = ErrorDefinitions,
            Examples = examples,
            Grammar = grammar,
            Limits = limits
        };
    }

    public static string GenerateMarkdownDocumentation()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# IconChest Query Language Reference");
        sb.AppendLine();
        sb.AppendLine("IconChest features an advanced, expressive search language inspired by JQL and Wireshark. It operates over library image metadata with positional error diagnostics, two-valued logic, and parameterized SQL compilation.");
        sb.AppendLine();

        sb.AppendLine("## Overview & Design Principles");
        sb.AppendLine();
        sb.AppendLine("- **Safe & Parameterized**: User text is never concatenated into raw SQL. All query criteria compile to parameterized queries with byte-identical SQL for identical AST shapes.");
        sb.AppendLine("- **Two-Valued Logic**: Every predicate evaluates strictly to `true` or `false` (never unknown/null). For example, `width != 100` matches images where `width` is null because a null width is indeed not equal to 100.");
        sb.AppendLine("- **Deterministic Case & Unicode Folding**: Text comparisons and wildcard queries use full Unicode normalization (NFC) and case-folding via custom SQLite functions `iq_eq` and `iq_like`.");
        sb.AppendLine("- **Timezone-Aware Dates**: Date comparisons evaluate calendar days against local boundaries converted to UTC.");
        sb.AppendLine();

        sb.AppendLine("## Lexical Rules");
        sb.AppendLine();
        sb.AppendLine("- **Whitespace**: Spaces, tabs, and newlines separate tokens and are otherwise ignored.");
        sb.AppendLine("- **Symbols**: `(`, `)`, `,`, `==`, `=`, `!=`, `<`, `<=`, `>`, `>=`, `&&`, `||`, `!`. Note: A lone `&` or `|` is rejected.");
        sb.AppendLine("- **Keywords** (case-insensitive): `and`, `or`, `not`, `in`, `is`, `empty`, `like`, `contains`, `under`, `true`, `false`.");
        sb.AppendLine("- **Words**: Unquoted sequences of Unicode letters, digits, and `_`, `-`, `.`, `*`, `?`.");
        sb.AppendLine("- **Strings**: Double-quoted `\"...\"` or single-quoted `'...'`. Supports escape sequences `\\\\`, `\\\"`, `\\\'`, `\\*`, and `\\?`.");
        sb.AppendLine();

        sb.AppendLine("## Grammar");
        sb.AppendLine();
        sb.AppendLine("```ebnf");
        sb.AppendLine("query     = [ orExpr ] ;                 (* blank query matches all images *)");
        sb.AppendLine("orExpr    = andExpr { OR andExpr } ;");
        sb.AppendLine("andExpr   = unary { AND unary } ;");
        sb.AppendLine("unary     = NOT unary | primary ;");
        sb.AppendLine("primary   = \"(\" orExpr \")\" | predicate ;");
        sb.AppendLine("predicate = word cmpOp value");
        sb.AppendLine("          | word [ NOT ] IN \"(\" value { \",\" value } \")\"");
        sb.AppendLine("          | word IS [ NOT ] EMPTY");
        sb.AppendLine("          | word [ NOT ] LIKE string");
        sb.AppendLine("          | word [ NOT ] CONTAINS string");
        sb.AppendLine("          | word [ NOT ] UNDER string");
        sb.AppendLine("          | word                          (* boolean shorthand or bare term *)");
        sb.AppendLine("          | string ;                      (* bare search term *)");
        sb.AppendLine("value     = word | string ;");
        sb.AppendLine("cmpOp     = \"==\" | \"=\" | \"!=\" | \"<\" | \"<=\" | \">\" | \">=\" ;");
        sb.AppendLine("OR  = \"or\"  | \"||\" ;   AND = \"and\" | \"&&\" ;   NOT = \"not\" | \"!\" ;");
        sb.AppendLine("```");
        sb.AppendLine();

        sb.AppendLine("## Field Reference");
        sb.AppendLine();
        sb.AppendLine("| Field | Type | Allowed Operators | Description | Examples |");
        sb.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (var f in FieldRegistry.Fields)
        {
            var aliasesStr = f.Aliases.Count > 0 ? $" (alias: {string.Join(", ", f.Aliases)})" : "";
            var ops = string.Join(" ", f.AllowedOperators.OrderBy(x => x).Select(x => $"`{x}`"));
            var examples = string.Join("<br>", f.Examples.Select(e => $"`{e}`"));
            sb.AppendLine($"| `{f.Name}`{aliasesStr} | `{f.Type.ToString().ToLowerInvariant()}` | {ops} | {f.Description} | {examples} |");
        }
        sb.AppendLine();

        sb.AppendLine("## Operator Semantics & Null Rules");
        sb.AppendLine();
        sb.AppendLine("- **`==` / `=`**: Exact equality. For text and tags, NFC-normalized and case-insensitive. For dates, covers the entire day, month, or year.");
        sb.AppendLine("- **`!=`**: Exact inequality. Strictly `not ==`. Null values satisfy `!=` because a null value is not equal to any concrete value.");
        sb.AppendLine("- **`<`, `<=`, `>`, `>=`**: Ordering comparisons for integers, sizes, ratings, and dates. Null values satisfy no ordering comparisons.");
        sb.AppendLine("- **`in (...)` / `not in (...)`**: Matches if the field equals any value in the list. `not in` is its exact negation.");
        sb.AppendLine("- **`like` / `not like`**: Full wildcard pattern match where `*` matches any sequence of characters and `?` matches single characters. Patterns must match the entire field value.");
        sb.AppendLine("- **`contains` / `not contains`**: Literal substring match. Wildcard characters (`*`, `?`) have no special meaning.");
        sb.AppendLine("- **`under` / `not under`** (Folder only): Matches images whose directory is equal to the given path or located in any subfolder under it.");
        sb.AppendLine("- **`is empty` / `is not empty`**:");
        sb.AppendLine("  - Text / Format / Path: empty or null string.");
        sb.AppendLine("  - Numbers / Dates: null.");
        sb.AppendLine("  - Rating: 0 or null.");
        sb.AppendLine("  - Tag: image has no tags.");
        sb.AppendLine("- **Boolean Shorthand**: Specifying a lone boolean field name (e.g. `favourite`) evaluates as `favourite == true`. `!favourite` evaluates as `favourite != true`.");
        sb.AppendLine();

        sb.AppendLine("## Dates & Timezones");
        sb.AppendLine();
        sb.AppendLine("- Dates can be specified as `yyyy`, `yyyy-mm`, or `yyyy-mm-dd`.");
        sb.AppendLine("- A date represents the calendar day (or whole month/year) in the active timezone (local machine timezone by default), converted to exact UTC boundary instants `[startUtc, endUtc)`.");
        sb.AppendLine("- Comparisons across Daylight Saving Time (DST) transitions adjust automatically to the actual length of the local calendar day (e.g. 23 or 25 hours).");
        sb.AppendLine();

        sb.AppendLine("## Implicit Filters & The OR Quirk");
        sb.AppendLine();
        sb.AppendLine("To maintain compatibility with library browsing defaults:");
        sb.AppendLine("- **Hidden images** are excluded by default (`is_hidden = 0`) unless the field `hidden` is mentioned anywhere in the query syntax tree.");
        sb.AppendLine("- **Missing files** are excluded by default (`is_missing = 0`) unless the field `missing` is mentioned anywhere in the query syntax tree.");
        sb.AppendLine();
        sb.AppendLine("> [!IMPORTANT]");
        sb.AppendLine("> **The OR Quirk**: If `hidden` or `missing` is mentioned anywhere in the query (even in one branch of an `or`, or under a `not`), the implicit filter is disabled for the entire query.");
        sb.AppendLine("> For example, `type == png or hidden == true` will evaluate both branches without the implicit `is_hidden = 0` filter.");
        sb.AppendLine();

        sb.AppendLine("## Error Diagnostics");
        sb.AppendLine();
        sb.AppendLine("Every error returns exact UTF-16 code unit position, span length, diagnostic code, and helpful suggestions:");
        sb.AppendLine();
        sb.AppendLine("| Code | Description | Example Invalid Query |");
        sb.AppendLine("| --- | --- | --- |");
        foreach (var err in ErrorDefinitions)
        {
            sb.AppendLine($"| `{err.Code}` | {err.Description} | `{err.Example}` |");
        }
        sb.AppendLine();

        sb.AppendLine("## Cookbook of Examples");
        sb.AppendLine();
        var groups = QueryExamples.All.GroupBy(e => e.Category);
        foreach (var group in groups)
        {
            sb.AppendLine($"### {group.Key}");
            sb.AppendLine();
            sb.AppendLine("| Query | Description |");
            sb.AppendLine("| --- | --- |");
            foreach (var ex in group)
            {
                sb.AppendLine($"| `{ex.Text}` | {ex.Description} |");
            }
            sb.AppendLine();
        }

        sb.AppendLine("## System Limits");
        sb.AppendLine();
        sb.AppendLine($"- **Max Query Length**: {QueryLexer.MaxQueryLength:N0} characters");
        sb.AppendLine($"- **Max Nesting Depth**: {QueryParser.MaxNestingDepth} parentheses / not levels");
        sb.AppendLine($"- **Max Terms**: {QueryParser.MaxTerms} terms");
        sb.AppendLine($"- **Max In-List Values**: {QueryParser.MaxInListValues} values");
        sb.AppendLine($"- **Max Pattern Length**: {QueryParser.MaxPatternLength} characters");
        sb.AppendLine();

        sb.AppendLine("## Differences from Standard SQL");
        sb.AppendLine();
        sb.AppendLine("1. **No Three-Valued Logic (3VL)**: SQL uses NULL propagation where `NULL != 5` evaluates to NULL. IconChest uses two-valued logic where `width != 100` evaluates to true for NULL widths.");
        sb.AppendLine("2. **Tag Multi-Valued Matching**: In SQL, querying tags requires manual JOIN or EXISTS queries. In IconChest, `tag == \"ui\"` and `tag in (\"a\", \"b\")` transparently target image tag relations.");
        sb.AppendLine("3. **Directory Hierarchy (`under`)**: `folder under \"...\"` automatically handles directory key normalization and subfolder tree matching.");
        sb.AppendLine("4. **Partial Calendar Dates**: Partial dates like `date == 2026-05` seamlessly map to local calendar ranges converted to UTC timestamps.");
        sb.AppendLine("5. **Strict Connectives**: There is no implicit `AND`. Adjacent terms like `dog cat` produce a clear syntax error suggesting `and`.");

        return sb.ToString();
    }
}

