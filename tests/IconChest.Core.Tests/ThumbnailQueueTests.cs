using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Xunit;

namespace IconChest.Core.Tests;

public class ThumbnailQueueTests
{
    private sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; }
        public FakeClock(DateTime initialUtc) => UtcNow = initialUtc;
    }

    private static (IconChestDatabase Db, ImageRepository ImageRepo, LibraryFolderRepository FolderRepo, ThumbnailQueue Queue, FakeClock Clock, string TempDir) CreateTestContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"thumb-queue-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        var configDir = Path.Combine(tempDir, "config");
        var dataDir = Path.Combine(tempDir, "data");
        var paths = new AppPaths(configDirOverride: configDir, dataDirOverride: dataDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);

        var db = new IconChestDatabase(settings);
        db.Migrate();

        var connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        var clock = new FakeClock(new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc));
        var folderRepo = new LibraryFolderRepository(connFactory);
        var imageRepo = new ImageRepository(connFactory);
        var queue = new ThumbnailQueue(connFactory, clock);

        return (db, imageRepo, folderRepo, queue, clock, tempDir);
    }

    private static ImageRecord CreateTestRecord(long folderId, string name, string hash, string status = "ok", bool isMissing = false)
    {
        return new ImageRecord
        {
            FolderId = folderId,
            Path = $"/test/{name}",
            PathKey = $"/test/{name}",
            DirectoryPath = "/test",
            DirectoryKey = "/test",
            FileName = name,
            Extension = Path.GetExtension(name),
            Format = "png",
            Width = 64,
            Height = 64,
            SizeBytes = 1024,
            Hash = hash,
            HashAlgo = "xxh128",
            CreatedUtc = "2026-11-02T12:00:00Z",
            ModifiedUtc = "2026-11-02T12:00:00Z",
            AddedUtc = "2026-11-02T12:00:00Z",
            Title = name,
            IsMissing = isMissing,
            Status = status
        };
    }

    [Fact]
    public void Enqueue_Idempotent_ThreeSizesPerDistinctHash_DuplicatesShareJobs()
    {
        var (_, imageRepo, folderRepo, queue, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img1 = CreateTestRecord(folderId, "icon1.png", "hash_alpha");
            var img2 = CreateTestRecord(folderId, "icon2.png", "hash_alpha"); // duplicate of img1
            var img3 = CreateTestRecord(folderId, "icon3.png", "hash_beta");

            var ids = imageRepo.InsertBatch([img1, img2, img3]);

            // Enqueue
            var enqueued1 = queue.EnqueueForImages(ids);
            // 2 distinct hashes * 3 sizes = 6 jobs
            Assert.Equal(6, enqueued1);

            // Re-enqueuing must be idempotent -> 0 new rows
            var enqueued2 = queue.EnqueueForImages(ids);
            Assert.Equal(0, enqueued2);

            var status = queue.GetStatus();
            Assert.Equal(6, status.TotalJobs);
            Assert.Equal(6, status.Pending);
            Assert.Equal(2, status.CountsBySize[64]);
            Assert.Equal(2, status.CountsBySize[128]);
            Assert.Equal(2, status.CountsBySize[256]);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void Enqueue_SkipsMissingAndUnreadableImages_DoesNotReEnqueueEvictedRows()
    {
        var (db, imageRepo, folderRepo, queue, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var missing = CreateTestRecord(folderId, "missing.png", "hash_missing", isMissing: true);
            var unreadable = CreateTestRecord(folderId, "bad.png", "hash_bad", status: "unreadable");
            var valid = CreateTestRecord(folderId, "valid.png", "hash_valid");

            var ids = imageRepo.InsertBatch([missing, unreadable, valid]);
            var enqueued = queue.EnqueueForImages(ids);
            Assert.Equal(3, enqueued); // only valid's 3 sizes

            // Manually mark one valid job as evicted
            using (var conn = db.OpenConnection())
            {
                conn.Execute("UPDATE thumbnail SET state = 'evicted' WHERE hash = 'hash_valid' AND size = 64;");
            }

            // Re-enqueuing must NOT re-enqueue evicted rows
            var reEnqueued = queue.EnqueueForImages(ids);
            Assert.Equal(0, reEnqueued);

            var record64 = queue.GetRecord("hash_valid", 64);
            Assert.NotNull(record64);
            Assert.Equal("evicted", record64.State);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ClaimOrder_SmallerSizesFirst_PrioritizeReorders()
    {
        var (_, imageRepo, folderRepo, queue, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img = CreateTestRecord(folderId, "icon.png", "hash_order");
            var ids = imageRepo.InsertBatch([img]);
            queue.EnqueueForImages(ids);

            // Default claim order within equal priority (0): size ASC (64, 128, 256)
            var job1 = queue.ClaimNextJob();
            Assert.NotNull(job1);
            Assert.Equal(64, job1.Size);
            Assert.Equal("running", job1.State);

            // Prioritize size 256 so it jumps size 128
            queue.Prioritize("hash_order", 256);

            var job2 = queue.ClaimNextJob();
            Assert.NotNull(job2);
            Assert.Equal(256, job2.Size);

            var job3 = queue.ClaimNextJob();
            Assert.NotNull(job3);
            Assert.Equal(128, job3.Size);

            // No more pending jobs
            Assert.Null(queue.ClaimNextJob());
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void DelayedRetries_AreNotClaimedEarly_RespectedByClock()
    {
        var (_, imageRepo, folderRepo, queue, clock, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img = CreateTestRecord(folderId, "icon.png", "hash_retry");
            var ids = imageRepo.InsertBatch([img]);
            queue.EnqueueForImages(ids);

            // Claim 64
            var job = queue.ClaimNextJob();
            Assert.NotNull(job);

            // Fail with non-deterministic error (e.g. transient file read error)
            queue.RecordAttemptFailure(job.Hash, job.Size, "transient read error", isDeterministic: false);

            var rec = queue.GetRecord(job.Hash, job.Size);
            Assert.NotNull(rec);
            Assert.Equal("pending", rec.State);
            Assert.Equal(1, rec.Attempts);
            Assert.NotNull(rec.NextAttemptUtc);

            // Immediate claim should NOT claim the delayed job (it will claim size 128 instead)
            var next1 = queue.ClaimNextJob();
            Assert.NotNull(next1);
            Assert.Equal(128, next1.Size);

            var next2 = queue.ClaimNextJob();
            Assert.NotNull(next2);
            Assert.Equal(256, next2.Size);

            // All other jobs claimed, but 64 retry delay is 30s in future
            Assert.Null(queue.ClaimNextJob());

            // Advance clock by 35 seconds
            clock.UtcNow = clock.UtcNow.AddSeconds(35);

            // Now 64 is eligible and claimed!
            var retriedJob = queue.ClaimNextJob();
            Assert.NotNull(retriedJob);
            Assert.Equal(64, retriedJob.Size);
            Assert.Equal(1, retriedJob.Attempts);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RetryBackoff_SequenceEndsInFailedAfterThreeAttempts_DeterministicFailsAtOnce()
    {
        var (_, imageRepo, folderRepo, queue, clock, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img = CreateTestRecord(folderId, "icon.png", "hash_fail");
            var ids = imageRepo.InsertBatch([img]);
            queue.EnqueueForImages(ids);

            // 1. Deterministic failure fails on attempt 1
            queue.RecordAttemptFailure("hash_fail", 64, "unreadable header", isDeterministic: true);
            var rec64 = queue.GetRecord("hash_fail", 64);
            Assert.NotNull(rec64);
            Assert.Equal("failed", rec64.State);
            Assert.Equal("unreadable header", rec64.Error);

            // 2. Retryable failure backoff on size 128:
            // Attempt 1 -> 30s delay
            queue.RecordAttemptFailure("hash_fail", 128, "transient error 1", isDeterministic: false);
            var rec128 = queue.GetRecord("hash_fail", 128);
            Assert.Equal("pending", rec128?.State);
            Assert.Equal(1, rec128?.Attempts);

            // Attempt 2 -> 2m delay
            clock.UtcNow = clock.UtcNow.AddSeconds(35);
            queue.RecordAttemptFailure("hash_fail", 128, "transient error 2", isDeterministic: false);
            rec128 = queue.GetRecord("hash_fail", 128);
            Assert.Equal("pending", rec128?.State);
            Assert.Equal(2, rec128?.Attempts);

            // Attempt 3 -> exceeds threshold, marks failed
            clock.UtcNow = clock.UtcNow.AddMinutes(3);
            queue.RecordAttemptFailure("hash_fail", 128, "transient error 3", isDeterministic: false);
            rec128 = queue.GetRecord("hash_fail", 128);
            Assert.Equal("failed", rec128?.State);
            Assert.Equal(3, rec128?.Attempts);

            // 3. RetryFailed resets failed jobs to pending
            var resetCount = queue.RetryFailed();
            Assert.Equal(2, resetCount);

            rec64 = queue.GetRecord("hash_fail", 64);
            rec128 = queue.GetRecord("hash_fail", 128);
            Assert.Equal("pending", rec64?.State);
            Assert.Equal(0, rec64?.Attempts);
            Assert.Equal("pending", rec128?.State);
            Assert.Equal(0, rec128?.Attempts);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task ConcurrentClaim_FourWorkers_TwoHundredJobs_ProcessedExactlyOnce()
    {
        var (_, imageRepo, folderRepo, queue, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var records = new List<ImageRecord>();
            for (int i = 0; i < 70; i++)
            {
                records.Add(CreateTestRecord(folderId, $"img_{i}.png", $"hash_{i:D4}"));
            }

            var ids = imageRepo.InsertBatch(records);
            // 70 * 3 = 210 jobs
            var enqueued = queue.EnqueueForImages(ids);
            Assert.Equal(210, enqueued);

            var claimedJobs = new System.Collections.Concurrent.ConcurrentBag<(string Hash, int Size)>();

            // 4 concurrent tasks claiming jobs until queue is empty
            var tasks = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
            {
                while (true)
                {
                    var job = queue.ClaimNextJob();
                    if (job == null) break;
                    claimedJobs.Add((job.Hash, job.Size));
                    // Mark done immediately
                    queue.CompleteJob(job.Hash, job.Size, 100, 64, 64);
                }
            })).ToArray();

            await Task.WhenAll(tasks);

            Assert.Equal(210, claimedJobs.Count);
            // Every job claimed must be distinct
            var distinctClaimed = claimedJobs.Distinct().Count();
            Assert.Equal(210, distinctClaimed);

            var status = queue.GetStatus();
            Assert.Equal(210, status.Done);
            Assert.Equal(0, status.Pending);
            Assert.Equal(0, status.Running);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void RecoverInterruptedJobs_ResetsRunningToPending()
    {
        var (_, imageRepo, folderRepo, queue, _, tempDir) = CreateTestContext();
        try
        {
            var folderId = folderRepo.Add("/test");
            var img = CreateTestRecord(folderId, "icon.png", "hash_crash");
            var ids = imageRepo.InsertBatch([img]);
            queue.EnqueueForImages(ids);

            // Worker claims a job (becomes 'running')
            var job = queue.ClaimNextJob();
            Assert.NotNull(job);
            Assert.Equal("running", job.State);

            var statusBefore = queue.GetStatus();
            Assert.Equal(1, statusBefore.Running);

            // Simulate application crash & restart recovery
            var recovered = queue.RecoverInterruptedJobs();
            Assert.Equal(1, recovered);

            var statusAfter = queue.GetStatus();
            Assert.Equal(0, statusAfter.Running);
            Assert.Equal(3, statusAfter.Pending);
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}

