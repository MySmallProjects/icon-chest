namespace IconChest.Core.Images;

public sealed class ImageSummary
{
    public long Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public int Rating { get; set; }
    public bool IsFavourite { get; set; }
    public bool IsHidden { get; set; }
    public bool IsMissing { get; set; }
    public bool IsDuplicate { get; set; }
    public string? EffectiveDateUtc { get; set; }
    public string Directory { get; set; } = string.Empty;
    public List<long> TagIds { get; set; } = new();
}

