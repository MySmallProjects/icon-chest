using System.Collections.Concurrent;
using IconChest.Core.Common;
using IconChest.Core.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Thumbnails;

public sealed class ThumbnailWorker : IThumbnailWorker
{
    private readonly IThumbnailQueue _queue;
    private readonly IThumbnailCache _cache;
    private readonly IThumbnailRenderer _renderer;
    private readonly ISettingsService _settingsService;
    private readonly IThumbnailProvider? _provider;
    private readonly ILogger<ThumbnailWorker> _logger;

    private readonly CancellationTokenSource _cts = new();
    private readonly AsyncManualResetEvent _pauseEvent = new(initialState: true);
    private readonly SemaphoreSlim _workSignal = new(0, int.MaxValue);

    private readonly List<Task> _workerTasks = [];
    private int _activeWorkerCount;
    private bool _isStarted;
    private bool _disposed;

    public bool IsPaused => !_pauseEvent.IsSet;
    public bool IsRunning => _isStarted && !_cts.IsCancellationRequested;

    public event Action<ThumbnailRecord>? JobCompleted;
    public event Action? QueueDrained;

    public ThumbnailWorker(
        IThumbnailQueue queue,
        IThumbnailCache cache,
        IThumbnailRenderer renderer,
        ISettingsService settingsService,
        IThumbnailProvider? provider = null,
        ILogger<ThumbnailWorker>? logger = null)
    {
        _queue = queue;
        _cache = cache;
        _renderer = renderer;
        _settingsService = settingsService;
        _provider = provider;
        _logger = logger ?? NullLogger<ThumbnailWorker>.Instance;
    }

    public void Start()
    {
        if (_isStarted) return;
        _isStarted = true;

        _logger.LogInformation("Starting ThumbnailWorker service...");

        // Crash recovery and temp cleanup
        var recovered = _queue.RecoverInterruptedJobs();
        if (recovered > 0)
        {
            _logger.LogInformation("Recovered {Count} interrupted thumbnail jobs to pending state.", recovered);
        }

        var cleanedTemp = _cache.CleanStaleTempFiles();
        if (cleanedTemp > 0)
        {
            _logger.LogDebug("Cleaned up {Count} stale temporary files in thumbnail cache.", cleanedTemp);
        }

        int workerCount = ResolveWorkerCount();
        _logger.LogInformation("Launching {Count} thumbnail rendering worker tasks.", workerCount);

        for (int i = 0; i < workerCount; i++)
        {
            int workerId = i + 1;
            _workerTasks.Add(Task.Run(() => WorkerLoopAsync(workerId, _cts.Token)));
        }

        // Pulse work signal to kick off initial processing
        NotifyWorkAvailable();
    }

    public void NotifyWorkAvailable()
    {
        try
        {
            _workSignal.Release(Math.Max(1, ResolveWorkerCount()));
        }
        catch (ObjectDisposedException)
        {
            // Worker is shutting down
        }
    }

    public void Pause()
    {
        if (_pauseEvent.IsSet)
        {
            _logger.LogInformation("Pausing ThumbnailWorker.");
            _pauseEvent.Reset();
        }
    }

    public void Resume()
    {
        if (!_pauseEvent.IsSet)
        {
            _logger.LogInformation("Resuming ThumbnailWorker.");
            _pauseEvent.Set();
            NotifyWorkAvailable();
        }
    }

    public async Task StopAsync()
    {
        if (!_isStarted || _cts.IsCancellationRequested) return;

        _logger.LogInformation("Stopping ThumbnailWorker gracefully...");
        _cts.Cancel();
        _pauseEvent.Set(); // Unblock paused workers so they can exit

        try
        {
            await Task.WhenAll(_workerTasks);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Exception while awaiting thumbnail worker tasks completion.");
        }

        _logger.LogInformation("ThumbnailWorker stopped.");
    }

    private int ResolveWorkerCount()
    {
        int configured = _settingsService.Current.ThumbnailWorkers;
        if (configured > 0) return configured;
        return Math.Clamp(Environment.ProcessorCount, 1, 4);
    }

    private async Task WorkerLoopAsync(int workerId, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            // 1. Wait if paused
            await _pauseEvent.WaitAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) break;

            // 2. Try claim a job
            var job = _queue.ClaimNextJob();
            if (job == null)
            {
                // Check if queue is completely drained
                CheckQueueDrained();

                // Wait for notification or delayed retry polling (every 5 seconds)
                try
                {
                    await _workSignal.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                continue;
            }

            // 3. Process claimed job
            Interlocked.Increment(ref _activeWorkerCount);
            try
            {
                await ProcessJobAsync(workerId, job, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkerCount);
            }
        }
    }

    private async Task ProcessJobAsync(int workerId, ThumbnailRecord job, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Worker {WorkerId} processing thumbnail ({Hash}, {Size}).", workerId, job.Hash, job.Size);

        var sourcePath = _queue.ResolveSourceFilePath(job.Hash);
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            _logger.LogWarning("No non-missing image record found for hash {Hash}. Marking job orphaned.", job.Hash);
            _queue.MarkOrphaned(job.Hash, job.Size);
            return;
        }

        if (!File.Exists(sourcePath))
        {
            _logger.LogWarning("Source file {Path} for hash {Hash} does not exist on disk.", sourcePath, job.Hash);
            _queue.RecordAttemptFailure(job.Hash, job.Size, "Source file not found on disk.", isDeterministic: false);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            _queue.ResetToPending(job.Hash, job.Size);
            return;
        }

        int quality = _settingsService.Current.ThumbnailQuality;
        var renderResult = await _renderer.RenderAsync(sourcePath, job.Size, quality, cancellationToken);

        if (cancellationToken.IsCancellationRequested)
        {
            _queue.ResetToPending(job.Hash, job.Size);
            return;
        }

        if (renderResult.Status == ThumbnailRenderStatus.Success && renderResult.Bytes != null)
        {
            try
            {
                var bytesWritten = await _cache.WriteAsync(job.Hash, job.Size, renderResult.Bytes, cancellationToken);
                _queue.CompleteJob(job.Hash, job.Size, bytesWritten, renderResult.Width, renderResult.Height);
                _logger.LogDebug("Thumbnail ({Hash}, {Size}) successfully generated ({Width}x{Height}, {Bytes} bytes).",
                    job.Hash, job.Size, renderResult.Width, renderResult.Height, bytesWritten);

                JobCompleted?.Invoke(job);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to write thumbnail cache file for ({Hash}, {Size}).", job.Hash, job.Size);
                _queue.RecordAttemptFailure(job.Hash, job.Size, $"Cache write failure: {ex.Message}", isDeterministic: false);
            }
        }
        else
        {
            bool isDeterministic = renderResult.Status is ThumbnailRenderStatus.Unreadable
                                   or ThumbnailRenderStatus.TooLarge
                                   or ThumbnailRenderStatus.Unsupported;

            var errorMsg = renderResult.ErrorMessage ?? $"Render failed with status: {renderResult.Status}";
            _logger.LogWarning("Failed to render thumbnail ({Hash}, {Size}): {Error}", job.Hash, job.Size, errorMsg);
            _queue.RecordAttemptFailure(job.Hash, job.Size, errorMsg, isDeterministic);
        }
    }

    private void CheckQueueDrained()
    {
        if (_activeWorkerCount == 0)
        {
            var status = _queue.GetStatus();
            if (status.Pending == 0 && status.Running == 0)
            {
                QueueDrained?.Invoke();

                // Trigger eviction when queue drains
                try
                {
                    _provider?.RunEviction();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error running cache eviction after queue drained.");
                }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts.Cancel();
        _cts.Dispose();
        _pauseEvent.Dispose();
        _workSignal.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        await StopAsync();
        Dispose();
    }
}

/// <summary>
/// Lightweight async manual reset event for pause/resume.
/// </summary>
internal sealed class AsyncManualResetEvent : IDisposable
{
    private volatile TaskCompletionSource<bool> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public bool IsSet => _tcs.Task.IsCompleted;

    public AsyncManualResetEvent(bool initialState)
    {
        if (initialState)
        {
            _tcs.TrySetResult(true);
        }
    }

    public Task WaitAsync(CancellationToken cancellationToken = default)
    {
        return _tcs.Task.WaitAsync(cancellationToken);
    }

    public void Set()
    {
        _tcs.TrySetResult(true);
    }

    public void Reset()
    {
        if (_tcs.Task.IsCompleted)
        {
            _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public void Dispose()
    {
        _tcs.TrySetCanceled();
    }
}

