using System.Globalization;
using System.Text.RegularExpressions;
using IconChest.Core.Common;
using IconChest.Core.Scanning;

namespace IconChest.Core.Query;

public sealed class ValidationResult
{
    public bool IsValid => Errors.Count == 0;
    public IReadOnlyList<QueryError> Errors { get; init; } = Array.Empty<QueryError>();
    public bool MentionsHidden { get; init; }
    public bool MentionsMissing { get; init; }
    public BoundNode? BoundTree { get; init; }
}

public sealed class QueryValidator
{
    private static readonly Regex SizeRegex = new(@"^(\d+(?:\.\d+)?)\s*(b|kb|mb|gb)?$", RegexOptions.IgnoreCase);
    private static readonly Regex YearRegex = new(@"^\d{4}$");
    private static readonly Regex YearMonthRegex = new(@"^\d{4}-\d{2}$");
    private static readonly Regex FullDateRegex = new(@"^\d{4}-\d{2}-\d{2}$");

    private readonly TimeZoneInfo _timeZone;
    private readonly List<QueryError> _errors = new();
    private bool _mentionsHidden;
    private bool _mentionsMissing;

    public QueryValidator(TimeZoneInfo? timeZone = null)
    {
        _timeZone = timeZone ?? TimeZoneInfo.Local;
    }

    public static ValidationResult Validate(QueryNode? root, TimeZoneInfo? timeZone = null)
    {
        var validator = new QueryValidator(timeZone);
        if (root == null)
        {
            return new ValidationResult();
        }

        var bound = validator.Visit(root);
        var orderedErrors = validator._errors
            .OrderBy(e => e.Position)
            .Take(10)
            .ToList();

        return new ValidationResult
        {
            Errors = orderedErrors,
            MentionsHidden = validator._mentionsHidden,
            MentionsMissing = validator._mentionsMissing,
            BoundTree = orderedErrors.Count == 0 ? bound : null
        };
    }

    private BoundNode? Visit(QueryNode node)
    {
        return node switch
        {
            OrNode orNode => VisitOr(orNode),
            AndNode andNode => VisitAnd(andNode),
            NotNode notNode => VisitNot(notNode),
            ComparisonNode compNode => VisitComparison(compNode),
            InNode inNode => VisitIn(inNode),
            IsEmptyNode isEmptyNode => VisitIsEmpty(isEmptyNode),
            LikeNode likeNode => VisitLike(likeNode),
            ContainsNode containsNode => VisitContains(containsNode),
            UnderNode underNode => VisitUnder(underNode),
            BareTermNode bareNode => VisitBareTerm(bareNode),
            BooleanShorthandNode boolNode => VisitBooleanShorthand(boolNode),
            _ => null
        };
    }

    private BoundNode? VisitOr(OrNode node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        if (left == null || right == null) return null;
        return new BoundOrNode(left, right);
    }

    private BoundNode? VisitAnd(AndNode node)
    {
        var left = Visit(node.Left);
        var right = Visit(node.Right);
        if (left == null || right == null) return null;
        return new BoundAndNode(left, right);
    }

    private BoundNode? VisitNot(NotNode node)
    {
        var operand = Visit(node.Operand);
        if (operand == null) return null;
        return new BoundNotNode(operand);
    }

    private BoundNode? VisitBooleanShorthand(BooleanShorthandNode node)
    {
        var fieldName = node.Field.Text;
        if (!FieldRegistry.TryGet(fieldName, out var field) || field == null)
        {
            var suggestion = FieldRegistry.FindNearestField(fieldName);
            AddError(new QueryError(
                QueryErrorCodes.UnknownField,
                $"Unknown field '{fieldName}'.",
                node.Field.Position,
                node.Field.Length,
                suggestion));
            return null;
        }

        CheckMentions(field);

        if (field.Type != FieldType.Boolean)
        {
            AddError(new QueryError(
                QueryErrorCodes.FieldNeedsOperator,
                $"Field '{field.Name}' requires an operator and value (e.g. '{field.Name} == ...').",
                node.Field.Position,
                node.Field.Length));
            return null;
        }

        return new BoundComparisonNode(field, "==", true);
    }

    private BoundNode? VisitBareTerm(BareTermNode node)
    {
        var text = node.Term.Text;

        // Check if the term happens to be a lone non-boolean field name
        if (node.Term.Type == QueryTokenType.Word && FieldRegistry.TryGet(text, out var field) && field != null)
        {
            CheckMentions(field);
            if (field.Type == FieldType.Boolean)
            {
                return new BoundComparisonNode(field, "==", true);
            }

            AddError(new QueryError(
                QueryErrorCodes.FieldNeedsOperator,
                $"Field '{field.Name}' requires an operator and value.",
                node.Term.Position,
                node.Term.Length));
            return null;
        }

        bool isWildcard = text.Contains('*') || text.Contains('?');
        string pattern = isWildcard ? $"*{text}*" : text;
        return new BoundBareTermNode(text, pattern, isWildcard);
    }

    private BoundNode? VisitComparison(ComparisonNode node)
    {
        var field = ResolveField(node.Field);
        if (field == null) return null;

        var op = NormalizeOp(node.Op.Text);
        if (!field.SupportsOperator(op))
        {
            AddError(new QueryError(
                QueryErrorCodes.OperatorNotAllowed,
                $"Operator '{node.Op.Text}' is not allowed for field '{field.Name}'. Allowed operators: {string.Join(", ", field.AllowedOperators)}.",
                node.Op.Position,
                node.Op.Length));
            return null;
        }

        // Wildcard check on unquoted word value after ==, !=
        if (node.Value.Type == QueryTokenType.Word && (node.Value.Text.Contains('*') || node.Value.Text.Contains('?')))
        {
            AddError(new QueryError(
                QueryErrorCodes.WildcardNotAllowed,
                $"Wildcard characters are not allowed after '{node.Op.Text}'. Use 'like' instead.",
                node.Value.Position,
                node.Value.Length,
                "like"));
            return null;
        }

        if (field.Type == FieldType.Date)
        {
            if (!TryParseDateBounds(node.Value.Text, _timeZone, out var startUtc, out var endUtc))
            {
                AddError(new QueryError(
                    QueryErrorCodes.InvalidDate,
                    $"Invalid date format or non-existent calendar date '{node.Value.Text}'. Use yyyy, yyyy-mm, or yyyy-mm-dd.",
                    node.Value.Position,
                    node.Value.Length));
                return null;
            }

            return new BoundDateComparisonNode(
                field,
                op,
                UtcDateText.FormatBound(startUtc),
                UtcDateText.FormatBound(endUtc));
        }

        var convertedVal = ConvertValue(field, node.Value);
        if (convertedVal == null) return null;

        return new BoundComparisonNode(field, op, convertedVal);
    }

    private BoundNode? VisitIn(InNode node)
    {
        var field = ResolveField(node.Field);
        if (field == null) return null;

        if (!field.SupportsOperator("in"))
        {
            AddError(new QueryError(
                QueryErrorCodes.OperatorNotAllowed,
                $"Operator 'in' is not allowed for field '{field.Name}'. Allowed operators: {string.Join(", ", field.AllowedOperators)}.",
                node.Position,
                node.Length));
            return null;
        }

        var values = new List<object>();
        foreach (var valToken in node.Values)
        {
            if (valToken.Type == QueryTokenType.Word && (valToken.Text.Contains('*') || valToken.Text.Contains('?')))
            {
                AddError(new QueryError(
                    QueryErrorCodes.WildcardNotAllowed,
                    "Wildcard characters are not allowed in 'in' value lists.",
                    valToken.Position,
                    valToken.Length,
                    "use 'like' instead"));
                return null;
            }

            var converted = ConvertValue(field, valToken);
            if (converted == null) return null;
            values.Add(converted);
        }

        return new BoundInNode(field, node.IsNot, values);
    }

    private BoundNode? VisitIsEmpty(IsEmptyNode node)
    {
        var field = ResolveField(node.Field);
        if (field == null) return null;

        if (!field.SupportsOperator("is empty"))
        {
            AddError(new QueryError(
                QueryErrorCodes.OperatorNotAllowed,
                $"Operator 'is empty' is not allowed for field '{field.Name}'. Allowed operators: {string.Join(", ", field.AllowedOperators)}.",
                node.Position,
                node.Length));
            return null;
        }

        return new BoundIsEmptyNode(field, node.IsNot);
    }

    private BoundNode? VisitLike(LikeNode node)
    {
        var field = ResolveField(node.Field);
        if (field == null) return null;

        if (!field.SupportsOperator("like"))
        {
            AddError(new QueryError(
                QueryErrorCodes.OperatorNotAllowed,
                $"Operator 'like' is not allowed for field '{field.Name}'. Allowed operators: {string.Join(", ", field.AllowedOperators)}.",
                node.Position,
                node.Length));
            return null;
        }

        return new BoundLikeNode(field, node.IsNot, node.Pattern.Text);
    }

    private BoundNode? VisitContains(ContainsNode node)
    {
        var field = ResolveField(node.Field);
        if (field == null) return null;

        if (!field.SupportsOperator("contains"))
        {
            AddError(new QueryError(
                QueryErrorCodes.OperatorNotAllowed,
                $"Operator 'contains' is not allowed for field '{field.Name}'. Allowed operators: {string.Join(", ", field.AllowedOperators)}.",
                node.Position,
                node.Length));
            return null;
        }

        return new BoundContainsNode(field, node.IsNot, node.Value.Text);
    }

    private BoundNode? VisitUnder(UnderNode node)
    {
        var field = ResolveField(node.Field);
        if (field == null) return null;

        if (!field.SupportsOperator("under"))
        {
            AddError(new QueryError(
                QueryErrorCodes.OperatorNotAllowed,
                $"Operator 'under' is not allowed for field '{field.Name}'. Allowed operators: {string.Join(", ", field.AllowedOperators)}.",
                node.Position,
                node.Length));
            return null;
        }

        var rawPath = node.Path.Text;
        if (!IsAbsolutePath(rawPath))
        {
            AddError(new QueryError(
                QueryErrorCodes.InvalidValue,
                $"Folder path '{rawPath}' must be an absolute path.",
                node.Path.Position,
                node.Path.Length));
            return null;
        }

        var cleanPath = NormalizeFolderPath(rawPath);
        var dirKey = PathKeyProvider.Default.GetDirectoryKey(cleanPath);
        return new BoundUnderNode(field, node.IsNot, dirKey);
    }

    private FieldDefinition? ResolveField(QueryToken fieldToken)
    {
        var name = fieldToken.Text;
        if (!FieldRegistry.TryGet(name, out var field) || field == null)
        {
            var suggestion = FieldRegistry.FindNearestField(name);
            AddError(new QueryError(
                QueryErrorCodes.UnknownField,
                $"Unknown field '{name}'.",
                fieldToken.Position,
                fieldToken.Length,
                suggestion));
            return null;
        }

        CheckMentions(field);
        return field;
    }

    private void CheckMentions(FieldDefinition field)
    {
        if (field.Name.Equals("hidden", StringComparison.OrdinalIgnoreCase))
        {
            _mentionsHidden = true;
        }
        else if (field.Name.Equals("missing", StringComparison.OrdinalIgnoreCase))
        {
            _mentionsMissing = true;
        }
    }

    private object? ConvertValue(FieldDefinition field, QueryToken valToken)
    {
        var text = valToken.Text;

        switch (field.Type)
        {
            case FieldType.Format:
            {
                var lower = text.ToLowerInvariant();
                if (lower == "jpeg") lower = "jpg";
                if (!FieldRegistry.KnownFormats.Contains(lower))
                {
                    AddError(new QueryError(
                        QueryErrorCodes.InvalidValue,
                        $"Unknown image format '{text}'. Allowed formats: {string.Join(", ", FieldRegistry.KnownFormats)}.",
                        valToken.Position,
                        valToken.Length));
                    return null;
                }
                return lower;
            }

            case FieldType.Rating:
            {
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rating))
                {
                    AddError(new QueryError(
                        QueryErrorCodes.InvalidNumber,
                        $"Rating value '{text}' must be an integer.",
                        valToken.Position,
                        valToken.Length));
                    return null;
                }
                if (rating < 0 || rating > 5)
                {
                    AddError(new QueryError(
                        QueryErrorCodes.InvalidValue,
                        $"Rating value '{text}' must be between 0 and 5.",
                        valToken.Position,
                        valToken.Length));
                    return null;
                }
                return rating;
            }

            case FieldType.Integer:
            {
                if (!long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intVal) || intVal < 0)
                {
                    AddError(new QueryError(
                        QueryErrorCodes.InvalidNumber,
                        $"Invalid integer value '{text}'. Expected a non-negative whole number.",
                        valToken.Position,
                        valToken.Length));
                    return null;
                }
                return intVal;
            }

            case FieldType.Size:
            {
                var match = SizeRegex.Match(text.Trim());
                if (!match.Success)
                {
                    AddError(new QueryError(
                        QueryErrorCodes.InvalidSize,
                        $"Invalid size value '{text}'. Use a number with optional unit b, kb, mb, gb (e.g., 500kb).",
                        valToken.Position,
                        valToken.Length));
                    return null;
                }

                if (!double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) || num < 0)
                {
                    AddError(new QueryError(
                        QueryErrorCodes.InvalidSize,
                        $"Invalid size number in '{text}'.",
                        valToken.Position,
                        valToken.Length));
                    return null;
                }

                var unit = match.Groups[2].Success ? match.Groups[2].Value.ToLowerInvariant() : "";
                long multiplier = unit switch
                {
                    "kb" => 1024L,
                    "mb" => 1024L * 1024,
                    "gb" => 1024L * 1024 * 1024,
                    _ => 1L
                };

                long bytes = (long)Math.Round(num * multiplier, MidpointRounding.AwayFromZero);
                return bytes;
            }

            case FieldType.Boolean:
            {
                if (bool.TryParse(text, out var b))
                {
                    return b;
                }
                AddError(new QueryError(
                    QueryErrorCodes.InvalidValue,
                    $"Invalid boolean value '{text}'. Expected 'true' or 'false'.",
                    valToken.Position,
                    valToken.Length));
                return null;
            }

            case FieldType.Path:
            {
                if (!IsAbsolutePath(text))
                {
                    AddError(new QueryError(
                        QueryErrorCodes.InvalidValue,
                        $"Folder path '{text}' must be an absolute path.",
                        valToken.Position,
                        valToken.Length));
                    return null;
                }
                return NormalizeFolderPath(text);
            }

            case FieldType.Text:
            case FieldType.MultiText:
            default:
                return text;
        }
    }

    public static bool IsAbsolutePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (path.StartsWith('/') || path.StartsWith('\\')) return true;
        if (path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\')) return true;
        return false;
    }

    public static string NormalizeFolderPath(string path)
    {
        var clean = path.Replace('\\', '/').Trim();
        while (clean.Length > 1 && clean.EndsWith('/'))
        {
            clean = clean[..^1];
        }
        return clean;
    }

    public static bool TryParseDateBounds(
        string raw,
        TimeZoneInfo timeZone,
        out DateTime startUtc,
        out DateTime endUtc)
    {
        startUtc = default;
        endUtc = default;

        if (string.IsNullOrWhiteSpace(raw)) return false;
        var trimmed = raw.Trim();

        DateTime startLocal;
        DateTime endLocal;

        if (YearRegex.IsMatch(trimmed))
        {
            if (!int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) || y < 1 || y > 9998)
                return false;

            startLocal = new DateTime(y, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            endLocal = new DateTime(y + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        }
        else if (YearMonthRegex.IsMatch(trimmed))
        {
            int y = int.Parse(trimmed[..4], CultureInfo.InvariantCulture);
            int m = int.Parse(trimmed[5..7], CultureInfo.InvariantCulture);
            if (m < 1 || m > 12 || y < 1 || y > 9998) return false;

            startLocal = new DateTime(y, m, 1, 0, 0, 0, DateTimeKind.Unspecified);
            endLocal = m == 12
                ? new DateTime(y + 1, 1, 1, 0, 0, 0, DateTimeKind.Unspecified)
                : new DateTime(y, m + 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
        }
        else if (FullDateRegex.IsMatch(trimmed))
        {
            if (!DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                return false;

            startLocal = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, DateTimeKind.Unspecified);
            endLocal = startLocal.AddDays(1);
        }
        else
        {
            return false;
        }

        startUtc = ConvertLocalInstantToUtc(startLocal, timeZone);
        endUtc = ConvertLocalInstantToUtc(endLocal, timeZone);
        return true;
    }

    private static DateTime ConvertLocalInstantToUtc(DateTime local, TimeZoneInfo tz)
    {
        if (tz.IsInvalidTime(local))
        {
            // During DST transition gap, advance by 1 hour
            local = local.AddHours(1);
        }
        return TimeZoneInfo.ConvertTimeToUtc(local, tz);
    }

    private static string NormalizeOp(string op) => op == "=" ? "==" : op;

    private void AddError(QueryError error)
    {
        _errors.Add(error);
    }
}

