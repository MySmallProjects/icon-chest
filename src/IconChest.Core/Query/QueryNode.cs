namespace IconChest.Core.Query;

public abstract record QueryNode(int Position, int Length);

public sealed record OrNode(QueryNode Left, QueryNode Right, int Position, int Length)
    : QueryNode(Position, Length);

public sealed record AndNode(QueryNode Left, QueryNode Right, int Position, int Length)
    : QueryNode(Position, Length);

public sealed record NotNode(QueryNode Operand, int Position, int Length)
    : QueryNode(Position, Length);

public sealed record ComparisonNode(
    QueryToken Field,
    QueryToken Op,
    QueryToken Value,
    int Position,
    int Length) : QueryNode(Position, Length);

public sealed record InNode(
    QueryToken Field,
    bool IsNot,
    IReadOnlyList<QueryToken> Values,
    int Position,
    int Length) : QueryNode(Position, Length);

public sealed record IsEmptyNode(
    QueryToken Field,
    bool IsNot,
    int Position,
    int Length) : QueryNode(Position, Length);

public sealed record LikeNode(
    QueryToken Field,
    bool IsNot,
    QueryToken Pattern,
    int Position,
    int Length) : QueryNode(Position, Length);

public sealed record ContainsNode(
    QueryToken Field,
    bool IsNot,
    QueryToken Value,
    int Position,
    int Length) : QueryNode(Position, Length);

public sealed record UnderNode(
    QueryToken Field,
    bool IsNot,
    QueryToken Path,
    int Position,
    int Length) : QueryNode(Position, Length);

public sealed record BareTermNode(
    QueryToken Term,
    int Position,
    int Length) : QueryNode(Position, Length);

public sealed record BooleanShorthandNode(
    QueryToken Field,
    int Position,
    int Length) : QueryNode(Position, Length);

