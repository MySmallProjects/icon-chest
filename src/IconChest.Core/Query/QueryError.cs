namespace IconChest.Core.Query;

public static class QueryErrorCodes
{
    public const string QueryTooLong = "query_too_long";
    public const string UnexpectedCharacter = "unexpected_character";
    public const string UnterminatedString = "unterminated_string";
    public const string UnexpectedToken = "unexpected_token";
    public const string UnexpectedEnd = "unexpected_end";
    public const string ExpectedExpression = "expected_expression";
    public const string ExpectedOperator = "expected_operator";
    public const string ExpectedValue = "expected_value";
    public const string MissingConnective = "missing_connective";
    public const string UnbalancedParenthesis = "unbalanced_parenthesis";
    public const string EmptyGroup = "empty_group";
    public const string EmptyList = "empty_list";
    public const string NestingTooDeep = "nesting_too_deep";
    public const string TooManyTerms = "too_many_terms";
    public const string TooManyValues = "too_many_values";
    public const string PatternTooLong = "pattern_too_long";
    public const string UnknownField = "unknown_field";
    public const string FieldNeedsOperator = "field_needs_operator";
    public const string OperatorNotAllowed = "operator_not_allowed";
    public const string InvalidValue = "invalid_value";
    public const string InvalidNumber = "invalid_number";
    public const string InvalidSize = "invalid_size";
    public const string InvalidDate = "invalid_date";
    public const string WildcardNotAllowed = "wildcard_not_allowed";
}

public sealed record QueryError(
    string Code,
    string Message,
    int Position,
    int Length,
    string? Suggestion = null);

