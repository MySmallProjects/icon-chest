namespace IconChest.Core.Query;

public sealed record FieldHelpItem(
    string Name,
    IReadOnlyList<string> Aliases,
    string Type,
    IReadOnlyList<string> AllowedOperators,
    string Description,
    IReadOnlyList<string> Examples);

public sealed record ErrorHelpItem(
    string Code,
    string Description,
    string Example);

public sealed record ExampleHelpItem(
    string Text,
    string Description,
    string Category);

public sealed record GrammarHelp(
    string Summary,
    IReadOnlyList<string> Rules);

public sealed record LimitsHelp(
    int MaxQueryLength,
    int MaxNestingDepth,
    int MaxTerms,
    int MaxInListValues,
    int MaxPatternLength);

public sealed class QueryHelpModel
{
    public IReadOnlyList<FieldHelpItem> Fields { get; init; } = Array.Empty<FieldHelpItem>();
    public IReadOnlyList<ErrorHelpItem> ErrorCodes { get; init; } = Array.Empty<ErrorHelpItem>();
    public IReadOnlyList<ExampleHelpItem> Examples { get; init; } = Array.Empty<ExampleHelpItem>();
    public GrammarHelp Grammar { get; init; } = new(string.Empty, Array.Empty<string>());
    public LimitsHelp Limits { get; init; } = new(0, 0, 0, 0, 0);
}

