namespace IconChest.Core.Missing;

public sealed class MissingImageItem
{
    public long Id { get; set; }
    public long FolderId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string? MissingSinceUtc { get; set; }
    public long SizeBytes { get; set; }
}

public sealed class MissingImagesResult
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public IReadOnlyList<MissingImageItem> Items { get; set; } = Array.Empty<MissingImageItem>();
}

public interface IMissingService
{
    MissingImagesResult GetMissing(int offset = 0, int limit = 50);
    int RemoveMissing(long? folderId = null);
    IReadOnlyList<long> GetFolderIdsWithMissing();
}

