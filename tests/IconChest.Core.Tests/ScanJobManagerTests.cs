using IconChest.Core.Scanning;
using IconChest.Core.Scanning.Exceptions;
using Xunit;

namespace IconChest.Core.Tests;

public class ScanJobManagerTests
{
    private class FakeScanner : ILibraryScanner
    {
        public Func<long, IProgress<ScanProgress>?, CancellationToken, Task<ScanResult>>? OnScan { get; set; }

        public Task<ScanResult> ScanAsync(long folderId, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            if (OnScan != null) return OnScan(folderId, progress, cancellationToken);
            return Task.FromResult(new ScanResult { FolderId = folderId, FilesFound = 1, FilesProcessed = 1 });
        }

        public Task<ScanResult> RescanAsync(long folderId, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
            => ScanAsync(folderId, progress, cancellationToken);

        public async Task<IReadOnlyList<ScanResult>> ScanAllAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var res = await ScanAsync(0, progress, cancellationToken);
            return new List<ScanResult> { res };
        }
    }

    [Fact]
    public async Task StartScan_TracksJobAndCompletes()
    {
        var scanner = new FakeScanner();
        var manager = new ScanJobManager(scanner);

        var job = manager.StartScan(10);
        Assert.NotNull(job.JobId);
        Assert.Equal(10, job.FolderId);

        // Wait for background completion
        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout && job.Status != ScanJobStatus.Completed)
        {
            await Task.Delay(50);
        }

        Assert.Equal(ScanJobStatus.Completed, job.Status);
        Assert.NotNull(job.Result);
        Assert.Equal(1, job.Result.FilesFound);
    }

    [Fact]
    public void StartScan_ConcurrentScanOnSameFolder_ThrowsScanAlreadyRunningException()
    {
        var tcs = new TaskCompletionSource<ScanResult>();
        var scanner = new FakeScanner
        {
            OnScan = async (_, _, token) => await tcs.Task
        };
        var manager = new ScanJobManager(scanner);

        manager.StartScan(42);

        // Second scan on folder 42 while the first is running throws
        Assert.Throws<ScanAlreadyRunningException>(() => manager.StartScan(42));

        tcs.SetResult(new ScanResult { FolderId = 42 });
    }

    [Fact]
    public async Task CancelJob_SignalsCancellationAndSetsStatusCancelled()
    {
        var scanStarted = new TaskCompletionSource();
        var scanner = new FakeScanner
        {
            OnScan = async (_, _, token) =>
            {
                scanStarted.SetResult();
                await Task.Delay(10000, token);
                return new ScanResult();
            }
        };
        var manager = new ScanJobManager(scanner);

        var job = manager.StartScan(99);
        await scanStarted.Task;

        var cancelled = manager.CancelJob(job.JobId);
        Assert.True(cancelled);

        var timeout = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < timeout && job.Status != ScanJobStatus.Cancelled)
        {
            await Task.Delay(50);
        }

        Assert.Equal(ScanJobStatus.Cancelled, job.Status);
    }
}
