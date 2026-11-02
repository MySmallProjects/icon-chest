using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Xunit;

namespace IconChest.Core.Tests;

public class ThumbnailCacheTests
{
    private static (ThumbnailCache Cache, string TempCacheDir) CreateTestCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"thumb-cache-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var paths = new AppPaths(configDirOverride: tempDir, dataDirOverride: tempDir);
        var settings = new SettingsService(paths);
        settings.Update(s => s.ThumbnailCachePath = tempDir);

        var cache = new ThumbnailCache(settings);
        return (cache, tempDir);
    }

    [Fact]
    public void GetThumbnailPath_CreatesCorrectFanoutLayout()
    {
        var (cache, tempDir) = CreateTestCache();
        try
        {
            var hash = "ab1234567890abcdef1234567890abcdef";
            var path = cache.GetThumbnailPath(hash, 64);

            var expectedDir = Path.Combine(tempDir, "ab");
            var expectedFile = Path.Combine(expectedDir, $"{hash}_64.webp");

            Assert.Equal(expectedFile, path);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task WriteAsync_IsAtomic_LeavesNoTempFiles()
    {
        var (cache, tempDir) = CreateTestCache();
        try
        {
            var hash = "cd1234567890abcdef1234567890abcdef";
            var payload = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };

            var bytesWritten = await cache.WriteAsync(hash, 128, payload);
            Assert.Equal(8, bytesWritten);

            var targetPath = cache.GetThumbnailPath(hash, 128);
            Assert.True(File.Exists(targetPath));
            Assert.Equal(payload, await File.ReadAllBytesAsync(targetPath));

            // No leftover .tmp files
            var tmpFiles = Directory.GetFiles(tempDir, "*.tmp", SearchOption.AllDirectories);
            Assert.Empty(tmpFiles);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void CleanStaleTempFiles_RemovesOrphanedTmpFiles()
    {
        var (cache, tempDir) = CreateTestCache();
        try
        {
            var subDir = Path.Combine(tempDir, "ef");
            Directory.CreateDirectory(subDir);

            var orphan1 = Path.Combine(subDir, "stale1.tmp");
            var orphan2 = Path.Combine(tempDir, "stale2.tmp");
            var keepFile = Path.Combine(subDir, "ef123_64.webp");

            File.WriteAllText(orphan1, "stale");
            File.WriteAllText(orphan2, "stale");
            File.WriteAllText(keepFile, "keep");

            var cleaned = cache.CleanStaleTempFiles();
            Assert.Equal(2, cleaned);

            Assert.False(File.Exists(orphan1));
            Assert.False(File.Exists(orphan2));
            Assert.True(File.Exists(keepFile));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task GetTotalBytes_CalculatesSumOfAllWebpFiles()
    {
        var (cache, tempDir) = CreateTestCache();
        try
        {
            var hash1 = "11aaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            var hash2 = "22bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";

            await cache.WriteAsync(hash1, 64, new byte[100]);
            await cache.WriteAsync(hash1, 128, new byte[250]);
            await cache.WriteAsync(hash2, 64, new byte[500]);

            var total = cache.GetTotalBytes();
            Assert.Equal(850, total);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ClearAll_RemovesAllCachedThumbnails()
    {
        var (cache, tempDir) = CreateTestCache();
        try
        {
            var hash = "33cccccccccccccccccccccccccccccc";
            await cache.WriteAsync(hash, 64, new byte[100]);
            await cache.WriteAsync(hash, 128, new byte[200]);

            Assert.True(cache.Exists(hash, 64));
            Assert.True(cache.Exists(hash, 128));

            cache.ClearAll();

            Assert.False(cache.Exists(hash, 64));
            Assert.False(cache.Exists(hash, 128));
            Assert.Equal(0, cache.GetTotalBytes());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

