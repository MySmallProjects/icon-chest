namespace IconChest.Core.Query;

public enum QueryTokenType
{
    LParen,
    RParen,
    Comma,
    CmpOp,
    And,
    Or,
    Not,
    In,
    Is,
    Empty,
    Like,
    Contains,
    Under,
    Word,
    String,
    EndOfInput
}

public sealed record QueryToken(
    QueryTokenType Type,
    string Text,
    int Position,
    int Length,
    string? RawString = null)
{
    public override string ToString() => $"{Type}({Text})@{Position}+{Length}";
}

