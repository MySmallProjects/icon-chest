namespace IconChest.Core.Query;

public enum FieldType
{
    Format,
    Text,
    Path,
    MultiText,
    Integer,
    Size,
    Rating,
    Boolean,
    Date
}

public sealed class FieldDefinition
{
    public required string Name { get; init; }
    public IReadOnlyList<string> Aliases { get; init; } = Array.Empty<string>();
    public required FieldType Type { get; init; }
    public required HashSet<string> AllowedOperators { get; init; }
    public required string SqlColumn { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<string> Examples { get; init; }

    public bool SupportsOperator(string op)
    {
        var normalizedOp = op switch
        {
            "=" => "==",
            _ => op
        };
        return AllowedOperators.Contains(normalizedOp);
    }
}

