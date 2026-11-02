namespace IconChest.Core.Query;

public sealed record QueryCompilationResult(
    bool IsValid,
    CompiledQuery? Query,
    IReadOnlyList<QueryError> Errors,
    string? NormalizedText,
    bool MentionsHidden,
    bool MentionsMissing);

public sealed record QueryValidationResult(
    bool IsValid,
    IReadOnlyList<QueryError> Errors,
    string? NormalizedText,
    bool MentionsHidden,
    bool MentionsMissing);

public static class QueryEngine
{
    public static QueryValidationResult Validate(string? text, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new QueryValidationResult(true, Array.Empty<QueryError>(), string.Empty, false, false);
        }

        var (ast, syntaxError) = QueryParser.Parse(text);
        if (syntaxError != null)
        {
            return new QueryValidationResult(false, new[] { syntaxError }, null, false, false);
        }

        if (ast == null)
        {
            return new QueryValidationResult(true, Array.Empty<QueryError>(), string.Empty, false, false);
        }

        var validation = QueryValidator.Validate(ast, timeZone);
        if (!validation.IsValid)
        {
            return new QueryValidationResult(
                false,
                validation.Errors,
                null,
                validation.MentionsHidden,
                validation.MentionsMissing);
        }

        string normalized = QueryFormatter.Format(ast);
        return new QueryValidationResult(
            true,
            Array.Empty<QueryError>(),
            normalized,
            validation.MentionsHidden,
            validation.MentionsMissing);
    }

    public static QueryCompilationResult ParseAndCompile(string? text, TimeZoneInfo? timeZone = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            var emptyVal = new ValidationResult { BoundTree = null };
            var emptyCompiled = QueryCompiler.Compile(emptyVal);
            return new QueryCompilationResult(true, emptyCompiled, Array.Empty<QueryError>(), string.Empty, false, false);
        }

        var (ast, syntaxError) = QueryParser.Parse(text);
        if (syntaxError != null)
        {
            return new QueryCompilationResult(false, null, new[] { syntaxError }, null, false, false);
        }

        if (ast == null)
        {
            var emptyVal = new ValidationResult { BoundTree = null };
            var emptyCompiled = QueryCompiler.Compile(emptyVal);
            return new QueryCompilationResult(true, emptyCompiled, Array.Empty<QueryError>(), string.Empty, false, false);
        }

        var validation = QueryValidator.Validate(ast, timeZone);
        if (!validation.IsValid)
        {
            return new QueryCompilationResult(
                false,
                null,
                validation.Errors,
                null,
                validation.MentionsHidden,
                validation.MentionsMissing);
        }

        var compiled = QueryCompiler.Compile(validation);
        string normalized = QueryFormatter.Format(ast);

        return new QueryCompilationResult(
            true,
            compiled,
            Array.Empty<QueryError>(),
            normalized,
            validation.MentionsHidden,
            validation.MentionsMissing);
    }
}

