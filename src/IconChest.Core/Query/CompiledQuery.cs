namespace IconChest.Core.Query;

public sealed record CompiledQuery(
    string WhereSql,
    IReadOnlyDictionary<string, object?> Parameters,
    bool MentionsHidden,
    bool MentionsMissing);

