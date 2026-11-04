using Dapper;
using IconChest.Core.Data;

namespace IconChest.Core.Duplicates;

public sealed class DuplicateService : IDuplicateService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public DuplicateService(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    private static (string OrderByClause, string NormalizedSort, string NormalizedDir) BuildSortClause(string? sort, string? dir)
    {
        var s = (sort ?? "reclaimable").Trim().ToLowerInvariant();
        var d = (dir ?? "").Trim().ToLowerInvariant();

        if (d != "asc" && d != "desc")
        {
            d = s == "name" ? "asc" : "desc";
        }

        string clause = s switch
        {
            "copies" => $"COUNT(*) {d.ToUpperInvariant()}, ((COUNT(*) - 1) * size_bytes) DESC, hash ASC",
            "size" => $"size_bytes {d.ToUpperInvariant()}, COUNT(*) DESC, hash ASC",
            "name" => $"MIN(file_name) {d.ToUpperInvariant()}, hash ASC",
            _ => $"((COUNT(*) - 1) * size_bytes) {d.ToUpperInvariant()}, size_bytes DESC, hash ASC"
        };

        return (clause, s, d);
    }

    public DuplicateSummaryResult GetSummary(string? sort = null, string? dir = null)
    {
        var (orderByClause, _, _) = BuildSortClause(sort, dir);

        using var conn = _connectionFactory.CreateAndOpenConnection();

        string sql = $@"
            SELECT hash,
                   size_bytes,
                   COUNT(*) AS copies,
                   ((COUNT(*) - 1) * size_bytes) AS reclaimable
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0 AND size_bytes > 0
            GROUP BY hash, size_bytes
            HAVING COUNT(*) > 1
            ORDER BY {orderByClause}
            LIMIT 200000;";

        var rows = conn.Query<(string Hash, long SizeBytes, int Copies, long Reclaimable)>(sql).ToList();

        long imageCount = 0;
        long reclaimableBytes = 0;
        var groupSizes = new List<int>(rows.Count);

        foreach (var r in rows)
        {
            imageCount += r.Copies;
            reclaimableBytes += r.Reclaimable;
            groupSizes.Add(r.Copies);
        }

        return new DuplicateSummaryResult
        {
            GroupCount = rows.Count,
            ImageCount = imageCount,
            ReclaimableBytes = reclaimableBytes,
            GroupSizes = groupSizes
        };
    }

    public DuplicateSearchResult GetDuplicates(int offset = 0, int limit = 50, string? sort = null, string? dir = null)
    {
        var clampedLimit = Math.Clamp(limit, 1, 500);
        var clampedOffset = Math.Max(0, offset);

        using var conn = _connectionFactory.CreateAndOpenConnection();

        // 1. Total duplicate groups count
        const string countSql = @"
            SELECT COUNT(*) FROM (
                SELECT hash, size_bytes
                FROM image
                WHERE is_hidden = 0 AND is_missing = 0 AND size_bytes > 0
                GROUP BY hash, size_bytes
                HAVING COUNT(*) > 1
            );";

        int totalGroups = conn.ExecuteScalar<int>(countSql);
        if (totalGroups == 0 || clampedOffset >= totalGroups)
        {
            return new DuplicateSearchResult
            {
                TotalGroups = totalGroups,
                Offset = clampedOffset,
                Limit = clampedLimit,
                Groups = Array.Empty<DuplicateGroup>()
            };
        }

        // 2. Fetch page of groups in requested sort order
        var (orderByClause, _, _) = BuildSortClause(sort, dir);

        string pagedGroupsSql = $@"
            SELECT hash AS Hash, size_bytes AS SizeBytes
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0 AND size_bytes > 0
            GROUP BY hash, size_bytes
            HAVING COUNT(*) > 1
            ORDER BY {orderByClause}
            LIMIT @limit OFFSET @offset;";

        var groups = conn.Query<(string Hash, long SizeBytes)>(
            pagedGroupsSql,
            new { limit = clampedLimit, offset = clampedOffset }).ToList();

        if (groups.Count == 0)
        {
            return new DuplicateSearchResult
            {
                TotalGroups = totalGroups,
                Offset = clampedOffset,
                Limit = clampedLimit,
                Groups = Array.Empty<DuplicateGroup>()
            };
        }

        // 3. Fetch all images belonging to these groups
        var hashes = groups.Select(g => g.Hash).Distinct().ToList();
        const string itemsSql = @"
            SELECT id,
                   path AS Path,
                   file_name AS FileName,
                   title AS Title,
                   width AS Width,
                   height AS Height,
                   format AS Format,
                   size_bytes AS SizeBytes,
                   rating AS Rating,
                   is_favourite AS IsFavourite,
                   effective_date_utc AS EffectiveDateUtc,
                   COALESCE(directory_path, '') AS Directory,
                   hash AS Hash
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0 AND hash IN @hashes
            ORDER BY path ASC;";

        var rawItems = conn.Query<DuplicateItemWithHash>(itemsSql, new { hashes }).ToList();

        // 4. Batch fetch tag IDs for these items
        if (rawItems.Count > 0)
        {
            var itemIds = rawItems.Select(x => x.Id).ToList();
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

            foreach (var item in rawItems)
            {
                if (tagMap.TryGetValue(item.Id, out var list))
                {
                    item.TagIds = list;
                }
            }
        }

        // 5. Group items into DuplicateGroup objects matching the paged groups
        var itemsLookup = rawItems.ToLookup(x => (x.Hash, x.SizeBytes));
        var resultGroups = new List<DuplicateGroup>();

        foreach (var (hash, sizeBytes) in groups)
        {
            var grp = new DuplicateGroup
            {
                Hash = hash,
                SizeBytes = sizeBytes,
                Items = itemsLookup[(hash, sizeBytes)].Select(x => (DuplicateGroupItem)x).ToList()
            };
            resultGroups.Add(grp);
        }

        return new DuplicateSearchResult
        {
            TotalGroups = totalGroups,
            Offset = clampedOffset,
            Limit = clampedLimit,
            Groups = resultGroups
        };
    }

    public IReadOnlyList<long> GetDuplicateImageIds(string? sort = null, string? dir = null, int limit = 200_000)
    {
        var clampedLimit = Math.Clamp(limit, 1, 200_000);
        var (orderByClause, _, _) = BuildSortClause(sort, dir);

        using var conn = _connectionFactory.CreateAndOpenConnection();

        // Retrieve the ordered groups
        string orderedGroupsSql = $@"
            SELECT hash, size_bytes
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0 AND size_bytes > 0
            GROUP BY hash, size_bytes
            HAVING COUNT(*) > 1
            ORDER BY {orderByClause}
            LIMIT 200000;";

        var groups = conn.Query<(string Hash, long SizeBytes)>(orderedGroupsSql).ToList();
        if (groups.Count == 0)
        {
            return Array.Empty<long>();
        }

        var hashes = groups.Select(g => g.Hash).Distinct().ToList();

        // Fetch image ids and hashes ordered by path
        const string imagesSql = @"
            SELECT id, hash, size_bytes
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0 AND hash IN @hashes
            ORDER BY path ASC;";

        var images = conn.Query<(long Id, string Hash, long SizeBytes)>(imagesSql, new { hashes }).ToList();
        var imagesLookup = images.ToLookup(x => (x.Hash, x.SizeBytes), x => x.Id);

        var resultIds = new List<long>(Math.Min(images.Count, clampedLimit));
        foreach (var grp in groups)
        {
            foreach (var imgId in imagesLookup[grp])
            {
                resultIds.Add(imgId);
                if (resultIds.Count >= clampedLimit)
                {
                    return resultIds;
                }
            }
        }

        return resultIds;
    }

    private sealed class DuplicateItemWithHash : DuplicateGroupItem
    {
        public string Hash { get; set; } = string.Empty;
    }
}
