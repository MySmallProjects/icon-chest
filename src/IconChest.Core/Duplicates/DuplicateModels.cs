namespace IconChest.Core.Duplicates;

public class DuplicateGroupItem
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Rating { get; set; }
    public bool IsFavourite { get; set; }
    public string? EffectiveDateUtc { get; set; }
    public string Directory { get; set; } = string.Empty;
    public string Folder => Directory;
    public List<long> TagIds { get; set; } = new();
}

public sealed class DuplicateGroup
{
    public string Hash { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public List<DuplicateGroupItem> Items { get; set; } = new();
    public int Count => Items.Count;
    public long ReclaimableBytes => Math.Max(0, (Items.Count - 1) * SizeBytes);
    public List<DuplicateGroupItem> Images => Items;
}

public sealed class DuplicateSearchResult
{
    public int TotalGroups { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public IReadOnlyList<DuplicateGroup> Groups { get; set; } = Array.Empty<DuplicateGroup>();
}

public sealed class DuplicateSummaryResult
{
    public int GroupCount { get; set; }
    public long ImageCount { get; set; }
    public long ReclaimableBytes { get; set; }
    public IReadOnlyList<int> GroupSizes { get; set; } = Array.Empty<int>();
}

public interface IDuplicateService
{
    DuplicateSummaryResult GetSummary(string? sort = null, string? dir = null);
    DuplicateSearchResult GetDuplicates(int offset = 0, int limit = 50, string? sort = null, string? dir = null);
    IReadOnlyList<long> GetDuplicateImageIds(string? sort = null, string? dir = null, int limit = 200_000);
}
