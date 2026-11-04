using Dapper;
using IconChest.Core.Data;
using IconChest.Core.Events;
using IconChest.Core.Thumbnails;

namespace IconChest.Core.Missing;

public sealed class MissingService : IMissingService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IThumbnailProvider? _thumbnailProvider;
    private readonly ILibraryEventEmitter? _eventEmitter;

    public MissingService(
        IDatabaseConnectionFactory connectionFactory,
        IThumbnailProvider? thumbnailProvider = null,
        ILibraryEventEmitter? eventEmitter = null)
    {
        _connectionFactory = connectionFactory;
        _thumbnailProvider = thumbnailProvider;
        _eventEmitter = eventEmitter;
    }

    public MissingImagesResult GetMissing(int offset = 0, int limit = 50, string? sort = null, string? dir = null)
    {
        var clampedLimit = Math.Clamp(limit, 1, 500);
        var clampedOffset = Math.Max(0, offset);

        using var conn = _connectionFactory.CreateAndOpenConnection();

        const string countSql = "SELECT COUNT(*) FROM image WHERE is_missing = 1;";
        int total = conn.ExecuteScalar<int>(countSql);

        if (total == 0 || clampedOffset >= total)
        {
            return new MissingImagesResult
            {
                Total = total,
                Offset = clampedOffset,
                Limit = clampedLimit,
                Items = Array.Empty<MissingImageItem>()
            };
        }

        var s = (sort ?? "path").Trim().ToLowerInvariant();
        var d = (dir ?? "").Trim().ToLowerInvariant();
        if (d != "asc" && d != "desc")
        {
            d = s == "since" ? "desc" : "asc";
        }

        string sortColumn = s switch
        {
            "name" => "i.file_name",
            "since" => "i.missing_since_utc",
            "size" => "i.size_bytes",
            _ => "i.path"
        };

        string itemsSql = $@"
            SELECT i.id,
                   i.folder_id AS FolderId,
                   i.path AS Path,
                   i.file_name AS FileName,
                   i.missing_since_utc AS MissingSinceUtc,
                   i.size_bytes AS SizeBytes,
                   COALESCE(i.directory_path, '') AS Folder,
                   COALESCE(f.path, '') AS LibraryFolder
            FROM image i
            LEFT JOIN library_folder f ON i.folder_id = f.id
            WHERE i.is_missing = 1
            ORDER BY {sortColumn} {d.ToUpperInvariant()}, i.id ASC
            LIMIT @limit OFFSET @offset;";

        var items = conn.Query<MissingImageItem>(
            itemsSql,
            new { limit = clampedLimit, offset = clampedOffset }).ToList();

        return new MissingImagesResult
        {
            Total = total,
            Offset = clampedOffset,
            Limit = clampedLimit,
            Items = items
        };
    }

    public int RemoveMissing(IReadOnlyList<long>? ids = null, long? folderId = null)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        int deletedCount;
        try
        {
            if (ids != null && ids.Count > 0)
            {
                deletedCount = conn.Execute(
                    "DELETE FROM image WHERE is_missing = 1 AND id IN @ids;",
                    new { ids }, transaction: tx);
            }
            else if (folderId.HasValue)
            {
                deletedCount = conn.Execute(
                    "DELETE FROM image WHERE is_missing = 1 AND folder_id = @folderId;",
                    new { folderId = folderId.Value }, transaction: tx);
            }
            else
            {
                deletedCount = conn.Execute("DELETE FROM image WHERE is_missing = 1;", transaction: tx);
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }

        if (deletedCount > 0)
        {
            _thumbnailProvider?.CleanupOrphans();
            _eventEmitter?.EmitLibraryChanged();
        }

        return deletedCount;
    }

    public IReadOnlyList<long> GetFolderIdsWithMissing()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.Query<long>("SELECT DISTINCT folder_id FROM image WHERE is_missing = 1;").ToList();
    }
}
