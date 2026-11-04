using System.Text.Json.Serialization;

namespace IconChest.Core.Tags;

public sealed record TagExportItem(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fgColor")] string FgColor,
    [property: JsonPropertyName("bgColor")] string BgColor);

public sealed record TagExportDocument(
    [property: JsonPropertyName("format")] string Format,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("exportedUtc")] string ExportedUtc,
    [property: JsonPropertyName("tags")] IReadOnlyList<TagExportItem> Tags);

public sealed record TagImportResult(
    [property: JsonPropertyName("addedCount")] int AddedCount,
    [property: JsonPropertyName("skippedCount")] int SkippedCount,
    [property: JsonPropertyName("duplicateInFileCount")] int DuplicateInFileCount,
    [property: JsonPropertyName("invalidCount")] int InvalidCount,
    [property: JsonPropertyName("errors")] IReadOnlyList<string> Errors)
{
    [JsonPropertyName("tagsImported")]
    public int TagsImported => AddedCount;

    [JsonPropertyName("tagsSkipped")]
    public int TagsSkipped => SkippedCount;

    [JsonPropertyName("tagsRead")]
    public int TagsRead => AddedCount + SkippedCount + DuplicateInFileCount + InvalidCount;
}

