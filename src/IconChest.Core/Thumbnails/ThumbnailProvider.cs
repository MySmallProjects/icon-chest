using System.Collections.Concurrent;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Thumbnails;

public sealed class ThumbnailProvider : IThumbnailProvider
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IThumbnailQueue _queue;
    private readonly IThumbnailCache _cache;
    private readonly ISettingsService _settingsService;
    private readonly IClock _clock;
    private readonly ILogger<ThumbnailProvider> _logger;

    private readonly ConcurrentDictionary<(string Hash, int Size), string> _accessQueue = new();
    private readonly Timer? _flushTimer;
    private bool _disposed;

    public ThumbnailProvider(
        IDatabaseConnectionFactory connectionFactory,
        IThumbnailQueue queue,
        IThumbnailCache cache,
        ISettingsService settingsService,
        IClock? clock = null,
        ILogger<ThumbnailProvider>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _queue = queue;
        _cache = cache;
        _settingsService = settingsService;
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<ThumbnailProvider>.Instance;

        // Periodic access log flush every 30 seconds
        _flushTimer = new Timer(_ => FlushAccessLog(), null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));
    }

    public Task<ThumbnailResult> GetAsync(long imageId, int size, CancellationToken cancellationToken = default)
    {
        // 1. Resolve image hash
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var image = conn.QuerySingleOrDefault<(string Hash, int IsMissing, string Status)>(@"
            SELECT hash, is_missing AS IsMissing, status
            FROM image
            WHERE id = @imageId;",
            new { imageId });

        if (string.IsNullOrWhiteSpace(image.Hash))
        {
            return Task.FromResult(ThumbnailResult.NotFound());
        }

        var normalizedSize = ThumbnailSizes.PickFor(size);
        var record = _queue.GetRecord(image.Hash, normalizedSize);

        if (record == null)
        {
            // Missing row -> enqueue with raised priority
            _queue.EnqueueForImages([imageId]);
            _queue.Prioritize(image.Hash, normalizedSize);
            return Task.FromResult(ThumbnailResult.Pending());
        }

        switch (record.State.ToLowerInvariant())
        {
            case "done":
                var filePath = _cache.GetThumbnailPath(record.Hash, record.Size);
                if (!File.Exists(filePath))
                {
                    // Self-healing: disk file disappeared -> reset to pending with high priority
                    _logger.LogInformation("Thumbnail file missing on disk for ({Hash}, {Size}). Self-healing to pending.", record.Hash, record.Size);
                    _queue.MarkPending(record.Hash, record.Size, priority: 50);
                    return Task.FromResult(ThumbnailResult.Pending());
                }

                // Record access
                _accessQueue[(record.Hash, record.Size)] = _clock.UtcNow.ToString("o");
                var etag = $"\"{record.Hash}_{record.Size}\"";
                return Task.FromResult(ThumbnailResult.Ready(filePath, etag));

            case "pending":
            case "running":
                _queue.Prioritize(record.Hash, record.Size);
                return Task.FromResult(ThumbnailResult.Pending());

            case "evicted":
                // Evicted thumbnail requested -> regenerate with high priority
                _queue.MarkPending(record.Hash, record.Size, priority: 50);
                return Task.FromResult(ThumbnailResult.Pending());

            case "failed":
                return Task.FromResult(ThumbnailResult.Failed(record.Error));

            default:
                return Task.FromResult(ThumbnailResult.Failed($"Unknown state: {record.State}"));
        }
    }

    public void FlushAccessLog()
    {
        if (_accessQueue.IsEmpty) return;

        var entries = _accessQueue.ToArray();
        foreach (var entry in entries)
        {
            _accessQueue.TryRemove(entry.Key, out _);
        }

        if (entries.Length == 0) return;

        try
        {
            using var conn = _connectionFactory.CreateAndOpenConnection();
            using var tx = conn.BeginTransaction();

            foreach (var kv in entries)
            {
                conn.Execute(@"
                    UPDATE thumbnail
                    SET last_accessed_utc = @lastAccessedUtc
                    WHERE hash = @hash AND size = @size;",
                    new { lastAccessedUtc = kv.Value, hash = kv.Key.Hash, size = kv.Key.Size },
                    transaction: tx);
            }

            tx.Commit();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to flush thumbnail access log to database.");
        }
    }

    public int RunEviction()
    {
        int maxMb = _settingsService.Current.ThumbnailCacheMaxMb;
        if (maxMb <= 0) return 0;

        long maxBytes = (long)maxMb * 1024 * 1024;
        long currentBytes = _cache.GetTotalBytes();
        if (currentBytes <= maxBytes) return 0;

        long targetBytes = (long)(maxBytes * 0.90); // Evict down to 90%
        _logger.LogInformation("Cache size ({CurrentMb} MB) exceeds maximum ({MaxMb} MB). Evicting to target {TargetMb} MB.",
            currentBytes / (1024 * 1024), maxMb, targetBytes / (1024 * 1024));

        using var conn = _connectionFactory.CreateAndOpenConnection();
        // Query done thumbnails ordered by least recently accessed
        var candidates = conn.Query<(string Hash, int Size, long? Bytes)>(@"
            SELECT hash, size, bytes
            FROM thumbnail
            WHERE state = 'done'
            ORDER BY COALESCE(last_accessed_utc, created_utc) ASC;").ToList();

        int evictedCount = 0;
        var now = _clock.UtcNow.ToString("o");

        foreach (var cand in candidates)
        {
            if (currentBytes <= targetBytes) break;

            _cache.Delete(cand.Hash, cand.Size);

            conn.Execute(@"
                UPDATE thumbnail
                SET state = 'evicted',
                    updated_utc = @now
                WHERE hash = @hash AND size = @size;",
                new { hash = cand.Hash, size = cand.Size, now });

            currentBytes -= cand.Bytes ?? 0;
            evictedCount++;
        }

        _logger.LogInformation("Eviction complete. Evicted {Count} thumbnails.", evictedCount);
        return evictedCount;
    }

    public int CleanupOrphans()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var orphanedThumbnails = conn.Query<(string Hash, int Size)>(@"
            SELECT DISTINCT t.hash, t.size
            FROM thumbnail t
            WHERE t.hash NOT IN (
                SELECT DISTINCT hash FROM image WHERE hash IS NOT NULL
            );").ToList();

        if (orphanedThumbnails.Count == 0) return 0;

        _logger.LogInformation("Cleaning up {Count} orphaned thumbnails from cache and database.", orphanedThumbnails.Count);

        using var tx = conn.BeginTransaction();
        foreach (var (hash, size) in orphanedThumbnails)
        {
            _cache.Delete(hash, size);
            conn.Execute("DELETE FROM thumbnail WHERE hash = @hash AND size = @size;", new { hash, size }, transaction: tx);
        }

        tx.Commit();
        return orphanedThumbnails.Count;
    }

    public Task ClearCacheAsync(bool reEnqueueAll = false, CancellationToken cancellationToken = default)
    {
        FlushAccessLog();

        _cache.ClearAll();

        using (var conn = _connectionFactory.CreateAndOpenConnection())
        {
            conn.Execute("DELETE FROM thumbnail;");
        }

        if (reEnqueueAll)
        {
            _queue.EnqueueAll();
        }

        return Task.CompletedTask;
    }

    public Task<CacheVerificationReport> VerifyCacheAsync(CancellationToken cancellationToken = default)
    {
        FlushAccessLog();

        using var conn = _connectionFactory.CreateAndOpenConnection();
        var doneRecords = conn.Query<(string Hash, int Size)>(@"
            SELECT hash, size
            FROM thumbnail
            WHERE state = 'done';").ToList();

        int validDone = 0;
        int restoredPending = 0;

        foreach (var (hash, size) in doneRecords)
        {
            if (_cache.Exists(hash, size))
            {
                validDone++;
            }
            else
            {
                _queue.MarkPending(hash, size, priority: 0);
                restoredPending++;
            }
        }

        // Check for stale files on disk with no corresponding 'done' row
        int staleDeleted = 0;
        if (Directory.Exists(_cache.CacheDirectory))
        {
            var files = Directory.EnumerateFiles(_cache.CacheDirectory, "*.webp", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var fileName = Path.GetFileNameWithoutExtension(file);
                var lastUnderscore = fileName.LastIndexOf('_');
                if (lastUnderscore > 0 && int.TryParse(fileName[(lastUnderscore + 1)..], out int size))
                {
                    var hash = fileName[..lastUnderscore];
                    var isDone = conn.ExecuteScalar<int>(@"
                        SELECT COUNT(*)
                        FROM thumbnail
                        WHERE hash = @hash AND size = @size AND state = 'done';",
                        new { hash, size }) > 0;

                    if (!isDone)
                    {
                        try
                        {
                            File.Delete(file);
                            staleDeleted++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to delete stale thumbnail file {File}", file);
                        }
                    }
                }
            }
        }

        return Task.FromResult(new CacheVerificationReport(
            doneRecords.Count,
            validDone,
            restoredPending,
            staleDeleted));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _flushTimer?.Dispose();
        FlushAccessLog();
    }
}
