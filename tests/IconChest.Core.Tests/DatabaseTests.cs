using Dapper;
using IconChest.Core.Data;
using IconChest.Core.Data.Exceptions;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IconChest.Core.Tests;

public class DatabaseTests
{
    private static (IconChestDatabase Db, string DbPath) CreateTempFileDb()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"iconchest-test-{Guid.NewGuid():N}.db");
        var db = new IconChestDatabase(tempFile);
        return (db, tempFile);
    }

    [Fact]
    public void FreshMigration_SucceedsAndSetsSchemaVersion()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            var initialVersion = db.GetSchemaVersion();
            Assert.Equal(0, initialVersion);

            var migratedVersion = db.Migrate();
            Assert.Equal(4, migratedVersion);
            Assert.Equal(4, db.GetSchemaVersion());

            // Check tables exist
            using var conn = db.OpenConnection();
            var tables = conn.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table';").ToList();
            Assert.Contains("schema_version", tables);
            Assert.Contains("library_folder", tables);
            Assert.Contains("image", tables);
            Assert.Contains("tag", tables);
            Assert.Contains("image_tag", tables);
            Assert.Contains("thumbnail", tables);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Migration_IsIdempotent()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            var v1 = db.Migrate();
            var v2 = db.Migrate();
            Assert.Equal(4, v1);
            Assert.Equal(4, v2);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void NewerSchema_ThrowsSchemaVersionException_LeavesFileUnmodified()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            db.Migrate();

            // Artificially inject a newer schema version
            using (var conn = db.OpenConnection())
            {
                conn.Execute("INSERT INTO schema_version (version, applied_utc, description) VALUES (99, '2026-11-01', 'Future');");
            }

            var ex = Assert.Throws<SchemaVersionException>(() => db.Migrate());
            Assert.Equal(99, ex.DatabaseVersion);
            Assert.Equal(4, ex.SupportedVersion);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FileDatabase_EnablesWalMode()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            using var conn = db.OpenConnection();
            var journalMode = conn.ExecuteScalar<string>("PRAGMA journal_mode;");
            Assert.Equal("wal", journalMode, ignoreCase: true);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ForeignKeys_AreEnforced()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            db.Migrate();
            using var conn = db.OpenConnection();

            // Attempt to insert an image with non-existent folder_id (9999)
            Assert.Throws<SqliteException>(() =>
            {
                conn.Execute(@"
                    INSERT INTO image (
                        folder_id, path, path_key, file_name, extension, format,
                        width, height, size_bytes, hash, hash_algo, created_utc,
                        modified_utc, added_utc, title
                    ) VALUES (
                        9999, '/icons/icon.png', '/icons/icon.png', 'icon.png', '.png', 'png',
                        64, 64, 1024, 'abc123', 'xxhash128', '2026-11-01',
                        '2026-11-01', '2026-11-01', 'Icon'
                    );");
            });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Constraints_UniqueAndCaseInsensitiveTag()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            db.Migrate();
            using var conn = db.OpenConnection();

            conn.Execute("INSERT INTO tag (name) VALUES ('Nature');");

            // Duplicate tag in different case must violate COLLATE NOCASE UNIQUE constraint
            Assert.Throws<SqliteException>(() =>
            {
                conn.Execute("INSERT INTO tag (name) VALUES ('nature');");
            });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void Constraints_UniquePathKey()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            db.Migrate();
            using var conn = db.OpenConnection();

            var folderId = conn.QuerySingle<long>("INSERT INTO library_folder (path) VALUES ('/icons') RETURNING id;");

            conn.Execute(@"
                INSERT INTO image (
                    folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, created_utc,
                    modified_utc, added_utc, title
                ) VALUES (
                    @folderId, '/icons/1.png', 'key_1', '1.png', '.png', 'png',
                    64, 64, 1024, 'hash1', 'xxhash128', '2026-11-01',
                    '2026-11-01', '2026-11-01', '1'
                );", new { folderId });

            // Duplicate path_key
            Assert.Throws<SqliteException>(() =>
            {
                conn.Execute(@"
                    INSERT INTO image (
                        folder_id, path, path_key, file_name, extension, format,
                        width, height, size_bytes, hash, hash_algo, created_utc,
                        modified_utc, added_utc, title
                    ) VALUES (
                        @folderId, '/icons/2.png', 'key_1', '2.png', '.png', 'png',
                        64, 64, 1024, 'hash2', 'xxhash128', '2026-11-01',
                        '2026-11-01', '2026-11-01', '2'
                    );", new { folderId });
            });
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void CascadingDeletes_WorkProperly()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            db.Migrate();
            using var conn = db.OpenConnection();

            var folderId = conn.QuerySingle<long>("INSERT INTO library_folder (path) VALUES ('/lib') RETURNING id;");

            var imageId = conn.QuerySingle<long>(@"
                INSERT INTO image (
                    folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, created_utc,
                    modified_utc, added_utc, title
                ) VALUES (
                    @folderId, '/lib/a.png', 'key_a', 'a.png', '.png', 'png',
                    32, 32, 500, 'hash_a', 'xxhash128', '2026-11-01',
                    '2026-11-01', '2026-11-01', 'A'
                ) RETURNING id;", new { folderId });

            var tagId = conn.QuerySingle<long>("INSERT INTO tag (name) VALUES ('TestTag') RETURNING id;");
            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@imageId, @tagId);", new { imageId, tagId });

            // Verify row counts before delete
            Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image;"));
            Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image_tag;"));

            // Delete library folder
            conn.Execute("DELETE FROM library_folder WHERE id = @folderId;", new { folderId });

            // Cascading delete must have cleaned up image and image_tag
            Assert.Equal(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image;"));
            Assert.Equal(0, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image_tag;"));
            Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM tag;")); // Tag itself remains
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void EffectiveDate_FallsBackInCorrectOrder()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            db.Migrate();
            using var conn = db.OpenConnection();

            var folderId = conn.QuerySingle<long>("INSERT INTO library_folder (path) VALUES ('/fallback') RETURNING id;");

            // Case 1: taken_override_utc is provided
            var id1 = conn.QuerySingle<long>(@"
                INSERT INTO image (
                    folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, created_utc,
                    modified_utc, exif_date_utc, taken_override_utc, added_utc, title
                ) VALUES (
                    @folderId, '/fallback/1.png', 'k1', '1.png', '.png', 'png',
                    32, 32, 500, 'h1', 'xxhash128', '2020-01-01',
                    '2021-01-01', '2022-01-01', '2025-01-01', '2026-01-01', '1'
                ) RETURNING id;", new { folderId });

            var eff1 = conn.QuerySingle<string>("SELECT effective_date_utc FROM image WHERE id = @id1;", new { id1 });
            Assert.Equal("2025-01-01", eff1);

            // Case 2: taken_override_utc is null, exif_date_utc is provided
            var id2 = conn.QuerySingle<long>(@"
                INSERT INTO image (
                    folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, created_utc,
                    modified_utc, exif_date_utc, taken_override_utc, added_utc, title
                ) VALUES (
                    @folderId, '/fallback/2.png', 'k2', '2.png', '.png', 'png',
                    32, 32, 500, 'h2', 'xxhash128', '2020-01-01',
                    '2021-01-01', '2022-01-01', NULL, '2026-01-01', '2'
                ) RETURNING id;", new { folderId });

            var eff2 = conn.QuerySingle<string>("SELECT effective_date_utc FROM image WHERE id = @id2;", new { id2 });
            Assert.Equal("2022-01-01", eff2);

            // Case 3: taken_override and exif are null, created_utc is provided
            var id3 = conn.QuerySingle<long>(@"
                INSERT INTO image (
                    folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, created_utc,
                    modified_utc, exif_date_utc, taken_override_utc, added_utc, title
                ) VALUES (
                    @folderId, '/fallback/3.png', 'k3', '3.png', '.png', 'png',
                    32, 32, 500, 'h3', 'xxhash128', '2020-01-01',
                    '2021-01-01', NULL, NULL, '2026-01-01', '3'
                ) RETURNING id;", new { folderId });

            var eff3 = conn.QuerySingle<string>("SELECT effective_date_utc FROM image WHERE id = @id3;", new { id3 });
            Assert.Equal("2020-01-01", eff3);

            // Case 4: taken_override, exif, and created are null/empty, modified_utc is provided
            var id4 = conn.QuerySingle<long>(@"
                INSERT INTO image (
                    folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, created_utc,
                    modified_utc, exif_date_utc, taken_override_utc, added_utc, title
                ) VALUES (
                    @folderId, '/fallback/4.png', 'k4', '4.png', '.png', 'png',
                    32, 32, 500, 'h4', 'xxhash128', NULL,
                    '2021-01-01', NULL, NULL, '2026-01-01', '4'
                ) RETURNING id;", new { folderId });

            var eff4 = conn.QuerySingle<string>("SELECT effective_date_utc FROM image WHERE id = @id4;", new { id4 });
            Assert.Equal("2021-01-01", eff4);

            // Case 5: all four date fields are null
            var id5 = conn.QuerySingle<long>(@"
                INSERT INTO image (
                    folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, created_utc,
                    modified_utc, exif_date_utc, taken_override_utc, added_utc, title
                ) VALUES (
                    @folderId, '/fallback/5.png', 'k5', '5.png', '.png', 'png',
                    32, 32, 500, 'h5', 'xxhash128', NULL,
                    NULL, NULL, NULL, '2026-01-01', '5'
                ) RETURNING id;", new { folderId });

            var eff5 = conn.QuerySingleOrDefault<string>("SELECT effective_date_utc FROM image WHERE id = @id5;", new { id5 });
            Assert.Null(eff5);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void FailedMigration_RollsBackTransaction()
    {
        var (db, tempFile) = CreateTempFileDb();
        try
        {
            // Initial migration succeeds to version 4
            db.Migrate();
            Assert.Equal(4, db.GetSchemaVersion());

            // Execute an explicit failed migration block within transaction
            using (var conn = db.OpenConnection())
            {
                using var tx = conn.BeginTransaction();
                try
                {
                    conn.Execute("CREATE TABLE temp_rollback_test (id INTEGER PRIMARY KEY);", transaction: tx);
                    // Invalid SQL statement to trigger failure
                    conn.Execute("INSERT INTO non_existent_table VALUES (1);", transaction: tx);
                    conn.Execute("INSERT INTO schema_version (version, applied_utc, description) VALUES (5, '2026-11-01', 'Fail');", transaction: tx);
                    tx.Commit();
                }
                catch
                {
                    tx.Rollback();
                }

                // Verify temp_rollback_test was rolled back and schema_version is still 4
                var tableExists = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'temp_rollback_test';");
                Assert.Equal(0, tableExists);
                var maxVersion = conn.ExecuteScalar<int>("SELECT MAX(version) FROM schema_version;");
                Assert.Equal(4, maxVersion);
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
