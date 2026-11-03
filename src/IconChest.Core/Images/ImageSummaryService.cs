using System.Data;
using System.Text.Json;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;

namespace IconChest.Core.Images;

public sealed class ImageSummaryResult
{
    public int Count { get; set; }
    public long TotalSizeBytes { get; set; }
    public Dictionary<string, int> Formats { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public int? MinWidth { get; set; }
    public int? MaxWidth { get; set; }
    public int? MinHeight { get; set; }
    public int? MaxHeight { get; set; }

    public string? Title { get; set; }
    public bool HasMultipleTitles { get; set; }
    public bool TitleShared => !HasMultipleTitles;

    public string? Description { get; set; }
    public bool HasMultipleDescriptions { get; set; }
    public bool DescriptionShared => !HasMultipleDescriptions;

    public int? Rating { get; set; }
    public bool HasMultipleRatings { get; set; }
    public bool RatingShared => !HasMultipleRatings;

    public string? TakenOverrideDate { get; set; }
    public bool HasMultipleTakenOverrideDates { get; set; }
    public bool TakenOverrideDateShared => !HasMultipleTakenOverrideDates;

    public int HiddenCount { get; set; }
    public int FavouriteCount { get; set; }

    public List<TagCountDto> Tags { get; set; } = new();
}

public sealed class TagCountDto
{
    public long TagId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? FgColor { get; set; }
    public string? BgColor { get; set; }
    public int Count { get; set; }
}

public interface IImageSummaryService
{
    ImageSummaryResult GetSummary(IReadOnlyList<long> ids);
}

public sealed class ImageSummaryService : IImageSummaryService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public ImageSummaryService(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public ImageSummaryResult GetSummary(IReadOnlyList<long> ids)
    {
        if (ids == null || ids.Count == 0)
        {
            throw new ArgumentException("IDs list cannot be empty.", nameof(ids));
        }

        if (ids.Count > 10_000)
        {
            throw new ArgumentException("Cannot summarize more than 10,000 images.", nameof(ids));
        }

        var distinctIds = ids.Distinct().ToList();
        var idsJson = JsonSerializer.Serialize(distinctIds);

        using var conn = _connectionFactory.CreateAndOpenConnection();

        // 1. Verify all IDs exist and fetch aggregate stats
        const string aggregateSql = @"
            SELECT
                COUNT(*) AS Count,
                COALESCE(SUM(size_bytes), 0) AS TotalSizeBytes,
                MIN(width) AS MinWidth,
                MAX(width) AS MaxWidth,
                MIN(height) AS MinHeight,
                MAX(height) AS MaxHeight,
                COUNT(DISTINCT title) AS DistinctTitleCount,
                MIN(title) AS SampleTitle,
                COUNT(DISTINCT description) AS DistinctDescriptionCount,
                MIN(description) AS SampleDescription,
                COUNT(CASE WHEN description IS NULL OR description = '' THEN 1 END) AS EmptyDescriptionCount,
                COUNT(DISTINCT rating) AS DistinctRatingCount,
                MIN(rating) AS SampleRating,
                COUNT(DISTINCT taken_override_utc) AS DistinctTakenCount,
                MIN(taken_override_utc) AS SampleTakenUtc,
                COUNT(CASE WHEN taken_override_utc IS NULL THEN 1 END) AS NullTakenCount,
                COUNT(CASE WHEN is_hidden = 1 THEN 1 END) AS HiddenCount,
                COUNT(CASE WHEN is_favourite = 1 THEN 1 END) AS FavouriteCount
            FROM image
            WHERE id IN (SELECT value FROM json_each(@idsJson));";

        var agg = conn.QuerySingle(aggregateSql, new { idsJson });
        int foundCount = (int)agg.Count;

        if (foundCount != distinctIds.Count)
        {
            // Find missing IDs
            const string missingSql = @"
                SELECT value
                FROM json_each(@idsJson)
                WHERE value NOT IN (SELECT id FROM image WHERE id IN (SELECT value FROM json_each(@idsJson)));";

            var missingIds = conn.Query<long>(missingSql, new { idsJson }).ToList();
            throw new KeyNotFoundException($"Image IDs not found: {string.Join(", ", missingIds)}");
        }

        var result = new ImageSummaryResult
        {
            Count = foundCount,
            TotalSizeBytes = (long)agg.TotalSizeBytes,
            MinWidth = agg.MinWidth != null ? (int?)agg.MinWidth : null,
            MaxWidth = agg.MaxWidth != null ? (int?)agg.MaxWidth : null,
            MinHeight = agg.MinHeight != null ? (int?)agg.MinHeight : null,
            MaxHeight = agg.MaxHeight != null ? (int?)agg.MaxHeight : null,
            HiddenCount = (int)agg.HiddenCount,
            FavouriteCount = (int)agg.FavouriteCount
        };

        // Title logic
        int titleCount = (int)agg.DistinctTitleCount;
        if (titleCount <= 1)
        {
            result.Title = (string?)agg.SampleTitle;
            result.HasMultipleTitles = false;
        }
        else
        {
            result.Title = null;
            result.HasMultipleTitles = true;
        }

        // Description logic
        int descCount = (int)agg.DistinctDescriptionCount;
        int emptyDescCount = (int)agg.EmptyDescriptionCount;
        if (descCount == 0 || (descCount == 1 && emptyDescCount == foundCount))
        {
            result.Description = string.Empty;
            result.HasMultipleDescriptions = false;
        }
        else if (descCount == 1 && emptyDescCount == 0)
        {
            result.Description = (string?)agg.SampleDescription;
            result.HasMultipleDescriptions = false;
        }
        else
        {
            result.Description = null;
            result.HasMultipleDescriptions = true;
        }

        // Rating logic
        int ratingCount = (int)agg.DistinctRatingCount;
        if (ratingCount <= 1)
        {
            result.Rating = agg.SampleRating != null ? (int?)agg.SampleRating : 0;
            result.HasMultipleRatings = false;
        }
        else
        {
            result.Rating = null;
            result.HasMultipleRatings = true;
        }

        // Taken date logic
        int takenCount = (int)agg.DistinctTakenCount;
        int nullTakenCount = (int)agg.NullTakenCount;
        if (takenCount == 0 && nullTakenCount == foundCount)
        {
            result.TakenOverrideDate = null;
            result.HasMultipleTakenOverrideDates = false;
        }
        else if (takenCount == 1 && nullTakenCount == 0)
        {
            string? sampleUtc = (string?)agg.SampleTakenUtc;
            result.TakenOverrideDate = DateHelper.UtcToLocalDateString(sampleUtc);
            result.HasMultipleTakenOverrideDates = false;
        }
        else
        {
            result.TakenOverrideDate = null;
            result.HasMultipleTakenOverrideDates = true;
        }

        // Formats
        const string formatsSql = @"
            SELECT format, COUNT(*) AS cnt
            FROM image
            WHERE id IN (SELECT value FROM json_each(@idsJson))
            GROUP BY format;";

        var formatRows = conn.Query<(string Format, int Count)>(formatsSql, new { idsJson });
        foreach (var f in formatRows)
        {
            result.Formats[f.Format] = f.Count;
        }

        // Tags
        const string tagsSql = @"
            SELECT it.tag_id AS TagId, t.name AS Name, t.fg_color AS FgColor, t.bg_color AS BgColor, COUNT(*) AS Count
            FROM image_tag it
            JOIN tag t ON it.tag_id = t.id
            WHERE it.image_id IN (SELECT value FROM json_each(@idsJson))
            GROUP BY it.tag_id, t.name, t.fg_color, t.bg_color;";

        result.Tags = conn.Query<TagCountDto>(tagsSql, new { idsJson }).ToList();

        return result;
    }
}

