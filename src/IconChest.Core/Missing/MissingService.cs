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

    public MissingImagesResult GetMissing(int offset = 0, int limit = 50)
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

        const string itemsSql = @"
            SELECT id,
                   folder_id AS FolderId,
                   path AS Path,
                   file_name AS FileName,
                   missing_since_utc AS MissingSinceUtc,
                   size_bytes AS SizeBytes
            FROM image
            WHERE is_missing = 1
            ORDER BY path ASC
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

    public int RemoveMissing(long? folderId = null)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        int deletedCount;
        try
        {
            if (folderId.HasValue)
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

