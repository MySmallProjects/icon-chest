using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace IconChest.Core.Scanning;

public sealed class ImageMetadataReader : IImageMetadataReader
{
    private const long MaxTransparencyCheckSizeBytes = 2 * 1024 * 1024; // 2 MB
    private static readonly Regex MultiWhitespace = new(@"\s+", RegexOptions.Compiled);

    private readonly ILogger<ImageMetadataReader> _logger;

    public TimeZoneInfo TimeZone { get; set; }

    public ImageMetadataReader(TimeZoneInfo? timeZone = null, ILogger<ImageMetadataReader>? logger = null)
    {
        TimeZone = timeZone ?? TimeZoneInfo.Local;
        _logger = logger ?? NullLogger<ImageMetadataReader>.Instance;
    }

    public string DeriveDefaultTitle(string fileName)
    {
        var withoutExt = Path.GetFileNameWithoutExtension(fileName);
        var withSpaces = withoutExt.Replace('_', ' ').Replace('-', ' ');
        return MultiWhitespace.Replace(withSpaces, " ").Trim();
    }

    public string SniffFormat(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 8 &&
            header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47 &&
            header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A)
        {
            return "png";
        }

        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
        {
            return "jpg";
        }

        if (header.Length >= 6 &&
            header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46 &&
            header[3] == 0x38 && (header[4] == 0x37 || header[4] == 0x39) && header[5] == 0x61)
        {
            return "gif";
        }

        if (header.Length >= 12 &&
            header[0] == 0x52 && header[1] == 0x49 && header[2] == 0x46 && header[3] == 0x46 && // RIFF
            header[8] == 0x57 && header[9] == 0x45 && header[10] == 0x42 && header[11] == 0x50) // WEBP
        {
            return "webp";
        }

        if (header.Length >= 2 && header[0] == 0x42 && header[1] == 0x4D)
        {
            return "bmp";
        }

        if (header.Length >= 4 && header[0] == 0x00 && header[1] == 0x00 && header[2] == 0x01 && header[3] == 0x00)
        {
            return "ico";
        }

        // SVG detection: look for '<svg' in the first 512 bytes
        var text = Encoding.UTF8.GetString(header);
        if (text.Contains("<svg", StringComparison.OrdinalIgnoreCase))
        {
            return "svg";
        }

        return "unknown";
    }

    public async Task<ImageMetadata> ReadMetadataAsync(string filePath, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
        {
            return new ImageMetadata("unknown", null, null, 0, null, null, null, null, null, "unreadable");
        }

        var size = fileInfo.Length;
        if (size == 0)
        {
            _logger.LogWarning("File {FilePath} is zero bytes.", filePath);
            return new ImageMetadata("unknown", null, null, 0, null, null, null, null, null, "unreadable");
        }

        var modifiedUtc = fileInfo.LastWriteTimeUtc;
        var createdUtc = fileInfo.CreationTimeUtc;
        // If created is missing, epoch, or after modified, fallback to modified
        if (createdUtc == DateTime.MinValue || createdUtc > modifiedUtc)
        {
            createdUtc = modifiedUtc;
        }

        byte[] headerBytes;
        try
        {
            await using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
            var headerLen = (int)Math.Min(4096, size);
            headerBytes = new byte[headerLen];
            var read = await fs.ReadAsync(headerBytes.AsMemory(0, headerLen), cancellationToken);
            if (read < headerLen)
            {
                Array.Resize(ref headerBytes, read);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read header of file {FilePath}", filePath);
            return new ImageMetadata("unknown", null, null, size, createdUtc, modifiedUtc, null, null, null, "unreadable");
        }

        var detectedFormat = SniffFormat(headerBytes);
        var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
        if (ext == "jpeg") ext = "jpg";

        if (detectedFormat != "unknown" && !string.Equals(detectedFormat, ext, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("File {FilePath} has extension '.{Ext}' but format is '{Format}'", filePath, ext, detectedFormat);
        }

        if (detectedFormat == "unknown")
        {
            _logger.LogWarning("File {FilePath} has unrecognized image format header", filePath);
            return new ImageMetadata("unknown", null, null, size, createdUtc, modifiedUtc, null, null, null, "unreadable");
        }

        if (detectedFormat == "svg")
        {
            return ReadSvgMetadata(filePath, size, createdUtc, modifiedUtc);
        }

        return ReadRasterMetadata(filePath, detectedFormat, size, createdUtc, modifiedUtc);
    }

    private ImageMetadata ReadSvgMetadata(string filePath, long size, DateTime createdUtc, DateTime modifiedUtc)
    {
        try
        {
            using var reader = new StreamReader(filePath);
            var doc = XDocument.Load(reader);
            var svgElement = doc.Root;
            if (svgElement == null || !svgElement.Name.LocalName.Equals("svg", StringComparison.OrdinalIgnoreCase))
            {
                return new ImageMetadata("svg", null, null, size, createdUtc, modifiedUtc, null, null, null, "unreadable");
            }

            int? width = null;
            int? height = null;

            var widthAttr = svgElement.Attribute("width")?.Value;
            var heightAttr = svgElement.Attribute("height")?.Value;

            if (TryParseSvgLength(widthAttr, out var parsedW) && TryParseSvgLength(heightAttr, out var parsedH))
            {
                width = (int)Math.Round(parsedW);
                height = (int)Math.Round(parsedH);
            }
            else
            {
                var viewBoxAttr = svgElement.Attribute("viewBox")?.Value;
                if (!string.IsNullOrWhiteSpace(viewBoxAttr))
                {
                    var parts = viewBoxAttr.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 4 &&
                        double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbW) &&
                        double.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var vbH))
                    {
                        width = (int)Math.Round(vbW);
                        height = (int)Math.Round(vbH);
                    }
                }
            }

            var status = (width.HasValue && height.HasValue) ? "ok" : "no_dimensions";
            return new ImageMetadata("svg", width, height, size, createdUtc, modifiedUtc, null, null, null, status);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse SVG file {FilePath}", filePath);
            return new ImageMetadata("svg", null, null, size, createdUtc, modifiedUtc, null, null, null, "unreadable");
        }
    }

    private static bool TryParseSvgLength(string? value, out double pixels)
    {
        pixels = 0;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var v = value.Trim().ToLowerInvariant();
        if (v.EndsWith("%") || v.EndsWith("em") || v.EndsWith("rem"))
        {
            return false;
        }

        double unitScale = 1.0;
        string numPart;

        if (v.EndsWith("px"))
        {
            numPart = v[..^2];
            unitScale = 1.0;
        }
        else if (v.EndsWith("pt"))
        {
            numPart = v[..^2];
            unitScale = 96.0 / 72.0;
        }
        else if (v.EndsWith("pc"))
        {
            numPart = v[..^2];
            unitScale = 16.0;
        }
        else if (v.EndsWith("mm"))
        {
            numPart = v[..^2];
            unitScale = 96.0 / 25.4;
        }
        else if (v.EndsWith("cm"))
        {
            numPart = v[..^2];
            unitScale = 96.0 / 2.54;
        }
        else if (v.EndsWith("in"))
        {
            numPart = v[..^2];
            unitScale = 96.0;
        }
        else
        {
            numPart = v;
        }

        if (double.TryParse(numPart.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var num) && num > 0)
        {
            pixels = num * unitScale;
            return true;
        }

        return false;
    }

    private ImageMetadata ReadRasterMetadata(string filePath, string format, long size, DateTime createdUtc, DateTime modifiedUtc)
    {
        int? width = null;
        int? height = null;
        int orientation = 1;

        // 1. SkiaSharp codec decode
        try
        {
            using var stream = File.OpenRead(filePath);
            using var codec = SKCodec.Create(stream);
            if (codec != null)
            {
                width = codec.Info.Width;
                height = codec.Info.Height;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SkiaSharp codec failed on {FilePath}", filePath);
        }

        // 2. MetadataExtractor EXIF
        string? exifJson = null;
        string? exifDateUtc = null;

        try
        {
            using var stream = File.OpenRead(filePath);
            var directories = ImageMetadataReaderExtensions.ReadMetadataSafe(stream);
            if (directories != null)
            {
                var dict = new Dictionary<string, object>();

                var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                var gps = directories.OfType<GpsDirectory>().FirstOrDefault();

                if (ifd0 != null)
                {
                    var make = ifd0.GetString(ExifDirectoryBase.TagMake);
                    var model = ifd0.GetString(ExifDirectoryBase.TagModel);
                    var software = ifd0.GetString(ExifDirectoryBase.TagSoftware);
                    if (ifd0.TryGetInt32(ExifDirectoryBase.TagOrientation, out var orientVal))
                    {
                        orientation = orientVal;
                        dict["orientation"] = orientation;
                    }

                    if (!string.IsNullOrWhiteSpace(make)) dict["make"] = make.Trim();
                    if (!string.IsNullOrWhiteSpace(model)) dict["model"] = model.Trim();
                    if (!string.IsNullOrWhiteSpace(software)) dict["software"] = software.Trim();
                }

                if (subIfd != null)
                {
                    var lens = subIfd.GetString(ExifDirectoryBase.TagLensModel);
                    var exposure = subIfd.GetString(ExifDirectoryBase.TagExposureTime);
                    var fNumber = subIfd.GetString(ExifDirectoryBase.TagFNumber);
                    var iso = subIfd.GetString(ExifDirectoryBase.TagIsoEquivalent);
                    var focalLength = subIfd.GetString(ExifDirectoryBase.TagFocalLength);

                    if (!string.IsNullOrWhiteSpace(lens)) dict["lens"] = lens.Trim();
                    if (!string.IsNullOrWhiteSpace(exposure)) dict["exposureTime"] = exposure.Trim();
                    if (!string.IsNullOrWhiteSpace(fNumber)) dict["fNumber"] = fNumber.Trim();
                    if (!string.IsNullOrWhiteSpace(iso)) dict["iso"] = iso.Trim();
                    if (!string.IsNullOrWhiteSpace(focalLength)) dict["focalLength"] = focalLength.Trim();

                    if (subIfd.TryGetDateTime(ExifDirectoryBase.TagDateTimeOriginal, out var dateTaken))
                    {
                        dict["dateTaken"] = dateTaken.ToString("yyyy-MM-dd HH:mm:ss");

                        // Convert to UTC using injected TimeZone
                        var offset = TimeZone.GetUtcOffset(dateTaken);
                        var utc = DateTime.SpecifyKind(dateTaken - offset, DateTimeKind.Utc);
                        exifDateUtc = utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
                    }
                }

                if (gps != null)
                {
                    var loc = gps.GetGeoLocation();
                    if (loc.HasValue && !loc.Value.IsZero)
                    {
                        dict["gpsLatitude"] = loc.Value.Latitude;
                        dict["gpsLongitude"] = loc.Value.Longitude;
                    }
                }

                if (dict.Count > 0)
                {
                    exifJson = JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = false });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No EXIF read for {FilePath}", filePath);
        }

        // Swap width and height if EXIF orientation is 5, 6, 7, 8
        if (width.HasValue && height.HasValue && orientation >= 5 && orientation <= 8)
        {
            (width, height) = (height, width);
        }

        // If codec couldn't get dimensions, file is unreadable
        if (!width.HasValue || !height.HasValue || width.Value <= 0 || height.Value <= 0)
        {
            _logger.LogWarning("File {FilePath} has no valid dimensions", filePath);
            return new ImageMetadata(format, null, null, size, createdUtc, modifiedUtc, exifDateUtc, exifJson, null, "unreadable");
        }

        // Transparency check for png, gif, webp, ico <= 2MB
        bool? hasTransparency = null;
        if (size <= MaxTransparencyCheckSizeBytes && format is "png" or "gif" or "webp" or "ico")
        {
            hasTransparency = DetectTransparency(filePath);
        }

        return new ImageMetadata(format, width, height, size, createdUtc, modifiedUtc, exifDateUtc, exifJson, hasTransparency, "ok");
    }

    private bool? DetectTransparency(string filePath)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            using var bitmap = SKBitmap.Decode(stream);
            if (bitmap == null) return null;

            var pixels = bitmap.Pixels;
            for (var i = 0; i < pixels.Length; i++)
            {
                if (pixels[i].Alpha < 255)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed transparency scan for {FilePath}", filePath);
            return null;
        }
    }
}

internal static class ImageMetadataReaderExtensions
{
    public static IReadOnlyList<MetadataExtractor.Directory>? ReadMetadataSafe(Stream stream)
    {
        try
        {
            return MetadataExtractor.ImageMetadataReader.ReadMetadata(stream);
        }
        catch
        {
            return null;
        }
    }
}
