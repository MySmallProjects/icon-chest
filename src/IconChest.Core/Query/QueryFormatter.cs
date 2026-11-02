using System.Text;

namespace IconChest.Core.Query;

public static class QueryFormatter
{
    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "not", "in", "is", "empty", "like", "contains", "under", "true", "false"
    };

    public static string Format(QueryNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return FormatNode(node, precedence: 0);
    }

    private static string FormatNode(QueryNode node, int precedence)
    {
        // Precedence:
        // 1: or
        // 2: and
        // 3: not
        // 4: primary / predicate
        return node switch
        {
            OrNode orNode => WrapIf(precedence > 1, $"{FormatNode(orNode.Left, 1)} or {FormatNode(orNode.Right, 1)}"),
            AndNode andNode => WrapIf(precedence > 2, $"{FormatNode(andNode.Left, 2)} and {FormatNode(andNode.Right, 2)}"),
            NotNode notNode => WrapIf(precedence > 3, $"not {FormatNode(notNode.Operand, 3)}"),
            ComparisonNode comp => $"{comp.Field.Text.ToLowerInvariant()} {NormalizeOp(comp.Op.Text)} {FormatValue(comp.Value)}",
            InNode inNode => FormatIn(inNode),
            IsEmptyNode emptyNode => $"{emptyNode.Field.Text.ToLowerInvariant()} {(emptyNode.IsNot ? "is not empty" : "is empty")}",
            LikeNode likeNode => $"{likeNode.Field.Text.ToLowerInvariant()} {(likeNode.IsNot ? "not like" : "like")} {FormatString(likeNode.Pattern.Text)}",
            ContainsNode containsNode => $"{containsNode.Field.Text.ToLowerInvariant()} {(containsNode.IsNot ? "not contains" : "contains")} {FormatString(containsNode.Value.Text)}",
            UnderNode underNode => $"{underNode.Field.Text.ToLowerInvariant()} {(underNode.IsNot ? "not under" : "under")} {FormatString(underNode.Path.Text)}",
            BareTermNode bareNode => FormatBareTerm(bareNode.Term),
            BooleanShorthandNode boolNode => boolNode.Field.Text.ToLowerInvariant(),
            _ => throw new InvalidOperationException($"Unknown node type {node.GetType().Name}")
        };
    }

    private static string FormatIn(InNode inNode)
    {
        var op = inNode.IsNot ? "not in" : "in";
        var values = string.Join(", ", inNode.Values.Select(FormatValue));
        return $"{inNode.Field.Text.ToLowerInvariant()} {op} ({values})";
    }

    private static string FormatValue(QueryToken token)
    {
        if (token.Type == QueryTokenType.String)
        {
            return FormatString(token.Text);
        }

        var text = token.Text;

        // Formats, booleans, keywords, or words in lowercase
        if (Keywords.Contains(text) || text.Contains(' ') || text.Contains('\t') || text.Contains('"') || text.Contains('\''))
        {
            return FormatString(text);
        }

        // Canonical lowercase for known formats, booleans, or format keywords
        if (FieldRegistry.KnownFormats.Contains(text) ||
            text.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return text.ToLowerInvariant();
        }

        return text;
    }

    private static string FormatBareTerm(QueryToken token)
    {
        var text = token.Text;
        if (token.Type == QueryTokenType.String || Keywords.Contains(text) || text.Contains(' ') || text.Contains('\t'))
        {
            return FormatString(text);
        }
        return text;
    }

    private static string FormatString(string text)
    {
        var sb = new StringBuilder(text.Length + 4);
        sb.Append('"');
        foreach (char c in text)
        {
            if (c == '\\')
            {
                sb.Append(@"\\");
            }
            else if (c == '"')
            {
                sb.Append(@"\""");
            }
            else
            {
                sb.Append(c);
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    private static string NormalizeOp(string op) => op == "=" ? "==" : op;

    private static string WrapIf(bool condition, string text) => condition ? $"({text})" : text;
}
