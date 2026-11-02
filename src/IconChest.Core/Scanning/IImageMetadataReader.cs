namespace IconChest.Core.Scanning;

public sealed record ImageMetadata(
    string Format,
    int? Width,
    int? Height,
    long SizeBytes,
    DateTime? CreatedUtc,
    DateTime? ModifiedUtc,
    string? ExifDateUtc,
    string? ExifJson,
    bool? HasTransparency,
    string Status);

public interface IImageMetadataReader
{
    TimeZoneInfo TimeZone { get; set; }
    Task<ImageMetadata> ReadMetadataAsync(string filePath, CancellationToken cancellationToken = default);
    string DeriveDefaultTitle(string fileName);
    string SniffFormat(ReadOnlySpan<byte> headerBytes);
}

