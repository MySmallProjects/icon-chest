using Dapper;
using IconChest.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public static class FilterEndpoints
{
    private sealed class FormatRow
    {
        public string Format { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class FolderRow
    {
        public long Id { get; set; }
        public string Path { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    private sealed class DateRangeRow
    {
        public string? MinDate { get; set; }
        public string? MaxDate { get; set; }
    }

    public static void MapFilterEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/filters").WithTags("Filters");

        group.MapGet("/options", (
            HttpContext context,
            IDatabaseConnectionFactory connectionFactory) =>
        {
            using var conn = connectionFactory.CreateAndOpenConnection();

            // Formats present with counts (excluding hidden and missing)
            const string formatsSql = @"
                SELECT format AS Format, COUNT(*) AS Count
                FROM image
                WHERE is_hidden = 0 AND is_missing = 0
                GROUP BY format
                ORDER BY format COLLATE NOCASE;";

            var formats = conn.Query<FormatRow>(formatsSql)
                .Select(f => new { format = f.Format.ToLowerInvariant(), count = f.Count })
                .ToList();

            // Library folders with counts (excluding hidden and missing)
            const string foldersSql = @"
                SELECT lf.id AS Id, lf.path AS Path, COUNT(i.id) AS Count
                FROM library_folder lf
                LEFT JOIN image i ON lf.id = i.folder_id AND i.is_hidden = 0 AND i.is_missing = 0
                GROUP BY lf.id, lf.path
                ORDER BY lf.path COLLATE NOCASE;";

            var folders = conn.Query<FolderRow>(foldersSql)
                .Select(f =>
                {
                    var trimmed = f.Path.TrimEnd('/', '\\');
                    var name = System.IO.Path.GetFileName(trimmed);
                    if (string.IsNullOrEmpty(name)) name = f.Path;
                    return new { id = f.Id, name, path = f.Path, count = f.Count };
                })
                .ToList();

            // Min and Max effective dates (excluding hidden and missing)
            const string datesSql = @"
                SELECT
                    MIN(effective_date_utc) AS MinDate,
                    MAX(effective_date_utc) AS MaxDate
                FROM image
                WHERE is_hidden = 0 AND is_missing = 0;";

            var dates = conn.QuerySingleOrDefault<DateRangeRow>(datesSql);
            string? minDate = dates?.MinDate != null && dates.MinDate.Length >= 10 ? dates.MinDate.Substring(0, 10) : null;
            string? maxDate = dates?.MaxDate != null && dates.MaxDate.Length >= 10 ? dates.MaxDate.Substring(0, 10) : null;

            return Results.Ok(new
            {
                formats,
                folders,
                minDate,
                maxDate,
                minEffectiveDate = minDate,
                maxEffectiveDate = maxDate
            });
        })
        .WithName("GetFilterOptions")
        .WithSummary("Returns options for Simple filter dropdowns, including formats, folders, and min/max dates excluding hidden and missing images");
    }
}
