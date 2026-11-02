using System.Text;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Scanning;

namespace IconChest.Core.Images;

public interface IImageSearchService
{
    ImageSearchResult Search(ImageSearchRequest request);
    IReadOnlyList<long> GetIds(ImageSearchRequest request, int maxIds = 200_000);
}

public sealed class ImageSearchService : IImageSearchService
{
    private static readonly HashSet<string> ValidSortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        "filename", "title", "effectivedate", "created", "modified", "taken", "added",
        "size", "width", "height", "type", "rating", "favourite", "folder"
    };

    private static readonly HashSet<string> ValidDateFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "effective", "created", "modified", "taken", "added"
    };

    private readonly IDatabaseConnectionFactory _connectionFactory;

    public ImageSearchService(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public ImageSearchResult Search(ImageSearchRequest request)
    {
        var limit = Math.Clamp(request.Limit, 1, 500);
        var offset = Math.Max(0, request.Offset);

        using var conn = _connectionFactory.CreateAndOpenConnection();
        var (whereSql, parameters) = BuildWhereClause(request, conn);
        var orderBySql = BuildOrderByClause(request.Sort, request.Dir);

        // 1. Get total count
        string countSql = $"SELECT COUNT(*) FROM image WHERE {whereSql};";
        int total = conn.ExecuteScalar<int>(countSql, parameters);

        if (total == 0 || offset >= total)
        {
            return new ImageSearchResult
            {
                Total = total,
                Offset = offset,
                Limit = limit,
                Items = Array.Empty<ImageSummary>()
            };
        }

        // 2. Fetch page of images
        parameters.Add("limit", limit);
        parameters.Add("offset", offset);

        string itemsSql = $@"
            SELECT id,
                   file_name AS FileName,
                   title AS Title,
                   width AS Width,
                   height AS Height,
                   format AS Format,
                   size_bytes AS SizeBytes,
                   rating AS Rating,
                   is_favourite AS IsFavourite,
                   is_hidden AS IsHidden,
                   is_missing AS IsMissing,
                   is_duplicate AS IsDuplicate,
                   effective_date_utc AS EffectiveDateUtc,
                   COALESCE(directory_path, '') AS Directory
            FROM image
            WHERE {whereSql}
            ORDER BY {orderBySql}
            LIMIT @limit OFFSET @offset;";

        var items = conn.Query<ImageSummary>(itemsSql, parameters).ToList();

        // 3. Batch fetch tags for the page
        if (items.Count > 0)
        {
            var itemIds = items.Select(x => x.Id).ToList();
            var tagMap = new Dictionary<long, List<long>>();
            foreach (var id in itemIds)
            {
                tagMap[id] = new List<long>();
            }

            const string tagsSql = @"
                SELECT image_id AS ImageId, tag_id AS TagId
                FROM image_tag
                WHERE image_id IN @itemIds
                ORDER BY image_id, tag_id;";

            var tagLinks = conn.Query<(long ImageId, long TagId)>(tagsSql, new { itemIds });
            foreach (var (imgId, tagId) in tagLinks)
            {
                if (tagMap.TryGetValue(imgId, out var list))
                {
                    list.Add(tagId);
                }
            }

            foreach (var item in items)
            {
                if (tagMap.TryGetValue(item.Id, out var list))
                {
                    item.TagIds = list;
                }
            }
        }

        return new ImageSearchResult
        {
            Total = total,
            Offset = offset,
            Limit = limit,
            Items = items
        };
    }

    public IReadOnlyList<long> GetIds(ImageSearchRequest request, int maxIds = 200_000)
    {
        var cappedLimit = Math.Clamp(maxIds, 1, 200_000);
        var offset = Math.Max(0, request.Offset);

        using var conn = _connectionFactory.CreateAndOpenConnection();
        var (whereSql, parameters) = BuildWhereClause(request, conn);
        var orderBySql = BuildOrderByClause(request.Sort, request.Dir);

        parameters.Add("limit", cappedLimit);
        parameters.Add("offset", offset);

        string sql = $@"
            SELECT id
            FROM image
            WHERE {whereSql}
            ORDER BY {orderBySql}
            LIMIT @limit OFFSET @offset;";

        return conn.Query<long>(sql, parameters).ToList();
    }

    private static (string WhereSql, DynamicParameters Parameters) BuildWhereClause(
        ImageSearchRequest request,
        System.Data.Common.DbConnection conn)
    {
        var conditions = new List<string>();
        var p = new DynamicParameters();

        // 1. Folder ID
        if (request.FolderId.HasValue)
        {
            conditions.Add("folder_id = @folderId");
            p.Add("folderId", request.FolderId.Value);
        }

        // 2. Directory & IncludeSubfolders
        if (!string.IsNullOrWhiteSpace(request.Directory))
        {
            var dirKey = PathKeyProvider.Default.GetDirectoryKey(request.Directory);
            if (request.IncludeSubfolders)
            {
                conditions.Add("(directory_key = @dirKey OR directory_key LIKE @dirKeyPrefix)");
                p.Add("dirKey", dirKey);
                p.Add("dirKeyPrefix", dirKey + "/%");
            }
            else
            {
                conditions.Add("directory_key = @dirKey");
                p.Add("dirKey", dirKey);
            }
        }

        // 3. Date field determination
        string dateCol = ResolveDateFieldColumn(request.DateField);

        // Year & Month filter on dateCol
        if (request.Year.HasValue && request.Month.HasValue)
        {
            int y = request.Year.Value;
            int m = Math.Clamp(request.Month.Value, 1, 12);
            string start = $"{y:D4}-{m:D2}-01";
            int nextY = m == 12 ? y + 1 : y;
            int nextM = m == 12 ? 1 : m + 1;
            string end = $"{nextY:D4}-{nextM:D2}-01";
            conditions.Add($"{dateCol} >= @ymStart AND {dateCol} < @ymEnd");
            p.Add("ymStart", start);
            p.Add("ymEnd", end);
        }
        else if (request.Year.HasValue)
        {
            int y = request.Year.Value;
            string start = $"{y:D4}-01-01";
            string end = $"{y + 1:D4}-01-01";
            conditions.Add($"{dateCol} >= @yStart AND {dateCol} < @yEnd");
            p.Add("yStart", start);
            p.Add("yEnd", end);
        }
        else if (request.Month.HasValue)
        {
            conditions.Add($"SUBSTR({dateCol}, 6, 2) = @month");
            p.Add("month", request.Month.Value.ToString("D2"));
        }

        // Date ranges
        if (!string.IsNullOrWhiteSpace(request.DateFrom))
        {
            conditions.Add($"{dateCol} >= @dateFrom");
            p.Add("dateFrom", request.DateFrom.Trim());
        }

        if (!string.IsNullOrWhiteSpace(request.DateTo))
        {
            var dateToTrim = request.DateTo.Trim();
            // If date is yyyy-mm-dd, append end of day to cover entire day
            var dateToVal = dateToTrim.Length == 10 ? dateToTrim + "T23:59:59.999Z" : dateToTrim;
            conditions.Add($"{dateCol} <= @dateTo");
            p.Add("dateTo", dateToVal);
        }

        // 4. Tags
        if (request.Tags != null && request.Tags.Count > 0)
        {
            var rawTags = request.Tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
            if (rawTags.Count > 0)
            {
                var tagIds = new List<long>();
                var tagNames = new List<string>();

                foreach (var t in rawTags)
                {
                    if (long.TryParse(t, out var parsedId))
                    {
                        tagIds.Add(parsedId);
                    }
                    else
                    {
                        tagNames.Add(t);
                    }
                }

                // Resolve all tag IDs from DB
                var resolvedIds = new HashSet<long>(tagIds);
                if (tagNames.Count > 0)
                {
                    var foundIds = conn.Query<long>(
                        "SELECT id FROM tag WHERE name IN @tagNames COLLATE NOCASE;",
                        new { tagNames });
                    foreach (var fid in foundIds)
                    {
                        resolvedIds.Add(fid);
                    }
                }

                if (resolvedIds.Count == 0)
                {
                    // No matching tags exist -> impossible match
                    conditions.Add("1 = 0");
                }
                else if (request.TagMode == TagFilterMode.All)
                {
                    conditions.Add($@"
                        id IN (
                            SELECT image_id
                            FROM image_tag
                            WHERE tag_id IN @resolvedTagIds
                            GROUP BY image_id
                            HAVING COUNT(DISTINCT tag_id) = {resolvedIds.Count}
                        )");
                    p.Add("resolvedTagIds", resolvedIds.ToList());
                }
                else
                {
                    // Any
                    conditions.Add(@"
                        id IN (
                            SELECT image_id
                            FROM image_tag
                            WHERE tag_id IN @resolvedTagIds
                        )");
                    p.Add("resolvedTagIds", resolvedIds.ToList());
                }
            }
        }

        // 5. Types (format/extension)
        if (request.Types != null && request.Types.Count > 0)
        {
            var cleanTypes = request.Types
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().TrimStart('.').ToLowerInvariant())
                .ToList();

            if (cleanTypes.Count > 0)
            {
                conditions.Add("LOWER(format) IN @cleanTypes");
                p.Add("cleanTypes", cleanTypes);
            }
        }

        // 6. Dimensions
        if (request.MinWidth.HasValue)
        {
            conditions.Add("width >= @minWidth");
            p.Add("minWidth", request.MinWidth.Value);
        }
        if (request.MaxWidth.HasValue)
        {
            conditions.Add("width <= @maxWidth");
            p.Add("maxWidth", request.MaxWidth.Value);
        }
        if (request.MinHeight.HasValue)
        {
            conditions.Add("height >= @minHeight");
            p.Add("minHeight", request.MinHeight.Value);
        }
        if (request.MaxHeight.HasValue)
        {
            conditions.Add("height <= @maxHeight");
            p.Add("maxHeight", request.MaxHeight.Value);
        }

        // 7. Size bytes
        if (request.MinSize.HasValue)
        {
            conditions.Add("size_bytes >= @minSize");
            p.Add("minSize", request.MinSize.Value);
        }
        if (request.MaxSize.HasValue)
        {
            conditions.Add("size_bytes <= @maxSize");
            p.Add("maxSize", request.MaxSize.Value);
        }

        // 8. Rating & Favourite
        if (request.RatingMin.HasValue)
        {
            conditions.Add("rating >= @ratingMin");
            p.Add("ratingMin", request.RatingMin.Value);
        }
        if (request.Favourite.HasValue)
        {
            conditions.Add("is_favourite = @favourite");
            p.Add("favourite", request.Favourite.Value ? 1 : 0);
        }

        // 9. Text search
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            conditions.Add("(file_name LIKE @textPattern OR title LIKE @textPattern OR description LIKE @textPattern)");
            p.Add("textPattern", $"%{request.Text.Trim()}%");
        }

        // 10. Inclusion flags (Hidden, Missing, Duplicate)
        switch (request.Hidden)
        {
            case InclusionState.Exclude:
                conditions.Add("is_hidden = 0");
                break;
            case InclusionState.Only:
                conditions.Add("is_hidden = 1");
                break;
            case InclusionState.Include:
                break;
        }

        switch (request.Missing)
        {
            case InclusionState.Exclude:
                conditions.Add("is_missing = 0");
                break;
            case InclusionState.Only:
                conditions.Add("is_missing = 1");
                break;
            case InclusionState.Include:
                break;
        }

        switch (request.Duplicate)
        {
            case InclusionState.Exclude:
                conditions.Add("is_duplicate = 0");
                break;
            case InclusionState.Only:
                conditions.Add("is_duplicate = 1");
                break;
            case InclusionState.Include:
                break;
        }

        // 11. Custom WHERE clause (Phase 5 hook)
        if (!string.IsNullOrWhiteSpace(request.CustomWhereClause))
        {
            conditions.Add($"({request.CustomWhereClause})");
            if (request.CustomWhereParameters != null)
            {
                foreach (var kv in request.CustomWhereParameters)
                {
                    p.Add(kv.Key, kv.Value);
                }
            }
        }

        string whereSql = conditions.Count > 0 ? string.Join(" AND ", conditions) : "1=1";
        return (whereSql, p);
    }

    private static string ResolveDateFieldColumn(string dateField)
    {
        return dateField.ToLowerInvariant() switch
        {
            "created" => "created_utc",
            "modified" => "modified_utc",
            "taken" => "COALESCE(taken_override_utc, exif_date_utc)",
            "added" => "added_utc",
            _ => "effective_date_utc"
        };
    }

    private static string BuildOrderByClause(string sort, string dir)
    {
        string direction = dir.Equals("desc", StringComparison.OrdinalIgnoreCase) ? "DESC" : "ASC";

        string sortColumn = sort.ToLowerInvariant() switch
        {
            "filename" => "file_name",
            "title" => "title",
            "effectivedate" => "effective_date_utc",
            "created" => "created_utc",
            "modified" => "modified_utc",
            "taken" => "COALESCE(taken_override_utc, exif_date_utc)",
            "added" => "added_utc",
            "size" => "size_bytes",
            "width" => "width",
            "height" => "height",
            "type" => "format",
            "rating" => "rating",
            "favourite" => "is_favourite",
            "folder" => "folder_id",
            _ => "file_name"
        };

        return $"{sortColumn} {direction}, id {direction}";
    }
}
