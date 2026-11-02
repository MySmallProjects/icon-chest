namespace IconChest.Core.Thumbnails;

public sealed record ThumbnailRecord
{
    public required string Hash { get; init; }
    public required int Size { get; init; }
    public required string State { get; init; }
    public int Priority { get; init; }
    public int Attempts { get; init; }
    public string? NextAttemptUtc { get; init; }
    public string? Error { get; init; }
    public long? Bytes { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public required string CreatedUtc { get; init; }
    public required string UpdatedUtc { get; init; }
    public string? LastAccessedUtc { get; init; }
    public string? ClaimedUtc { get; init; }
}

public sealed record ThumbnailQueueStatus(
    int TotalJobs,
    int Pending,
    int Running,
    int Done,
    int Failed,
    int Evicted,
    int Orphaned,
    Dictionary<int, int> CountsBySize,
    double ItemsPerSecond = 0.0,
    TimeSpan? EstimatedTimeRemaining = null);

