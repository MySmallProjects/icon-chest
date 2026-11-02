using System.Text;
using IconChest.Core.Scanning;

namespace IconChest.Core.Query;

public sealed class QueryCompiler
{
    private readonly Dictionary<string, object?> _parameters = new();
    private int _paramCounter;

    public static CompiledQuery Compile(ValidationResult validation)
    {
        if (!validation.IsValid)
        {
            throw new InvalidOperationException("Cannot compile query with validation errors.");
        }

        var compiler = new QueryCompiler();
        string whereSql;

        if (validation.BoundTree == null)
        {
            whereSql = "1 = 1";
        }
        else
        {
            whereSql = compiler.CompileNode(validation.BoundTree);
        }

        // Apply implicit filters if not mentioned
        var conditions = new List<string> { $"({whereSql})" };
        if (!validation.MentionsHidden)
        {
            conditions.Add("image.is_hidden = 0");
        }
        if (!validation.MentionsMissing)
        {
            conditions.Add("image.is_missing = 0");
        }

        string finalSql = string.Join(" AND ", conditions);

        return new CompiledQuery(
            finalSql,
            compiler._parameters,
            validation.MentionsHidden,
            validation.MentionsMissing);
    }

    private string CompileNode(BoundNode node)
    {
        return node switch
        {
            BoundOrNode orNode => $"({CompileNode(orNode.Left)} OR {CompileNode(orNode.Right)})",
            BoundAndNode andNode => $"({CompileNode(andNode.Left)} AND {CompileNode(andNode.Right)})",
            BoundNotNode notNode => $"({CompileNode(notNode.Operand)} IS NOT 1)",
            BoundComparisonNode compNode => CompileComparison(compNode),
            BoundDateComparisonNode dateComp => CompileDateComparison(dateComp),
            BoundInNode inNode => CompileIn(inNode),
            BoundIsEmptyNode isEmpty => CompileIsEmpty(isEmpty),
            BoundLikeNode likeNode => CompileLike(likeNode),
            BoundContainsNode contains => CompileContains(contains),
            BoundUnderNode under => CompileUnder(under),
            BoundBareTermNode bare => CompileBareTerm(bare),
            _ => throw new InvalidOperationException($"Unsupported bound node type: {node.GetType().Name}")
        };
    }

    private string CompileComparison(BoundComparisonNode node)
    {
        var field = node.Field;
        var op = node.Operator;

        if (field.Type == FieldType.MultiText)
        {
            // Tag field
            var pName = NextParam(node.Value);
            if (op == "==")
            {
                return $"(EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND iq_eq(t.name, @{pName}) = 1))";
            }
            if (op == "!=")
            {
                return $"(NOT EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND iq_eq(t.name, @{pName}) = 1))";
            }
            throw new InvalidOperationException($"Unsupported operator '{op}' on multi-text field.");
        }

        if (field.Type == FieldType.Path)
        {
            var pathStr = (string)node.Value;
            var dirKey = PathKeyProvider.Default.GetDirectoryKey(pathStr);
            var pName = NextParam(dirKey);
            if (op == "==")
            {
                return $"(image.directory_key = @{pName})";
            }
            if (op == "!=")
            {
                return $"(image.directory_key IS NULL OR image.directory_key != @{pName})";
            }
            throw new InvalidOperationException($"Unsupported comparison operator '{op}' on path field.");
        }

        if (field.Type == FieldType.Boolean)
        {
            bool val = (bool)node.Value;
            string col = $"image.{field.SqlColumn}";

            if (op == "==")
            {
                return val ? $"({col} = 1)" : $"({col} IS NOT NULL AND {col} = 0)";
            }
            if (op == "!=")
            {
                return val ? $"({col} IS NOT 1)" : $"({col} IS NOT 0)";
            }
            throw new InvalidOperationException($"Unsupported operator '{op}' on boolean field.");
        }

        if (field.Type == FieldType.Format)
        {
            var pName = NextParam(node.Value);
            if (op == "==")
            {
                return $"(iq_eq(image.format, @{pName}) = 1)";
            }
            if (op == "!=")
            {
                return $"(iq_eq(image.format, @{pName}) = 0)";
            }
            throw new InvalidOperationException($"Unsupported operator '{op}' on format field.");
        }

        if (field.Type == FieldType.Text)
        {
            var pName = NextParam(node.Value);
            string col = $"image.{field.SqlColumn}";
            if (op == "==")
            {
                return $"(iq_eq({col}, @{pName}) = 1)";
            }
            if (op == "!=")
            {
                return $"({col} IS NULL OR iq_eq({col}, @{pName}) = 0)";
            }
            throw new InvalidOperationException($"Unsupported operator '{op}' on text field.");
        }

        // Numeric fields: width, height, size, rating
        {
            var pName = NextParam(node.Value);
            string col = $"image.{field.SqlColumn}";

            return op switch
            {
                "==" => $"({col} = @{pName})",
                "!=" => $"({col} IS NULL OR {col} != @{pName})",
                "<" => $"({col} IS NOT NULL AND {col} < @{pName})",
                "<=" => $"({col} IS NOT NULL AND {col} <= @{pName})",
                ">" => $"({col} IS NOT NULL AND {col} > @{pName})",
                ">=" => $"({col} IS NOT NULL AND {col} >= @{pName})",
                _ => throw new InvalidOperationException($"Unsupported numeric operator '{op}'.")
            };
        }
    }

    private string CompileDateComparison(BoundDateComparisonNode node)
    {
        string col = node.Field.Name.Equals("taken", StringComparison.OrdinalIgnoreCase)
            ? "COALESCE(image.taken_override_utc, image.exif_date_utc)"
            : $"image.{node.Field.SqlColumn}";

        var pStart = NextParam(node.BoundStartUtc);
        var pEnd = NextParam(node.BoundEndUtc);

        return node.Operator switch
        {
            "==" => $"({col} IS NOT NULL AND {col} >= @{pStart} AND {col} < @{pEnd})",
            "!=" => $"({col} IS NULL OR {col} < @{pStart} OR {col} >= @{pEnd})",
            "<" => $"({col} IS NOT NULL AND {col} < @{pStart})",
            "<=" => $"({col} IS NOT NULL AND {col} < @{pEnd})",
            ">" => $"({col} IS NOT NULL AND {col} >= @{pEnd})",
            ">=" => $"({col} IS NOT NULL AND {col} >= @{pStart})",
            _ => throw new InvalidOperationException($"Unsupported date operator '{node.Operator}'.")
        };
    }

    private string CompileIn(BoundInNode node)
    {
        var field = node.Field;

        if (field.Type == FieldType.MultiText)
        {
            // Tag field in list
            var orParts = new List<string>(node.Values.Count);
            foreach (var v in node.Values)
            {
                var p = NextParam(v);
                orParts.Add($"iq_eq(t.name, @{p}) = 1");
            }
            string innerCond = orParts.Count > 0 ? string.Join(" OR ", orParts) : "1 = 0";

            return node.IsNot
                ? $"(NOT EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND ({innerCond})))"
                : $"(EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND ({innerCond})))";
        }

        if (field.Type == FieldType.Path)
        {
            var pNames = new List<string>(node.Values.Count);
            foreach (var v in node.Values)
            {
                var dirKey = PathKeyProvider.Default.GetDirectoryKey((string)v);
                pNames.Add($"@{NextParam(dirKey)}");
            }
            string list = string.Join(", ", pNames);
            return node.IsNot
                ? $"(image.directory_key IS NULL OR image.directory_key NOT IN ({list}))"
                : $"(image.directory_key IN ({list}))";
        }

        if (field.Type == FieldType.Format)
        {
            var orParts = new List<string>(node.Values.Count);
            foreach (var v in node.Values)
            {
                var p = NextParam(v);
                orParts.Add($"iq_eq(image.format, @{p}) = 1");
            }
            string inner = string.Join(" OR ", orParts);
            return node.IsNot ? $"({inner}) IS NOT 1" : $"({inner})";
        }

        if (field.Type == FieldType.Text)
        {
            var orParts = new List<string>(node.Values.Count);
            string col = $"image.{field.SqlColumn}";
            foreach (var v in node.Values)
            {
                var p = NextParam(v);
                orParts.Add($"iq_eq({col}, @{p}) = 1");
            }
            string inner = string.Join(" OR ", orParts);
            return node.IsNot ? $"({col} IS NULL OR ({inner}) IS NOT 1)" : $"({inner})";
        }

        // Numeric fields: width, height, size, rating
        {
            string col = $"image.{field.SqlColumn}";
            var pNames = new List<string>(node.Values.Count);
            foreach (var v in node.Values)
            {
                pNames.Add($"@{NextParam(v)}");
            }
            string list = string.Join(", ", pNames);
            return node.IsNot
                ? $"({col} IS NULL OR {col} NOT IN ({list}))"
                : $"({col} IS NOT NULL AND {col} IN ({list}))";
        }
    }

    private static string CompileIsEmpty(BoundIsEmptyNode node)
    {
        var field = node.Field;

        if (field.Type == FieldType.MultiText)
        {
            return node.IsNot
                ? "(EXISTS (SELECT 1 FROM image_tag it WHERE it.image_id = image.id))"
                : "(NOT EXISTS (SELECT 1 FROM image_tag it WHERE it.image_id = image.id))";
        }

        if (field.Type == FieldType.Rating)
        {
            return node.IsNot
                ? "(image.rating IS NOT NULL AND image.rating > 0)"
                : "(image.rating IS NULL OR image.rating = 0)";
        }

        string col = field.Name.Equals("taken", StringComparison.OrdinalIgnoreCase)
            ? "COALESCE(image.taken_override_utc, image.exif_date_utc)"
            : $"image.{field.SqlColumn}";

        if (field.Type is FieldType.Text or FieldType.Format or FieldType.Path)
        {
            return node.IsNot
                ? $"({col} IS NOT NULL AND {col} != '')"
                : $"({col} IS NULL OR {col} = '')";
        }

        // Numbers and dates
        return node.IsNot
            ? $"({col} IS NOT NULL)"
            : $"({col} IS NULL)";
    }

    private string CompileLike(BoundLikeNode node)
    {
        var pName = NextParam(node.Pattern);

        if (node.Field.Type == FieldType.MultiText)
        {
            return node.IsNot
                ? $"(NOT EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND iq_like(t.name, @{pName}) = 1))"
                : $"(EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND iq_like(t.name, @{pName}) = 1))";
        }

        string col = $"image.{node.Field.SqlColumn}";
        return node.IsNot
            ? $"({col} IS NULL OR iq_like({col}, @{pName}) = 0)"
            : $"({col} IS NOT NULL AND iq_like({col}, @{pName}) = 1)";
    }

    private string CompileContains(BoundContainsNode node)
    {
        string pattern = "*" + EscapeLiteralForWildcardPattern(node.Value) + "*";
        var pName = NextParam(pattern);

        if (node.Field.Type == FieldType.MultiText)
        {
            return node.IsNot
                ? $"(NOT EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND iq_like(t.name, @{pName}) = 1))"
                : $"(EXISTS (SELECT 1 FROM image_tag it JOIN tag t ON it.tag_id = t.id WHERE it.image_id = image.id AND iq_like(t.name, @{pName}) = 1))";
        }

        string col = $"image.{node.Field.SqlColumn}";
        return node.IsNot
            ? $"({col} IS NULL OR iq_like({col}, @{pName}) = 0)"
            : $"({col} IS NOT NULL AND iq_like({col}, @{pName}) = 1)";
    }

    private string CompileUnder(BoundUnderNode node)
    {
        var pExact = NextParam(node.DirectoryKey);
        var pPrefix = NextParam(node.DirectoryKey + "/%");

        return node.IsNot
            ? $"(image.directory_key IS NULL OR (image.directory_key != @{pExact} AND image.directory_key NOT LIKE @{pPrefix}))"
            : $"(image.directory_key = @{pExact} OR image.directory_key LIKE @{pPrefix})";
    }

    private string CompileBareTerm(BoundBareTermNode node)
    {
        string pat = node.IsWildcard
            ? node.Pattern
            : "*" + EscapeLiteralForWildcardPattern(node.RawTerm) + "*";

        var pName = NextParam(pat);

        return $"(" +
               $"iq_like(image.file_name, @{pName}) = 1 OR " +
               $"iq_like(image.title, @{pName}) = 1 OR " +
               $"iq_like(image.description, @{pName}) = 1" +
               $")";
    }

    private string NextParam(object? value)
    {
        string name = $"q{_paramCounter++}";
        _parameters[name] = value;
        return name;
    }

    public static string EscapeLiteralForWildcardPattern(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        foreach (char c in text)
        {
            if (c is '\\' or '*' or '?')
            {
                sb.Append('\\');
            }
            sb.Append(c);
        }
        return sb.ToString();
    }
}

