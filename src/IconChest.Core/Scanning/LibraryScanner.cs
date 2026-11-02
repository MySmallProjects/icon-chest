using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using IconChest.Core.Common;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning.Exceptions;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Scanning;

public sealed class LibraryScanner : ILibraryScanner
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".gif", ".webp", ".svg", ".bmp", ".ico"
    };

    private static readonly ConcurrentDictionary<string, byte> ActiveScans = new(StringComparer.OrdinalIgnoreCase);

    private readonly ILibraryFolderRepository _folderRepo;
    private readonly IImageRepository _imageRepo;
    private readonly ISettingsService _settingsService;
    private readonly IFileHasher _hasher;
    private readonly IImageMetadataReader _metadataReader;
    private readonly IPathKeyProvider _pathKeyProvider;
    private readonly IClock _clock;
    private readonly IThumbnailQueue? _thumbnailQueue;
    private readonly ThumbnailWorker? _thumbnailWorker;
    private readonly IThumbnailProvider? _thumbnailProvider;
    private readonly ILogger<LibraryScanner> _logger;

    public LibraryScanner(
        ILibraryFolderRepository folderRepo,
        IImageRepository imageRepo,
        ISettingsService settingsService,
        IFileHasher hasher,
        IImageMetadataReader metadataReader,
        IPathKeyProvider? pathKeyProvider = null,
        IClock? clock = null,
        IThumbnailQueue? thumbnailQueue = null,
        ThumbnailWorker? thumbnailWorker = null,
        IThumbnailProvider? thumbnailProvider = null,
        ILogger<LibraryScanner>? logger = null)
    {
        _folderRepo = folderRepo;
        _imageRepo = imageRepo;
        _settingsService = settingsService;
        _hasher = hasher;
        _metadataReader = metadataReader;
        _pathKeyProvider = pathKeyProvider ?? PathKeyProvider.Default;
        _clock = clock ?? SystemClock.Instance;
        _thumbnailQueue = thumbnailQueue;
        _thumbnailWorker = thumbnailWorker;
        _thumbnailProvider = thumbnailProvider;
        _logger = logger ?? NullLogger<LibraryScanner>.Instance;
    }

    public async Task<IReadOnlyList<ScanResult>> ScanAllAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var folders = _folderRepo.List().Where(f => f.Enabled).ToList();
        var results = new List<ScanResult>();

        foreach (var folder in folders)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            var res = await ScanAsync(folder.Id, progress, cancellationToken);
            results.Add(res);
        }

        return results;
    }

    public async Task<ScanResult> ScanAsync(long folderId, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var folder = _folderRepo.GetById(folderId);
        if (folder == null)
        {
            return new ScanResult { FolderId = folderId, IsFatalError = true, FatalErrorMessage = $"Folder with ID {folderId} not found." };
        }

        var scanKey = Path.GetFullPath(folder.Path);
        if (!ActiveScans.TryAdd(scanKey, 0))
        {
            throw new ScanAlreadyRunningException(folderId);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = new ScanResult { FolderId = folderId, FolderPath = folder.Path };
        var throttler = new ProgressThrottler(progress);

        try
        {
            _logger.LogInformation("Starting scan of library folder {FolderId}: {FolderPath}", folderId, folder.Path);
            throttler.Report(ScanStage.Starting, result, null, force: true);

            // 1. Safety check
            if (!Directory.Exists(folder.Path))
            {
                result.IsFatalError = true;
                result.FatalErrorMessage = $"Root path '{folder.Path}' does not exist or is unavailable.";
                _logger.LogError("Scan aborted: Root path '{FolderPath}' does not exist.", folder.Path);
                result.WarningsAndErrors.Add(result.FatalErrorMessage);
                return result;
            }

            try
            {
                _ = Directory.EnumerateFileSystemEntries(folder.Path).Take(1).ToList();
            }
            catch (Exception ex)
            {
                result.IsFatalError = true;
                result.FatalErrorMessage = $"Root path '{folder.Path}' cannot be listed: {ex.Message}";
                _logger.LogError(ex, "Scan aborted: Root path '{FolderPath}' cannot be listed.", folder.Path);
                result.WarningsAndErrors.Add(result.FatalErrorMessage);
                return result;
            }

            // 2. Resolve include and exclude patterns
            var settings = _settingsService.Current;
            var rawInclude = !string.IsNullOrWhiteSpace(folder.IncludePatterns)
                ? folder.IncludePatterns
                : settings.IncludePatterns;

            var includePatterns = (rawInclude ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            var excludeNames = (settings.ExcludeNames ?? string.Empty)
                .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            // 3. Recursive enumeration
            throttler.Report(ScanStage.Enumerating, result, folder.Path);
            var enumeratedFiles = new List<FileInfo>();
            var isIncomplete = false;

            EnumerateDirectory(new DirectoryInfo(folder.Path), excludeNames, includePatterns, enumeratedFiles, ref isIncomplete, result, cancellationToken);
            result.IsIncomplete = isIncomplete;
            result.FilesFound = enumeratedFiles.Count;

            if (cancellationToken.IsCancellationRequested)
            {
                result.IsCancelled = true;
                return result;
            }

            // 4. Load existing records for this folder
            var existingRecords = _imageRepo.GetRecordsByFolder(folderId);

            // 5. Classify files
            var filesToProcess = new List<(FileInfo File, string PathKey, string DirPath, string DirKey, string FileName, string Ext, ImageRecord? Existing)>();
            var foundKeys = new HashSet<string>(StringComparer.Ordinal);
            var restoredIds = new List<long>();

            foreach (var fi in enumeratedFiles)
            {
                var (pathKey, dirPath, dirKey, fileName, ext) = _pathKeyProvider.Analyze(fi.FullName);
                foundKeys.Add(pathKey);

                if (existingRecords.TryGetValue(pathKey, out var existing))
                {
                    // Check if unchanged (same size and same modified time)
                    var lastWriteUtc = fi.LastWriteTimeUtc;
                    var existingModUtc = DateTime.TryParse(existing.ModifiedUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var dt)
                        ? dt.ToUniversalTime()
                        : (DateTime?)null;

                    var isUnchanged = existing.SizeBytes == fi.Length &&
                                      existingModUtc.HasValue &&
                                      Math.Abs((lastWriteUtc - existingModUtc.Value).TotalSeconds) < 1.0;

                    if (isUnchanged)
                    {
                        result.UnchangedFiles++;
                        result.FilesProcessed++;
                        if (existing.IsMissing)
                        {
                            restoredIds.Add(existing.Id);
                        }
                        continue;
                    }

                    // Changed file
                    filesToProcess.Add((fi, pathKey, dirPath, dirKey, fileName, ext, existing));
                }
                else
                {
                    // New candidate
                    filesToProcess.Add((fi, pathKey, dirPath, dirKey, fileName, ext, null));
                }
            }

            if (restoredIds.Count > 0)
            {
                _imageRepo.RestoreMissingBatch(restoredIds);
            }

            // 6. Process files in parallel
            throttler.Report(ScanStage.Reading, result, null, force: true);
            var workerCount = settings.ScanWorkers <= 0
                ? Math.Clamp(Environment.ProcessorCount, 1, 4)
                : settings.ScanWorkers;

            var affectedHashes = new ConcurrentBag<string>();
            var newRecordsToInsert = new ConcurrentBag<ImageRecord>();
            var changedRecordsToUpdate = new ConcurrentBag<ImageRecord>();

            using (var semaphore = new SemaphoreSlim(workerCount))
            {
                var tasks = filesToProcess.Select(async item =>
                {
                    try
                    {
                        await semaphore.WaitAsync(cancellationToken);
                    }
                    catch (OperationCanceledException)
                    {
                        return;
                    }

                    try
                    {
                        if (cancellationToken.IsCancellationRequested) return;

                        throttler.Report(ScanStage.Reading, result, item.File.FullName);
                        var hash = await _hasher.HashFileAsync(item.File.FullName, cancellationToken);
                        var meta = await _metadataReader.ReadMetadataAsync(item.File.FullName, cancellationToken);

                        affectedHashes.Add(hash);

                        if (item.Existing != null)
                        {
                            // Changed record - update technical fields, preserve user edits
                            var updated = new ImageRecord
                            {
                                Id = item.Existing.Id,
                                FolderId = folderId,
                                Path = item.File.FullName,
                                PathKey = item.PathKey,
                                DirectoryPath = item.DirPath,
                                DirectoryKey = item.DirKey,
                                FileName = item.FileName,
                                Extension = item.Ext,
                                Format = meta.Format,
                                Width = meta.Width ?? 0,
                                Height = meta.Height ?? 0,
                                SizeBytes = meta.SizeBytes,
                                Hash = hash,
                                HashAlgo = _hasher.AlgorithmName,
                                CreatedUtc = meta.CreatedUtc?.ToString("o"),
                                ModifiedUtc = meta.ModifiedUtc?.ToString("o"),
                                ExifDateUtc = meta.ExifDateUtc,
                                HasTransparency = meta.HasTransparency,
                                Status = meta.Status,
                                ExifJson = meta.ExifJson
                            };

                            changedRecordsToUpdate.Add(updated);
                            Interlocked.Increment(ref result.ChangedFiles);
                        }
                        else
                        {
                            // New candidate
                            var newRecord = new ImageRecord
                            {
                                FolderId = folderId,
                                Path = item.File.FullName,
                                PathKey = item.PathKey,
                                DirectoryPath = item.DirPath,
                                DirectoryKey = item.DirKey,
                                FileName = item.FileName,
                                Extension = item.Ext,
                                Format = meta.Format,
                                Width = meta.Width ?? 0,
                                Height = meta.Height ?? 0,
                                SizeBytes = meta.SizeBytes,
                                Hash = hash,
                                HashAlgo = _hasher.AlgorithmName,
                                CreatedUtc = meta.CreatedUtc?.ToString("o"),
                                ModifiedUtc = meta.ModifiedUtc?.ToString("o"),
                                ExifDateUtc = meta.ExifDateUtc,
                                AddedUtc = _clock.UtcNow.ToString("o"),
                                Title = _metadataReader.DeriveDefaultTitle(item.FileName),
                                HasTransparency = meta.HasTransparency,
                                Status = meta.Status,
                                ExifJson = meta.ExifJson
                            };

                            newRecordsToInsert.Add(newRecord);
                        }

                        Interlocked.Increment(ref result.FilesProcessed);
                    }
                    catch (OperationCanceledException)
                    {
                        // Cancellation handled
                    }
                    catch (Exception ex)
                    {
                        Interlocked.Increment(ref result.Errors);
                        _logger.LogWarning(ex, "Error processing file {FilePath}", item.File.FullName);
                        lock (result.WarningsAndErrors)
                        {
                            if (result.WarningsAndErrors.Count < 50)
                            {
                                result.WarningsAndErrors.Add($"{item.File.FullName}: {ex.Message}");
                            }
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                try
                {
                    await Task.WhenAll(tasks);
                }
                catch (OperationCanceledException)
                {
                    // Cancellation handled
                }
            }

            if (cancellationToken.IsCancellationRequested)
            {
                result.IsCancelled = true;
                return result;
            }

            // Flush changed records updates in batches
            if (!changedRecordsToUpdate.IsEmpty)
            {
                foreach (var batch in changedRecordsToUpdate.Chunk(500))
                {
                    _imageRepo.UpdateTechnicalBatch(batch);
                }
            }

            // 7. Reconcile Moves
            throttler.Report(ScanStage.Reconciling, result, null, force: true);
            var libraryMovePool = _imageRepo.GetLibraryMoveCandidates()
                .Where(r => !foundKeys.Contains(r.PathKey) && (!File.Exists(r.Path) || r.IsMissing))
                .ToList();

            var moveUpdates = new List<ImageMoveUpdate>();
            var actualNewInserts = new List<ImageRecord>();

            foreach (var cand in newRecordsToInsert)
            {
                // Find candidates in pool with matching hash and size
                var matches = libraryMovePool
                    .Where(p => p.Hash == cand.Hash && p.SizeBytes == cand.SizeBytes)
                    .ToList();

                if (matches.Count > 0)
                {
                    // Prefer same filename, then lowest id
                    var match = matches
                        .OrderByDescending(m => string.Equals(m.FileName, cand.FileName, StringComparison.OrdinalIgnoreCase))
                        .ThenBy(m => m.Id)
                        .First();

                    moveUpdates.Add(new ImageMoveUpdate(
                        match.Id,
                        folderId,
                        cand.Path,
                        cand.PathKey,
                        cand.DirectoryPath,
                        cand.DirectoryKey,
                        cand.FileName,
                        cand.Extension,
                        cand.ModifiedUtc));

                    affectedHashes.Add(match.Hash);
                    libraryMovePool.Remove(match);
                    result.MovedFiles++;
                }
                else
                {
                    actualNewInserts.Add(cand);
                    result.NewFiles++;
                }
            }

            if (moveUpdates.Count > 0)
            {
                foreach (var batch in moveUpdates.Chunk(500))
                {
                    _imageRepo.UpdateMoveBatch(batch);
                }
            }

            var insertedIds = new List<long>();
            if (actualNewInserts.Count > 0)
            {
                foreach (var batch in actualNewInserts.Chunk(500))
                {
                    var ids = _imageRepo.InsertBatch(batch);
                    insertedIds.AddRange(ids);
                }
            }

            // 8. Mark missing (only if not cancelled, not incomplete, not fatal error)
            if (!result.IsCancelled && !result.IsIncomplete && !result.IsFatalError)
            {
                var movedOriginalIds = new HashSet<long>(moveUpdates.Select(m => m.Id));
                var missingIds = existingRecords.Values
                    .Where(r => !foundKeys.Contains(r.PathKey) && !movedOriginalIds.Contains(r.Id))
                    .Select(r => r.Id)
                    .ToList();

                if (missingIds.Count > 0)
                {
                    _imageRepo.MarkMissingBatch(missingIds, _clock.UtcNow.ToString("o"));
                    result.MissingFiles = missingIds.Count;
                }

                // 9. Auto-remove missing if setting enabled
                if (settings.AutoRemoveMissing)
                {
                    _imageRepo.DeleteMissingForFolder(folderId);
                    _thumbnailProvider?.CleanupOrphans();
                }
            }

            // 10. Recompute duplicates for affected hashes
            throttler.Report(ScanStage.Finalising, result, null, force: true);
            _imageRepo.RecomputeDuplicates(affectedHashes);
            result.Duplicates = _imageRepo.Count(folderId: folderId, duplicatesOnly: true);

            // 11. Update last scan timestamp
            _folderRepo.UpdateLastScan(folderId, _clock.UtcNow.ToString("o"));

            // 12. Enqueue thumbnail jobs for new, changed, moved and restored images
            if (!result.IsCancelled && !result.IsFatalError && _thumbnailQueue != null)
            {
                try
                {
                    var targetImageIds = new List<long>();
                    targetImageIds.AddRange(insertedIds);
                    targetImageIds.AddRange(changedRecordsToUpdate.Select(c => c.Id));
                    targetImageIds.AddRange(moveUpdates.Select(m => m.Id));
                    targetImageIds.AddRange(restoredIds);

                    if (targetImageIds.Count > 0)
                    {
                        result.ThumbnailsEnqueued = _thumbnailQueue.EnqueueForImages(targetImageIds);
                        _thumbnailWorker?.NotifyWorkAvailable();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enqueue thumbnail jobs after scan.");
                }
            }

            throttler.Report(ScanStage.Completed, result, null, force: true);

            _logger.LogInformation("Completed scan of folder {FolderId}. Found: {Found}, New: {New}, Changed: {Changed}, Unchanged: {Unchanged}, Moved: {Moved}, Missing: {Missing}, Duplicates: {Dupes}",
                folderId, result.FilesFound, result.NewFiles, result.ChangedFiles, result.UnchangedFiles, result.MovedFiles, result.MissingFiles, result.Duplicates);

            return result;
        }
        finally
        {
            stopwatch.Stop();
            result.Duration = stopwatch.Elapsed;
            ActiveScans.TryRemove(scanKey, out _);
        }
    }

    private void EnumerateDirectory(
        DirectoryInfo dir,
        List<string> excludeNames,
        List<string> includePatterns,
        List<FileInfo> collectedFiles,
        ref bool isIncomplete,
        ScanResult result,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return;

        // Check if directory matches exclude rules
        if (excludeNames.Any(ex => string.Equals(dir.Name, ex, StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogDebug("Skipping excluded directory: {Dir}", dir.FullName);
            return;
        }

        // Do not follow symlink directories (reparse points)
        if (dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            _logger.LogDebug("Skipping symlink directory: {Dir}", dir.FullName);
            return;
        }

        IEnumerable<FileSystemInfo> entries;
        try
        {
            entries = dir.EnumerateFileSystemInfos();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot read directory {Dir}. Flagging scan as incomplete.", dir.FullName);
            isIncomplete = true;
            lock (result.WarningsAndErrors)
            {
                if (result.WarningsAndErrors.Count < 50)
                {
                    result.WarningsAndErrors.Add($"Cannot read directory '{dir.FullName}': {ex.Message}");
                }
            }
            return;
        }

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested) return;

            if (entry is DirectoryInfo subDir)
            {
                EnumerateDirectory(subDir, excludeNames, includePatterns, collectedFiles, ref isIncomplete, result, cancellationToken);
            }
            else if (entry is FileInfo fi)
            {
                // Check format support first
                var ext = fi.Extension;
                if (!SupportedExtensions.Contains(ext))
                {
                    _logger.LogDebug("Skipping unsupported file extension: {File}", fi.FullName);
                    continue;
                }

                // Check wildcard include patterns
                var matchesInclude = includePatterns.Count == 0 ||
                                     includePatterns.Any(pat => WildcardMatcher.IsMatch(fi.Name, pat, ignoreCase: true));

                if (!matchesInclude)
                {
                    _logger.LogDebug("File does not match include patterns: {File}", fi.FullName);
                    continue;
                }

                collectedFiles.Add(fi);
            }
        }
    }

    private sealed class ProgressThrottler
    {
        private readonly IProgress<ScanProgress>? _progress;
        private DateTime _lastReportTime = DateTime.MinValue;
        private readonly object _lock = new();

        public ProgressThrottler(IProgress<ScanProgress>? progress)
        {
            _progress = progress;
        }

        public void Report(ScanStage stage, ScanResult result, string? currentPath, bool force = false)
        {
            if (_progress == null) return;

            lock (_lock)
            {
                var now = DateTime.UtcNow;
                if (!force && (now - _lastReportTime).TotalMilliseconds < 100)
                {
                    return;
                }

                _lastReportTime = now;
                _progress.Report(new ScanProgress(
                    stage,
                    result.FilesFound,
                    result.FilesProcessed,
                    result.NewFiles,
                    result.ChangedFiles,
                    result.UnchangedFiles,
                    result.MovedFiles,
                    result.MissingFiles,
                    result.Duplicates,
                    result.Errors,
                    currentPath));
            }
        }
    }
}

