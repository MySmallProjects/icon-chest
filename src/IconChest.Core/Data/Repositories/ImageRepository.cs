using Dapper;
using IconChest.Core.Data.Models;
using Microsoft.Data.Sqlite;

namespace IconChest.Core.Data.Repositories;

public sealed class ImageRepository : IImageRepository
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    public ImageRepository(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public ImageRecord? GetById(long id)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.QuerySingleOrDefault<ImageRecord>(@"
            SELECT id, folder_id AS FolderId, path, path_key AS PathKey,
                   directory_path AS DirectoryPath, directory_key AS DirectoryKey,
                   file_name AS FileName, extension, format, width, height, size_bytes AS SizeBytes,
                   hash, hash_algo AS HashAlgo, created_utc AS CreatedUtc, modified_utc AS ModifiedUtc,
                   exif_date_utc AS ExifDateUtc, taken_override_utc AS TakenOverrideUtc, added_utc AS AddedUtc,
                   title, description, rating, is_favourite AS IsFavourite, is_hidden AS IsHidden,
                   is_missing AS IsMissing, missing_since_utc AS MissingSinceUtc,
                   is_duplicate AS IsDuplicate, has_transparency AS HasTransparency,
                   status, exif_json AS ExifJson, effective_date_utc AS EffectiveDateUtc
            FROM image
            WHERE id = @id;", new { id });
    }

    public ImageRecord? GetByPathKey(string pathKey)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.QuerySingleOrDefault<ImageRecord>(@"
            SELECT id, folder_id AS FolderId, path, path_key AS PathKey,
                   directory_path AS DirectoryPath, directory_key AS DirectoryKey,
                   file_name AS FileName, extension, format, width, height, size_bytes AS SizeBytes,
                   hash, hash_algo AS HashAlgo, created_utc AS CreatedUtc, modified_utc AS ModifiedUtc,
                   exif_date_utc AS ExifDateUtc, taken_override_utc AS TakenOverrideUtc, added_utc AS AddedUtc,
                   title, description, rating, is_favourite AS IsFavourite, is_hidden AS IsHidden,
                   is_missing AS IsMissing, missing_since_utc AS MissingSinceUtc,
                   is_duplicate AS IsDuplicate, has_transparency AS HasTransparency,
                   status, exif_json AS ExifJson, effective_date_utc AS EffectiveDateUtc
            FROM image
            WHERE path_key = @pathKey;", new { pathKey });
    }

    public Dictionary<string, ImageRecord> GetRecordsByFolder(long folderId)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var records = conn.Query<ImageRecord>(@"
            SELECT id, folder_id AS FolderId, path, path_key AS PathKey,
                   directory_path AS DirectoryPath, directory_key AS DirectoryKey,
                   file_name AS FileName, extension, format, width, height, size_bytes AS SizeBytes,
                   hash, hash_algo AS HashAlgo, created_utc AS CreatedUtc, modified_utc AS ModifiedUtc,
                   exif_date_utc AS ExifDateUtc, taken_override_utc AS TakenOverrideUtc, added_utc AS AddedUtc,
                   title, description, rating, is_favourite AS IsFavourite, is_hidden AS IsHidden,
                   is_missing AS IsMissing, missing_since_utc AS MissingSinceUtc,
                   is_duplicate AS IsDuplicate, has_transparency AS HasTransparency,
                   status, exif_json AS ExifJson, effective_date_utc AS EffectiveDateUtc
            FROM image
            WHERE folder_id = @folderId;", new { folderId });

        return records.ToDictionary(r => r.PathKey, StringComparer.Ordinal);
    }

    public void InsertBatch(IEnumerable<ImageRecord> records)
    {
        var list = records.ToList();
        if (list.Count == 0) return;

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            const string sql = @"
                INSERT INTO image (
                    folder_id, path, path_key, directory_path, directory_key,
                    file_name, extension, format, width, height, size_bytes,
                    hash, hash_algo, created_utc, modified_utc, exif_date_utc,
                    taken_override_utc, added_utc, title, description, rating,
                    is_favourite, is_hidden, is_missing, missing_since_utc,
                    is_duplicate, has_transparency, status, exif_json
                ) VALUES (
                    @FolderId, @Path, @PathKey, @DirectoryPath, @DirectoryKey,
                    @FileName, @Extension, @Format, @Width, @Height, @SizeBytes,
                    @Hash, @HashAlgo, @CreatedUtc, @ModifiedUtc, @ExifDateUtc,
                    @TakenOverrideUtc, @AddedUtc, @Title, @Description, @Rating,
                    @IsFavourite, @IsHidden, @IsMissing, @MissingSinceUtc,
                    @IsDuplicate, @HasTransparency, @Status, @ExifJson
                );";

            conn.Execute(sql, list, transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void UpdateTechnicalBatch(IEnumerable<ImageRecord> records)
    {
        var list = records.ToList();
        if (list.Count == 0) return;

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            // Updates technical fields while preserving title, description, rating, favourite, hidden, taken override, tags
            const string sql = @"
                UPDATE image SET
                    format = @Format,
                    width = @Width,
                    height = @Height,
                    size_bytes = @SizeBytes,
                    hash = @Hash,
                    hash_algo = @HashAlgo,
                    created_utc = @CreatedUtc,
                    modified_utc = @ModifiedUtc,
                    exif_date_utc = @ExifDateUtc,
                    has_transparency = @HasTransparency,
                    status = @Status,
                    exif_json = @ExifJson,
                    is_missing = 0,
                    missing_since_utc = NULL
                WHERE id = @Id;";

            conn.Execute(sql, list, transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void UpdateMoveBatch(IEnumerable<ImageMoveUpdate> moves)
    {
        var list = moves.ToList();
        if (list.Count == 0) return;

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            const string sql = @"
                UPDATE image SET
                    folder_id = @FolderId,
                    path = @Path,
                    path_key = @PathKey,
                    directory_path = @DirectoryPath,
                    directory_key = @DirectoryKey,
                    file_name = @FileName,
                    extension = @Extension,
                    modified_utc = @ModifiedUtc,
                    is_missing = 0,
                    missing_since_utc = NULL
                WHERE id = @Id;";

            conn.Execute(sql, list, transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void MarkMissingBatch(IEnumerable<long> imageIds, string missingSinceUtc)
    {
        var list = imageIds.ToList();
        if (list.Count == 0) return;

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            const string sql = @"
                UPDATE image SET
                    is_missing = 1,
                    missing_since_utc = COALESCE(missing_since_utc, @missingSinceUtc)
                WHERE id = @id;";

            conn.Execute(sql, list.Select(id => new { id, missingSinceUtc }), transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void RestoreMissingBatch(IEnumerable<long> imageIds)
    {
        var list = imageIds.ToList();
        if (list.Count == 0) return;

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            const string sql = @"
                UPDATE image SET
                    is_missing = 0,
                    missing_since_utc = NULL
                WHERE id = @id;";

            conn.Execute(sql, list.Select(id => new { id }), transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public int DeleteMissingForFolder(long folderId)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.Execute("DELETE FROM image WHERE folder_id = @folderId AND is_missing = 1;", new { folderId });
    }

    public IReadOnlyList<ImageRecord> GetLibraryMoveCandidates()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        // Candidate pool: all records across whole library flagged missing OR whose file no longer exists
        return conn.Query<ImageRecord>(@"
            SELECT id, folder_id AS FolderId, path, path_key AS PathKey,
                   directory_path AS DirectoryPath, directory_key AS DirectoryKey,
                   file_name AS FileName, extension, format, width, height, size_bytes AS SizeBytes,
                   hash, hash_algo AS HashAlgo, created_utc AS CreatedUtc, modified_utc AS ModifiedUtc,
                   exif_date_utc AS ExifDateUtc, taken_override_utc AS TakenOverrideUtc, added_utc AS AddedUtc,
                   title, description, rating, is_favourite AS IsFavourite, is_hidden AS IsHidden,
                   is_missing AS IsMissing, missing_since_utc AS MissingSinceUtc,
                   is_duplicate AS IsDuplicate, has_transparency AS HasTransparency,
                   status, exif_json AS ExifJson, effective_date_utc AS EffectiveDateUtc
            FROM image
            ORDER BY id ASC;").ToList();
    }

    public void RecomputeDuplicates(IEnumerable<string> hashes)
    {
        var hashList = hashes.Where(h => !string.IsNullOrWhiteSpace(h)).Distinct().ToList();
        if (hashList.Count == 0) return;

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            // Clear duplicates for affected hashes
            conn.Execute("UPDATE image SET is_duplicate = 0 WHERE hash IN @hashList;", new { hashList }, transaction: tx);

            // Set duplicates for affected hashes
            conn.Execute(@"
                UPDATE image
                SET is_duplicate = 1
                WHERE hash IN @hashList
                  AND is_hidden = 0
                  AND is_missing = 0
                  AND size_bytes > 0
                  AND (hash, size_bytes) IN (
                      SELECT hash, size_bytes
                      FROM image
                      WHERE hash IN @hashList
                        AND is_hidden = 0
                        AND is_missing = 0
                        AND size_bytes > 0
                      GROUP BY hash, size_bytes
                      HAVING COUNT(*) > 1
                  );", new { hashList }, transaction: tx);

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void RecomputeAllDuplicates()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            conn.Execute("UPDATE image SET is_duplicate = 0;", transaction: tx);
            conn.Execute(@"
                UPDATE image
                SET is_duplicate = 1
                WHERE is_hidden = 0
                  AND is_missing = 0
                  AND size_bytes > 0
                  AND (hash, size_bytes) IN (
                      SELECT hash, size_bytes
                      FROM image
                      WHERE is_hidden = 0
                        AND is_missing = 0
                        AND size_bytes > 0
                      GROUP BY hash, size_bytes
                      HAVING COUNT(*) > 1
                  );", transaction: tx);
            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public void SetHidden(long id, bool isHidden)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        conn.Execute("UPDATE image SET is_hidden = @isHidden WHERE id = @id;", new { id, isHidden = isHidden ? 1 : 0 });
    }

    public IReadOnlyList<ImageRecord> List(
        long? folderId = null,
        bool includeHidden = false,
        bool includeMissing = false,
        bool duplicatesOnly = false,
        int limit = 50,
        int offset = 0)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var sql = @"
            SELECT id, folder_id AS FolderId, path, path_key AS PathKey,
                   directory_path AS DirectoryPath, directory_key AS DirectoryKey,
                   file_name AS FileName, extension, format, width, height, size_bytes AS SizeBytes,
                   hash, hash_algo AS HashAlgo, created_utc AS CreatedUtc, modified_utc AS ModifiedUtc,
                   exif_date_utc AS ExifDateUtc, taken_override_utc AS TakenOverrideUtc, added_utc AS AddedUtc,
                   title, description, rating, is_favourite AS IsFavourite, is_hidden AS IsHidden,
                   is_missing AS IsMissing, missing_since_utc AS MissingSinceUtc,
                   is_duplicate AS IsDuplicate, has_transparency AS HasTransparency,
                   status, exif_json AS ExifJson, effective_date_utc AS EffectiveDateUtc
            FROM image
            WHERE 1=1";

        if (folderId.HasValue) sql += " AND folder_id = @folderId";
        if (!includeHidden) sql += " AND is_hidden = 0";
        if (!includeMissing) sql += " AND is_missing = 0";
        if (duplicatesOnly) sql += " AND is_duplicate = 1";

        sql += " ORDER BY id ASC LIMIT @limit OFFSET @offset;";

        return conn.Query<ImageRecord>(sql, new { folderId, limit, offset }).ToList();
    }

    public int Count(
        long? folderId = null,
        bool includeHidden = false,
        bool includeMissing = false,
        bool duplicatesOnly = false)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var sql = "SELECT COUNT(*) FROM image WHERE 1=1";
        if (folderId.HasValue) sql += " AND folder_id = @folderId";
        if (!includeHidden) sql += " AND is_hidden = 0";
        if (!includeMissing) sql += " AND is_missing = 0";
        if (duplicatesOnly) sql += " AND is_duplicate = 1";

        return conn.ExecuteScalar<int>(sql, new { folderId });
    }

    public DatabaseStats GetStats()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var folders = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM library_folder;");
        var images = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image;");
        var missing = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image WHERE is_missing = 1;");
        var hidden = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image WHERE is_hidden = 1;");
        var duplicates = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image WHERE is_duplicate = 1;");
        var tags = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM tag;");

        return new DatabaseStats(folders, images, missing, hidden, duplicates, tags);
    }
}

