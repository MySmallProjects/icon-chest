namespace IconChest.Core.Query;

public abstract record BoundNode;

public sealed record BoundOrNode(BoundNode Left, BoundNode Right) : BoundNode;

public sealed record BoundAndNode(BoundNode Left, BoundNode Right) : BoundNode;

public sealed record BoundNotNode(BoundNode Operand) : BoundNode;

public sealed record BoundComparisonNode(
    FieldDefinition Field,
    string Operator,
    object Value) : BoundNode;

public sealed record BoundDateComparisonNode(
    FieldDefinition Field,
    string Operator,
    string BoundStartUtc,
    string BoundEndUtc) : BoundNode;

public sealed record BoundInNode(
    FieldDefinition Field,
    bool IsNot,
    IReadOnlyList<object> Values) : BoundNode;

public sealed record BoundIsEmptyNode(
    FieldDefinition Field,
    bool IsNot) : BoundNode;

public sealed record BoundLikeNode(
    FieldDefinition Field,
    bool IsNot,
    string Pattern) : BoundNode;

public sealed record BoundContainsNode(
    FieldDefinition Field,
    bool IsNot,
    string Value) : BoundNode;

public sealed record BoundUnderNode(
    FieldDefinition Field,
    bool IsNot,
    string DirectoryKey) : BoundNode;

public sealed record BoundBareTermNode(
    string RawTerm,
    string Pattern,
    bool IsWildcard) : BoundNode;

