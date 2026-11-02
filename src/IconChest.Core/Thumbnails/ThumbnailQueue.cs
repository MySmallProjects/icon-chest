using System.Diagnostics;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;

namespace IconChest.Core.Thumbnails;

public sealed class ThumbnailQueue : IThumbnailQueue
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IClock _clock;

    // Throughput tracking
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _completedSinceStart;

    public ThumbnailQueue(IDatabaseConnectionFactory connectionFactory, IClock? clock = null)
    {
        _connectionFactory = connectionFactory;
        _clock = clock ?? SystemClock.Instance;
    }

    public int EnqueueForImages(IEnumerable<long> imageIds)
    {
        var ids = imageIds.ToList();
        if (ids.Count == 0) return 0;

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();

        // Query distinct eligible hashes
        var hashes = conn.Query<string>(@"
            SELECT DISTINCT hash
            FROM image
            WHERE id IN @ids
              AND is_missing = 0
              AND status = 'ok'
              AND size_bytes > 0
              AND hash IS NOT NULL;",
            new { ids },
            transaction: tx).ToList();

        if (hashes.Count == 0) return 0;

        var now = _clock.UtcNow.ToString("o");
        int inserted = 0;

        foreach (var hash in hashes)
        {
            foreach (var size in ThumbnailSizes.All)
            {
                var rows = conn.Execute(@"
                    INSERT OR IGNORE INTO thumbnail (
                        hash, size, state, priority, attempts, created_utc, updated_utc
                    ) VALUES (
                        @hash, @size, 'pending', 0, 0, @now, @now
                    );",
                    new { hash, size, now },
                    transaction: tx);

                inserted += rows;
            }
        }

        tx.Commit();
        return inserted;
    }

    public int EnqueueForFolder(long folderId)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();

        var hashes = conn.Query<string>(@"
            SELECT DISTINCT hash
            FROM image
            WHERE folder_id = @folderId
              AND is_missing = 0
              AND status = 'ok'
              AND size_bytes > 0
              AND hash IS NOT NULL;",
            new { folderId },
            transaction: tx).ToList();

        if (hashes.Count == 0) return 0;

        var now = _clock.UtcNow.ToString("o");
        int inserted = 0;

        foreach (var hash in hashes)
        {
            foreach (var size in ThumbnailSizes.All)
            {
                var rows = conn.Execute(@"
                    INSERT OR IGNORE INTO thumbnail (
                        hash, size, state, priority, attempts, created_utc, updated_utc
                    ) VALUES (
                        @hash, @size, 'pending', 0, 0, @now, @now
                    );",
                    new { hash, size, now },
                    transaction: tx);

                inserted += rows;
            }
        }

        tx.Commit();
        return inserted;
    }

    public int EnqueueAll()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();

        var hashes = conn.Query<string>(@"
            SELECT DISTINCT hash
            FROM image
            WHERE is_missing = 0
              AND status = 'ok'
              AND size_bytes > 0
              AND hash IS NOT NULL;",
            transaction: tx).ToList();

        if (hashes.Count == 0) return 0;

        var now = _clock.UtcNow.ToString("o");
        int inserted = 0;

        foreach (var hash in hashes)
        {
            foreach (var size in ThumbnailSizes.All)
            {
                var rows = conn.Execute(@"
                    INSERT OR IGNORE INTO thumbnail (
                        hash, size, state, priority, attempts, created_utc, updated_utc
                    ) VALUES (
                        @hash, @size, 'pending', 0, 0, @now, @now
                    );",
                    new { hash, size, now },
                    transaction: tx);

                inserted += rows;
            }
        }

        tx.Commit();
        return inserted;
    }

    public void Prioritize(string hash, int size)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");
        conn.Execute(@"
            UPDATE thumbnail
            SET priority = priority + 100,
                updated_utc = @now
            WHERE hash = @hash AND size = @size;",
            new { hash, size, now });
    }

    public void MarkPending(string hash, int size, int priority = 10)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");
        conn.Execute(@"
            UPDATE thumbnail
            SET state = 'pending',
                priority = MAX(priority, @priority),
                attempts = 0,
                next_attempt_utc = NULL,
                updated_utc = @now
            WHERE hash = @hash AND size = @size;",
            new { hash, size, priority, now });
    }

    public ThumbnailRecord? ClaimNextJob()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");

        // Atomic claim statement using CTE and RETURNING
        var sql = @"
            WITH next_job AS (
                SELECT hash, size
                FROM thumbnail
                WHERE state = 'pending'
                  AND (next_attempt_utc IS NULL OR next_attempt_utc <= @now)
                ORDER BY priority DESC, size ASC, next_attempt_utc ASC
                LIMIT 1
            )
            UPDATE thumbnail
            SET state = 'running',
                claimed_utc = @now,
                updated_utc = @now
            WHERE (hash, size) IN (SELECT hash, size FROM next_job)
            RETURNING hash, size, state, priority, attempts, next_attempt_utc AS NextAttemptUtc,
                      error, bytes, width, height, created_utc AS CreatedUtc,
                      updated_utc AS UpdatedUtc, last_accessed_utc AS LastAccessedUtc,
                      claimed_utc AS ClaimedUtc;";

        return conn.QuerySingleOrDefault<ThumbnailRecord>(sql, new { now });
    }

    public string? ResolveSourceFilePath(string hash)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.QueryFirstOrDefault<string>(@"
            SELECT path
            FROM image
            WHERE hash = @hash AND is_missing = 0
            ORDER BY id ASC
            LIMIT 1;", new { hash });
    }

    public void CompleteJob(string hash, int size, long bytes, int width, int height)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");
        conn.Execute(@"
            UPDATE thumbnail
            SET state = 'done',
                bytes = @bytes,
                width = @width,
                height = @height,
                error = NULL,
                updated_utc = @now
            WHERE hash = @hash AND size = @size;",
            new { hash, size, bytes, width, height, now });

        Interlocked.Increment(ref _completedSinceStart);
    }

    public void RecordAttemptFailure(string hash, int size, string error, bool isDeterministic)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var nowDt = _clock.UtcNow;
        var now = nowDt.ToString("o");

        var currentAttempts = conn.ExecuteScalar<int>(@"
            SELECT attempts
            FROM thumbnail
            WHERE hash = @hash AND size = @size;",
            new { hash, size });

        var newAttempts = currentAttempts + 1;

        if (isDeterministic || newAttempts >= 3)
        {
            conn.Execute(@"
                UPDATE thumbnail
                SET state = 'failed',
                    attempts = @newAttempts,
                    error = @error,
                    next_attempt_utc = NULL,
                    updated_utc = @now
                WHERE hash = @hash AND size = @size;",
                new { hash, size, newAttempts, error, now });
        }
        else
        {
            var delay = currentAttempts switch
            {
                0 => TimeSpan.FromSeconds(30),
                1 => TimeSpan.FromMinutes(2),
                _ => TimeSpan.FromMinutes(10)
            };

            var nextAttemptUtc = nowDt.Add(delay).ToString("o");

            conn.Execute(@"
                UPDATE thumbnail
                SET state = 'pending',
                    attempts = @newAttempts,
                    error = @error,
                    next_attempt_utc = @nextAttemptUtc,
                    updated_utc = @now
                WHERE hash = @hash AND size = @size;",
                new { hash, size, newAttempts, error, nextAttemptUtc, now });
        }
    }

    public void MarkOrphaned(string hash, int size)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");
        conn.Execute(@"
            UPDATE thumbnail
            SET state = 'orphaned',
                updated_utc = @now
            WHERE hash = @hash AND size = @size;",
            new { hash, size, now });
    }

    public void ResetToPending(string hash, int size)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");
        conn.Execute(@"
            UPDATE thumbnail
            SET state = 'pending',
                claimed_utc = NULL,
                next_attempt_utc = NULL,
                updated_utc = @now
            WHERE hash = @hash AND size = @size;",
            new { hash, size, now });
    }

    public int RecoverInterruptedJobs()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");
        return conn.Execute(@"
            UPDATE thumbnail
            SET state = 'pending',
                claimed_utc = NULL,
                next_attempt_utc = NULL,
                updated_utc = @now
            WHERE state = 'running';",
            new { now });
    }

    public int RetryFailed()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var now = _clock.UtcNow.ToString("o");
        return conn.Execute(@"
            UPDATE thumbnail
            SET state = 'pending',
                attempts = 0,
                error = NULL,
                next_attempt_utc = NULL,
                updated_utc = @now
            WHERE state = 'failed';",
            new { now });
    }

    public ThumbnailRecord? GetRecord(string hash, int size)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.QuerySingleOrDefault<ThumbnailRecord>(@"
            SELECT hash, size, state, priority, attempts, next_attempt_utc AS NextAttemptUtc,
                   error, bytes, width, height, created_utc AS CreatedUtc,
                   updated_utc AS UpdatedUtc, last_accessed_utc AS LastAccessedUtc,
                   claimed_utc AS ClaimedUtc
            FROM thumbnail
            WHERE hash = @hash AND size = @size;",
            new { hash, size });
    }

    public IReadOnlyList<ThumbnailRecord> GetRecordsForImage(long imageId)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.Query<ThumbnailRecord>(@"
            SELECT t.hash, t.size, t.state, t.priority, t.attempts, t.next_attempt_utc AS NextAttemptUtc,
                   t.error, t.bytes, t.width, t.height, t.created_utc AS CreatedUtc,
                   t.updated_utc AS UpdatedUtc, t.last_accessed_utc AS LastAccessedUtc,
                   t.claimed_utc AS ClaimedUtc
            FROM image i
            JOIN thumbnail t ON i.hash = t.hash
            WHERE i.id = @imageId
            ORDER BY t.size ASC;",
            new { imageId }).ToList();
    }

    public ThumbnailQueueStatus GetStatus()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        var stateCounts = conn.Query<(string State, int Count)>(@"
            SELECT state, COUNT(*)
            FROM thumbnail
            GROUP BY state;").ToDictionary(x => x.State, x => x.Count);

        var sizeCounts = conn.Query<(int Size, int Count)>(@"
            SELECT size, COUNT(*)
            FROM thumbnail
            GROUP BY size;").ToDictionary(x => x.Size, x => x.Count);

        int pending = stateCounts.GetValueOrDefault("pending", 0);
        int running = stateCounts.GetValueOrDefault("running", 0);
        int done = stateCounts.GetValueOrDefault("done", 0);
        int failed = stateCounts.GetValueOrDefault("failed", 0);
        int evicted = stateCounts.GetValueOrDefault("evicted", 0);
        int orphaned = stateCounts.GetValueOrDefault("orphaned", 0);
        int total = pending + running + done + failed + evicted + orphaned;

        double elapsedSeconds = _stopwatch.Elapsed.TotalSeconds;
        double itemsPerSecond = elapsedSeconds > 0.5 && _completedSinceStart > 0
            ? _completedSinceStart / elapsedSeconds
            : 0.0;

        TimeSpan? eta = null;
        if (itemsPerSecond > 0.05 && (pending + running) > 0)
        {
            eta = TimeSpan.FromSeconds((pending + running) / itemsPerSecond);
        }

        return new ThumbnailQueueStatus(
            total,
            pending,
            running,
            done,
            failed,
            evicted,
            orphaned,
            sizeCounts,
            itemsPerSecond,
            eta);
    }
}
