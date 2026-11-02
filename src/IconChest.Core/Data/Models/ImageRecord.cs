namespace IconChest.Core.Data.Models;

public sealed class ImageRecord
{
    public long Id { get; set; }
    public long FolderId { get; set; }
    public string Path { get; set; } = string.Empty;
    public string PathKey { get; set; } = string.Empty;
    public string DirectoryPath { get; set; } = string.Empty;
    public string DirectoryKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public int? Width { get; set; }
    public int? Height { get; set; }
    public long SizeBytes { get; set; }
    public string Hash { get; set; } = string.Empty;
    public string HashAlgo { get; set; } = "xxh128";
    public string? CreatedUtc { get; set; }
    public string? ModifiedUtc { get; set; }
    public string? ExifDateUtc { get; set; }
    public string? TakenOverrideUtc { get; set; }
    public string AddedUtc { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Rating { get; set; }
    public bool IsFavourite { get; set; }
    public bool IsHidden { get; set; }
    public bool IsMissing { get; set; }
    public string? MissingSinceUtc { get; set; }
    public bool IsDuplicate { get; set; }
    public bool? HasTransparency { get; set; }
    public string Status { get; set; } = "ok";
    public string? ExifJson { get; set; }
    public string? EffectiveDateUtc { get; set; }
}

public sealed record ImageMoveUpdate(
    long Id,
    long FolderId,
    string Path,
    string PathKey,
    string DirectoryPath,
    string DirectoryKey,
    string FileName,
    string Extension,
    string? ModifiedUtc);

