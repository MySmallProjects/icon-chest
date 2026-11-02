using IconChest.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Thumbnails;

public sealed class ThumbnailCache : IThumbnailCache
{
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ThumbnailCache> _logger;

    public string CacheDirectory
    {
        get
        {
            var customPath = _settingsService.Current.ThumbnailCachePath;
            if (!string.IsNullOrWhiteSpace(customPath))
            {
                return Path.GetFullPath(customPath);
            }

            return _settingsService.Paths.DefaultThumbnailCachePath;
        }
    }

    public ThumbnailCache(ISettingsService settingsService, ILogger<ThumbnailCache>? logger = null)
    {
        _settingsService = settingsService;
        _logger = logger ?? NullLogger<ThumbnailCache>.Instance;
    }

    public string GetThumbnailPath(string hash, int size)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        var normalizedHash = hash.Trim().ToLowerInvariant();
        var prefix = normalizedHash.Length >= 2 ? normalizedHash[..2] : "00";
        var fileName = $"{normalizedHash}_{size}.webp";
        return Path.Combine(CacheDirectory, prefix, fileName);
    }

    public bool Exists(string hash, int size)
    {
        var path = GetThumbnailPath(hash, size);
        return File.Exists(path);
    }

    public async Task<long> WriteAsync(string hash, int size, byte[] bytes, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentNullException.ThrowIfNull(bytes);

        var targetPath = GetThumbnailPath(hash, size);
        var dir = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(dir, $"{hash}_{size}.{Guid.NewGuid():N}.tmp");

        try
        {
            await File.WriteAllBytesAsync(tempPath, bytes, cancellationToken);
            File.Move(tempPath, targetPath, overwrite: true);
            return bytes.Length;
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean up temporary file {TempPath}", tempPath);
                }
            }
        }
    }

    public bool Delete(string hash, int size)
    {
        var path = GetThumbnailPath(hash, size);
        if (File.Exists(path))
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete thumbnail file {Path}", path);
                return false;
            }
        }

        return false;
    }

    public long GetTotalBytes()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            return 0;
        }

        long total = 0;
        try
        {
            var files = Directory.EnumerateFiles(CacheDirectory, "*.webp", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                try
                {
                    var fi = new FileInfo(file);
                    total += fi.Length;
                }
                catch
                {
                    // Ignore files deleted concurrently
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error calculating total cache size in {CacheDir}", CacheDirectory);
        }

        return total;
    }

    public int CleanStaleTempFiles()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            return 0;
        }

        int cleaned = 0;
        try
        {
            var tempFiles = Directory.EnumerateFiles(CacheDirectory, "*.tmp", SearchOption.AllDirectories);
            foreach (var file in tempFiles)
            {
                try
                {
                    File.Delete(file);
                    cleaned++;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not delete stale temp file {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during stale temp file cleanup");
        }

        return cleaned;
    }

    public void ClearAll()
    {
        if (!Directory.Exists(CacheDirectory))
        {
            return;
        }

        try
        {
            foreach (var subDir in Directory.GetDirectories(CacheDirectory))
            {
                try
                {
                    Directory.Delete(subDir, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete directory {Dir}", subDir);
                }
            }

            foreach (var file in Directory.GetFiles(CacheDirectory))
            {
                try
                {
                    File.Delete(file);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to delete file {File}", file);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error clearing cache directory {CacheDir}", CacheDirectory);
        }
    }
}

