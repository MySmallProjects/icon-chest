using System.Collections.Concurrent;
using System.Diagnostics;
using IconChest.Core.Common;
using IconChest.Core.Events;
using IconChest.Core.Scanning.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Scanning;

public sealed class ScanJobManager : IScanJobManager
{
    private const int MaxHistory = 20;

    private readonly ILibraryScanner _scanner;
    private readonly ILibraryEventEmitter? _eventEmitter;
    private readonly IClock _clock;
    private readonly ILogger<ScanJobManager> _logger;

    private readonly object _lock = new();
    private readonly List<ScanJobInfo> _jobHistory = new();
    private readonly ConcurrentDictionary<string, (ScanJobInfo Job, CancellationTokenSource Cts)> _activeJobs = new();
    private readonly ConcurrentDictionary<long, string> _activeFolderToJobMap = new();

    public ScanJobManager(
        ILibraryScanner scanner,
        ILibraryEventEmitter? eventEmitter = null,
        IClock? clock = null,
        ILogger<ScanJobManager>? logger = null)
    {
        _scanner = scanner;
        _eventEmitter = eventEmitter;
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<ScanJobManager>.Instance;
    }

    public ScanJobInfo StartScan(long folderId)
    {
        var job = new ScanJobInfo
        {
            JobId = Guid.NewGuid().ToString("N"),
            FolderId = folderId,
            Status = ScanJobStatus.Pending,
            CreatedUtc = _clock.UtcNow
        };

        var cts = new CancellationTokenSource();

        lock (_lock)
        {
            if (!_activeFolderToJobMap.TryAdd(folderId, job.JobId))
            {
                throw new ScanAlreadyRunningException(folderId);
            }

            _activeJobs[job.JobId] = (job, cts);
            _jobHistory.Add(job);
            TrimHistoryUnderLock();
        }

        // Run scan on background task
        _ = Task.Run(async () =>
        {
            job.StartedUtc = _clock.UtcNow;
            job.Status = ScanJobStatus.Running;

            long lastProgressReportMs = 0;
            var progressStopwatch = Stopwatch.StartNew();

            var progressReporter = new Progress<ScanProgress>(p =>
            {
                job.Progress = p;

                long nowMs = progressStopwatch.ElapsedMilliseconds;
                // Throttle progress emissions to ~5/sec (every 200ms) unless finished/failed
                if (nowMs - lastProgressReportMs >= 200 || p.Stage == ScanStage.Completed || p.Stage == ScanStage.Failed)
                {
                    lastProgressReportMs = nowMs;
                    _eventEmitter?.EmitScanProgress(new
                    {
                        jobId = job.JobId,
                        folderId = job.FolderId,
                        folderPath = job.FolderPath,
                        stage = p.Stage.ToString(),
                        filesFound = p.FilesFound,
                        filesProcessed = p.FilesProcessed,
                        newFiles = p.NewFiles,
                        changedFiles = p.ChangedFiles,
                        movedFiles = p.MovedFiles,
                        missingFiles = p.MissingFiles,
                        errorCount = p.Errors,
                        currentFile = p.CurrentPath
                    });
                }
            });

            try
            {
                var result = await _scanner.ScanAsync(folderId, progressReporter, cts.Token);
                job.Result = result;
                job.FolderPath = result.FolderPath;
                job.Status = result.IsFatalError ? ScanJobStatus.Failed : ScanJobStatus.Completed;
                if (result.IsFatalError)
                {
                    job.ErrorMessage = result.FatalErrorMessage;
                }
            }
            catch (OperationCanceledException)
            {
                job.Status = ScanJobStatus.Cancelled;
                job.ErrorMessage = "Scan was cancelled.";
            }
            catch (Exception ex)
            {
                job.Status = ScanJobStatus.Failed;
                job.ErrorMessage = ex.Message;
                _logger.LogWarning(ex, "Scan job {JobId} for folder {FolderId} failed.", job.JobId, folderId);
            }
            finally
            {
                job.FinishedUtc = _clock.UtcNow;
                _activeFolderToJobMap.TryRemove(folderId, out _);
                _activeJobs.TryRemove(job.JobId, out _);
                cts.Dispose();

                _eventEmitter?.EmitScanCompleted(new
                {
                    jobId = job.JobId,
                    folderId = job.FolderId,
                    folderPath = job.FolderPath,
                    status = job.Status.ToString().ToLowerInvariant(),
                    errorMessage = job.ErrorMessage,
                    result = job.Result
                });

                if (job.Status == ScanJobStatus.Completed)
                {
                    _eventEmitter?.EmitLibraryChanged();
                }
            }
        });

        return job;
    }

    public IReadOnlyList<ScanJobInfo> GetRecentJobs(int limit = 20)
    {
        lock (_lock)
        {
            return _jobHistory
                .OrderByDescending(j => j.CreatedUtc)
                .Take(limit)
                .ToList();
        }
    }

    public ScanJobInfo? GetJob(string jobId)
    {
        lock (_lock)
        {
            return _jobHistory.FirstOrDefault(j => j.JobId.Equals(jobId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool CancelJob(string jobId)
    {
        if (_activeJobs.TryGetValue(jobId, out var entry))
        {
            entry.Cts.Cancel();
            return true;
        }

        return false;
    }

    private void TrimHistoryUnderLock()
    {
        while (_jobHistory.Count > MaxHistory)
        {
            // Only trim completed, failed or cancelled jobs
            var oldestFinishedIndex = _jobHistory.FindIndex(j => j.Status is ScanJobStatus.Completed or ScanJobStatus.Failed or ScanJobStatus.Cancelled);
            if (oldestFinishedIndex >= 0)
            {
                _jobHistory.RemoveAt(oldestFinishedIndex);
            }
            else
            {
                break;
            }
        }
    }
}
