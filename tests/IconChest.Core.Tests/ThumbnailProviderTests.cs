using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Xunit;

namespace IconChest.Core.Tests;

public class ThumbnailProviderTests
{
    private static (IconChestDatabase Db,
                    ImageRepository ImageRepo,
                    LibraryFolderRepository FolderRepo,
                    ThumbnailCache Cache,
                    ThumbnailQueue Queue,
                    ThumbnailProvider Provider,
                    SettingsService Settings,
                    string TempDir) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"thumb-prov-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var configDir = Path.Combine(tempDir, "config");
        var dataDir = Path.Combine(tempDir, "data");
        var cacheDir = Path.Combine(tempDir, "cache");

        var paths = new AppPaths(configDirOverride: configDir, dataDirOverride: dataDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        settings.Update(s =>
        {
            s.ThumbnailCachePath = cacheDir;
            s.ThumbnailCacheMaxMb = 1; // 1 MB max for easy eviction testing
        });

        var db = new IconChestDatabase(settings);
        db.Migrate();

        var connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        var folderRepo = new LibraryFolderRepository(connFactory);
        var imageRepo = new ImageRepository(connFactory);
        var cache = new ThumbnailCache(settings);
        var queue = new ThumbnailQueue(connFactory);
        var provider = new ThumbnailProvider(connFactory, queue, cache, settings);

        return (db, imageRepo, folderRepo, cache, queue, provider, settings, tempDir);
    }

    [Theory]
    [InlineData(16, 64)]
    [InlineData(32, 64)]
    [InlineData(64, 64)]
    [InlineData(65, 128)]
    [InlineData(100, 128)]
    [InlineData(128, 128)]
    [InlineData(129, 256)]
    [InlineData(200, 256)]
    [InlineData(256, 256)]
    [InlineData(512, 256)]
    [InlineData(1024, 256)]
    public void PickFor_ReturnsSmallestSizeAtLeastDisplayPixels_CappedAt256(int displayPixels, int expectedSize)
    {
        var picked = ThumbnailSizes.PickFor(displayPixels);
        Assert.Equal(expectedSize, picked);
    }

    [Fact]
    public async Task GetAsync_ReturnsExpectedStatusStates()
    {
        var (_, imageRepo, folderRepo, cache, queue, provider, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/img.png",
                PathKey = "/test/img.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "img.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_states",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T12:00:00Z",
                ModifiedUtc = "2026-11-02T12:00:00Z",
                AddedUtc = "2026-11-02T12:00:00Z",
                Title = "img"
            };

            var id = imageRepo.InsertBatch([img])[0];

            // 1. NotFound for non-existent image ID
            var resNotFound = await provider.GetAsync(999999, 64);
            Assert.Equal(ThumbnailResultStatus.NotFound, resNotFound.Status);

            // 2. Pending when enqueued
            queue.EnqueueForImages([id]);
            var resPending = await provider.GetAsync(id, 64);
            Assert.Equal(ThumbnailResultStatus.Pending, resPending.Status);

            // 3. Ready when done and file exists on disk
            await cache.WriteAsync("hash_states", 64, new byte[] { 1, 2, 3 });
            queue.CompleteJob("hash_states", 64, 3, 64, 64);

            var resReady = await provider.GetAsync(id, 64);
            Assert.Equal(ThumbnailResultStatus.Ready, resReady.Status);
            Assert.Equal("image/webp", resReady.ContentType);
            Assert.NotNull(resReady.FilePath);
            Assert.True(File.Exists(resReady.FilePath));
            Assert.Equal("\"hash_states_64\"", resReady.ETag);

            // 4. Failed state
            queue.RecordAttemptFailure("hash_states", 128, "Simulated fatal error", isDeterministic: true);
            var resFailed = await provider.GetAsync(id, 128);
            Assert.Equal(ThumbnailResultStatus.Failed, resFailed.Status);
            Assert.Equal("Simulated fatal error", resFailed.Error);
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task SelfHealing_DoneThumbnailMissingFromDisk_RestoredToPending()
    {
        var (_, imageRepo, folderRepo, cache, queue, provider, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/selfheal.png",
                PathKey = "/test/selfheal.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "selfheal.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_heal",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T12:00:00Z",
                ModifiedUtc = "2026-11-02T12:00:00Z",
                AddedUtc = "2026-11-02T12:00:00Z",
                Title = "selfheal"
            };

            var id = imageRepo.InsertBatch([img])[0];
            queue.EnqueueForImages([id]);

            // Complete job
            await cache.WriteAsync("hash_heal", 64, new byte[] { 10, 20, 30 });
            queue.CompleteJob("hash_heal", 64, 3, 64, 64);

            // Verify file exists
            var diskPath = cache.GetThumbnailPath("hash_heal", 64);
            Assert.True(File.Exists(diskPath));

            // Delete disk file
            File.Delete(diskPath);
            Assert.False(File.Exists(diskPath));

            // Requesting thumbnail detects missing file and self-heals
            var res = await provider.GetAsync(id, 64);
            Assert.Equal(ThumbnailResultStatus.Pending, res.Status);

            var record = queue.GetRecord("hash_heal", 64);
            Assert.NotNull(record);
            Assert.Equal("pending", record.State);
            Assert.True(record.Priority >= 50); // Raised priority
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task AccessTrackingAndEviction_FlushesBatch_EvictsLeastRecentlyUsedDownTo90Percent()
    {
        var (db, imageRepo, folderRepo, cache, queue, provider, settings, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img1 = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/a.png",
                PathKey = "/test/a.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "a.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_lru_1",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T10:00:00Z",
                ModifiedUtc = "2026-11-02T10:00:00Z",
                AddedUtc = "2026-11-02T10:00:00Z",
                Title = "a"
            };

            var img2 = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/b.png",
                PathKey = "/test/b.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "b.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_lru_2",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T11:00:00Z",
                ModifiedUtc = "2026-11-02T11:00:00Z",
                AddedUtc = "2026-11-02T11:00:00Z",
                Title = "b"
            };

            var ids = imageRepo.InsertBatch([img1, img2]);
            queue.EnqueueForImages(ids);

            // Write 600 KB payload for each image size 64 -> total 1200 KB > 1024 KB max
            var payload = new byte[600 * 1024];
            await cache.WriteAsync("hash_lru_1", 64, payload);
            queue.CompleteJob("hash_lru_1", 64, payload.Length, 64, 64);

            await cache.WriteAsync("hash_lru_2", 64, payload);
            queue.CompleteJob("hash_lru_2", 64, payload.Length, 64, 64);

            // Access img2 so img1 is least recently used
            await provider.GetAsync(ids[1], 64);
            provider.FlushAccessLog();

            using (var conn = db.OpenConnection())
            {
                var access2 = conn.ExecuteScalar<string>("SELECT last_accessed_utc FROM thumbnail WHERE hash = 'hash_lru_2' AND size = 64;");
                Assert.NotNull(access2);
            }

            // Run Eviction: total is ~1.2 MB, max is 1.0 MB. Target is 0.90 MB (921 KB).
            // Evicting img1 (600 KB) leaves 600 KB <= 921 KB -> evicts 1 item.
            var evicted = provider.RunEviction();
            Assert.Equal(1, evicted);

            var rec1 = queue.GetRecord("hash_lru_1", 64);
            var rec2 = queue.GetRecord("hash_lru_2", 64);

            Assert.Equal("evicted", rec1?.State);
            Assert.False(cache.Exists("hash_lru_1", 64));

            Assert.Equal("done", rec2?.State);
            Assert.True(cache.Exists("hash_lru_2", 64));

            // Requesting evicted thumbnail regenerates it lazily
            var resEvictedReq = await provider.GetAsync(ids[0], 64);
            Assert.Equal(ThumbnailResultStatus.Pending, resEvictedReq.Status);
            var rec1Restored = queue.GetRecord("hash_lru_1", 64);
            Assert.Equal("pending", rec1Restored?.State);
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task CleanupOrphans_DeletesFilesAndRowsForDeletedImageRecords_KeepsShared()
    {
        var (_, imageRepo, folderRepo, cache, queue, provider, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var imgUnique = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/unique.png",
                PathKey = "/test/unique.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "unique.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_unique",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T12:00:00Z",
                ModifiedUtc = "2026-11-02T12:00:00Z",
                AddedUtc = "2026-11-02T12:00:00Z",
                Title = "unique"
            };

            var imgShared1 = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/shared1.png",
                PathKey = "/test/shared1.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "shared1.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_shared",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T12:00:00Z",
                ModifiedUtc = "2026-11-02T12:00:00Z",
                AddedUtc = "2026-11-02T12:00:00Z",
                Title = "shared1"
            };

            var imgShared2 = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/shared2.png",
                PathKey = "/test/shared2.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "shared2.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_shared",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T12:00:00Z",
                ModifiedUtc = "2026-11-02T12:00:00Z",
                AddedUtc = "2026-11-02T12:00:00Z",
                Title = "shared2"
            };

            var ids = imageRepo.InsertBatch([imgUnique, imgShared1, imgShared2]);
            queue.EnqueueForImages(ids);

            await cache.WriteAsync("hash_unique", 64, new byte[] { 1 });
            queue.CompleteJob("hash_unique", 64, 1, 64, 64);

            await cache.WriteAsync("hash_shared", 64, new byte[] { 2 });
            queue.CompleteJob("hash_shared", 64, 2, 64, 64);

            // Delete imgUnique from image table
            using (var conn = new DatabaseConnectionFactory(Path.Combine(tempDir, "data", "iconchest.db")).CreateAndOpenConnection())
            {
                conn.Execute("DELETE FROM image WHERE id = @Id;", new { Id = ids[0] });
            }

            // Cleanup orphans
            var orphansCleaned = provider.CleanupOrphans();
            // hash_unique had 3 sizes in thumbnail table
            Assert.Equal(3, orphansCleaned);

            // hash_unique is gone
            Assert.Null(queue.GetRecord("hash_unique", 64));
            Assert.False(cache.Exists("hash_unique", 64));

            // hash_shared is kept because imgShared2 still references it
            Assert.NotNull(queue.GetRecord("hash_shared", 64));
            Assert.True(cache.Exists("hash_shared", 64));
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ClearCache_And_VerifyCache_BehaveAsExpected()
    {
        var (_, imageRepo, folderRepo, cache, queue, provider, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img = new ImageRecord
            {
                FolderId = folderId,
                Path = "/test/verify.png",
                PathKey = "/test/verify.png",
                DirectoryPath = "/test",
                DirectoryKey = "/test",
                FileName = "verify.png",
                Extension = ".png",
                Format = "png",
                Width = 64,
                Height = 64,
                SizeBytes = 100,
                Hash = "hash_verify",
                HashAlgo = "xxh128",
                CreatedUtc = "2026-11-02T12:00:00Z",
                ModifiedUtc = "2026-11-02T12:00:00Z",
                AddedUtc = "2026-11-02T12:00:00Z",
                Title = "verify"
            };

            var id = imageRepo.InsertBatch([img])[0];
            queue.EnqueueForImages([id]);

            // Complete size 64 and size 128
            await cache.WriteAsync("hash_verify", 64, new byte[] { 1 });
            queue.CompleteJob("hash_verify", 64, 1, 64, 64);

            await cache.WriteAsync("hash_verify", 128, new byte[] { 2 });
            queue.CompleteJob("hash_verify", 128, 2, 128, 128);

            // Delete size 128 file from disk
            File.Delete(cache.GetThumbnailPath("hash_verify", 128));

            // Create an unindexed stale file on disk
            var stalePath = Path.Combine(cache.CacheDirectory, "99", "stalehash_64.webp");
            Directory.CreateDirectory(Path.GetDirectoryName(stalePath)!);
            await File.WriteAllBytesAsync(stalePath, new byte[] { 9, 9, 9 });

            // Verify cache
            var report = await provider.VerifyCacheAsync();
            Assert.Equal(2, report.TotalChecked);
            Assert.Equal(1, report.ValidDone);
            Assert.Equal(1, report.MissingFileRestoredToPending);
            Assert.Equal(1, report.StaleFilesDeleted);

            // Stale file was deleted
            Assert.False(File.Exists(stalePath));

            // Clear cache
            await provider.ClearCacheAsync(reEnqueueAll: false);
            var statusAfterClear = queue.GetStatus();
            Assert.Equal(0, statusAfterClear.TotalJobs);
            Assert.Equal(0, cache.GetTotalBytes());
        }
        finally
        {
            provider.Dispose();
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

