namespace IconChest.Core.Thumbnails;

public enum ThumbnailResultStatus
{
    Ready,
    Pending,
    Failed,
    NotFound
}

public sealed record ThumbnailResult(
    ThumbnailResultStatus Status,
    string? FilePath = null,
    string ContentType = "image/webp",
    string? ETag = null,
    string? Error = null)
{
    public static ThumbnailResult Ready(string filePath, string etag) =>
        new(ThumbnailResultStatus.Ready, filePath, "image/webp", etag);

    public static ThumbnailResult Pending() =>
        new(ThumbnailResultStatus.Pending);

    public static ThumbnailResult Failed(string? error = null) =>
        new(ThumbnailResultStatus.Failed, Error: error ?? "Thumbnail generation failed.");

    public static ThumbnailResult NotFound() =>
        new(ThumbnailResultStatus.NotFound);
}

public sealed record CacheVerificationReport(
    int TotalChecked,
    int ValidDone,
    int MissingFileRestoredToPending,
    int StaleFilesDeleted);

