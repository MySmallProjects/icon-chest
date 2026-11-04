using System.Text.Json.Serialization;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data.Migrations;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Events;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Data;

public sealed record BackupResult(string Path, long SizeBytes, DateTime CreatedUtc)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public sealed record LibraryFolderStatus(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("exists")] bool Exists)
{
    [JsonPropertyName("existsOnDisk")]
    public bool ExistsOnDisk => Exists;
}

public sealed record BackupInspectionReport(
    [property: JsonPropertyName("filePath")] string FilePath,
    [property: JsonPropertyName("fileDateUtc")] DateTime FileDateUtc,
    [property: JsonPropertyName("fileSizeBytes")] long FileSizeBytes,
    [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
    [property: JsonPropertyName("isSupported")] bool IsSupported,
    [property: JsonPropertyName("imageCount")] long ImageCount,
    [property: JsonPropertyName("folderCount")] long FolderCount,
    [property: JsonPropertyName("tagCount")] long TagCount,
    [property: JsonPropertyName("libraryFolders")] IReadOnlyList<LibraryFolderStatus> LibraryFolders,
    [property: JsonPropertyName("integrityCheck")] string IntegrityCheck = "ok");

public sealed record RestoreSummary(
    string BackupFilePath,
    int OriginalSchemaVersion,
    int FinalSchemaVersion,
    long ImageCount,
    long FolderCount,
    long TagCount,
    IReadOnlyList<string> Warnings);

public sealed record MoveResult(string NewDbPath, string? OldDbPath, bool OldDeleted);

public sealed record BackupFileInfo(string Name, string Path, long SizeBytes, DateTime CreatedUtc);

public sealed record DataInfo(
    string DbPath,
    long DbSizeBytes,
    long WalSizeBytes,
    int SchemaVersion,
    long ImageCount,
    long FolderCount,
    long TagCount,
    string BackupFolder,
    IReadOnlyList<BackupFileInfo> Backups);

public interface IDataMaintenanceService
{
    DataInfo GetInfo();
    IReadOnlyList<BackupFileInfo> GetBackups();
    Task<BackupResult> BackupAsync(string? targetPath = null, bool overwrite = false, CancellationToken cancellationToken = default);
    BackupInspectionReport InspectBackup(string backupPath);
    Task<RestoreSummary> RestoreAsync(string backupPath, bool confirm = false, CancellationToken cancellationToken = default);
    Task<MoveResult> MoveAsync(string targetFolder, bool deleteOld = false, CancellationToken cancellationToken = default);
}

public sealed class DataMaintenanceService : IDataMaintenanceService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ISettingsService _settingsService;
    private readonly IDbGate _dbGate;
    private readonly ILibraryFolderRepository _folderRepository;
    private readonly IScanJobManager? _scanJobManager;
    private readonly IThumbnailWorker? _thumbnailWorker;
    private readonly ILibraryEventEmitter? _eventEmitter;
    private readonly IClock _clock;
    private readonly ILogger<DataMaintenanceService> _logger;

    public DataMaintenanceService(
        IDatabaseConnectionFactory connectionFactory,
        ISettingsService settingsService,
        IDbGate dbGate,
        ILibraryFolderRepository folderRepository,
        IScanJobManager? scanJobManager = null,
        IThumbnailWorker? thumbnailWorker = null,
        ILibraryEventEmitter? eventEmitter = null,
        IClock? clock = null,
        ILogger<DataMaintenanceService>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _settingsService = settingsService;
        _dbGate = dbGate;
        _folderRepository = folderRepository;
        _scanJobManager = scanJobManager;
        _thumbnailWorker = thumbnailWorker;
        _eventEmitter = eventEmitter;
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<DataMaintenanceService>.Instance;
    }

    private string ResolveBackupFolder()
    {
        var configured = _settingsService.Current.BackupFolder;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return Path.GetFullPath(configured);
        }

        return _settingsService.Paths.DefaultBackupDirectory;
    }

    private void ValidateNotInsideLibrary(string path, string targetDescription)
    {
        var fullPath = Path.GetFullPath(path);
        var libraryFolders = _folderRepository.List();
        foreach (var folder in libraryFolders)
        {
            var fullLib = Path.GetFullPath(folder.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (fullPath.Equals(fullLib, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullLib + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullLib + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"The target {targetDescription} ('{path}') must not be inside any library folder ('{folder.Path}').");
            }
        }
    }

    private static SqliteConnection OpenDirectConnection(string dbPath)
    {
        var conn = new SqliteConnection($"Data Source={dbPath};");
        conn.Open();
        DatabaseConnectionFactory.RegisterFunctions(conn);
        return conn;
    }

    public DataInfo GetInfo()
    {
        var dbPath = _connectionFactory.DatabasePath;
        long dbSize = File.Exists(dbPath) ? new FileInfo(dbPath).Length : 0;
        long walSize = 0;
        var walPath = dbPath + "-wal";
        if (File.Exists(walPath))
        {
            walSize = new FileInfo(walPath).Length;
        }

        int schemaVersion = 0;
        long imageCount = 0;
        long folderCount = 0;
        long tagCount = 0;

        try
        {
            using var conn = _connectionFactory.CreateAndOpenConnection();
            schemaVersion = conn.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version;") ?? 0;
            imageCount = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM image;");
            folderCount = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM library_folder;");
            tagCount = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM tag;");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read database counts for DataInfo.");
        }

        var backupFolder = ResolveBackupFolder();
        var backups = GetBackups();

        return new DataInfo(
            DbPath: dbPath,
            DbSizeBytes: dbSize,
            WalSizeBytes: walSize,
            SchemaVersion: schemaVersion,
            ImageCount: imageCount,
            FolderCount: folderCount,
            TagCount: tagCount,
            BackupFolder: backupFolder,
            Backups: backups);
    }

    public IReadOnlyList<BackupFileInfo> GetBackups()
    {
        var folder = ResolveBackupFolder();
        if (!Directory.Exists(folder))
        {
            return Array.Empty<BackupFileInfo>();
        }

        var dirInfo = new DirectoryInfo(folder);
        var files = dirInfo.EnumerateFiles("*.*")
            .Where(f => f.Extension.Equals(".db", StringComparison.OrdinalIgnoreCase) ||
                        f.Extension.Equals(".sqlite", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupFileInfo(
                Name: f.Name,
                Path: f.FullName,
                SizeBytes: f.Length,
                CreatedUtc: f.LastWriteTimeUtc))
            .ToList();

        return files;
    }

    public Task<BackupResult> BackupAsync(string? targetPath = null, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var backupFolder = ResolveBackupFolder();
        Directory.CreateDirectory(backupFolder);

        string destination;
        if (string.IsNullOrWhiteSpace(targetPath))
        {
            var fileName = $"iconchest-backup-{_clock.UtcNow:yyyyMMdd-HHmmss}.db";
            destination = Path.Combine(backupFolder, fileName);
        }
        else
        {
            destination = Path.GetFullPath(targetPath);
            if (string.IsNullOrEmpty(Path.GetExtension(destination)))
            {
                destination += ".db";
            }
            var destDir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrWhiteSpace(destDir))
            {
                Directory.CreateDirectory(destDir);
            }
        }

        ValidateNotInsideLibrary(destination, "backup path");

        if (File.Exists(destination) && !overwrite)
        {
            throw new InvalidOperationException($"Backup destination file already exists: {destination}");
        }

        var tempPath = destination + ".tmp." + Guid.NewGuid().ToString("N");

        try
        {
            using (var readLease = _dbGate.EnterRead())
            using (var source = _connectionFactory.CreateConnection())
            {
                source.Open();
                using (var dest = new SqliteConnection($"Data Source={tempPath};Mode=ReadWriteCreate;"))
                {
                    dest.Open();
                    source.BackupDatabase(dest);
                }
            }

            File.Move(tempPath, destination, overwrite: true);
            var fileInfo = new FileInfo(destination);

            _logger.LogInformation("Database backup successfully created at {Path} ({Size} bytes).", destination, fileInfo.Length);
            return Task.FromResult(new BackupResult(destination, fileInfo.Length, fileInfo.LastWriteTimeUtc));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { }
            }
        }
    }

    public BackupInspectionReport InspectBackup(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        if (!File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Backup file does not exist: {backupPath}");
        }

        var fileInfo = new FileInfo(backupPath);
        if (fileInfo.Length == 0)
        {
            throw new InvalidOperationException("Backup file is empty (0 bytes).");
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadOnly
        };

        using var conn = new SqliteConnection(builder.ConnectionString);
        try
        {
            conn.Open();

            // 1. PRAGMA integrity_check
            var integrity = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Backup file failed SQLite integrity check: {integrity}");
            }

            // 2. Check required tables
            var tables = conn.Query<string>("SELECT name FROM sqlite_master WHERE type = 'table';")
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (!tables.Contains("image") || !tables.Contains("library_folder") || !tables.Contains("tag") || !tables.Contains("image_tag"))
            {
                throw new InvalidOperationException("Backup file is missing required IconChest tables (image, library_folder, tag, image_tag).");
            }

            // 3. Schema version
            int schemaVersion = 0;
            if (tables.Contains("schema_version"))
            {
                schemaVersion = conn.ExecuteScalar<int?>("SELECT MAX(version) FROM schema_version;") ?? 0;
            }

            var runner = new MigrationRunner(_connectionFactory, _clock);
            int supportedVersion = runner.SupportedVersion;
            bool isSupported = schemaVersion <= supportedVersion;

            if (!isSupported)
            {
                throw new InvalidOperationException($"Backup schema version {schemaVersion} is newer than supported application version {supportedVersion}.");
            }

            // 4. Counts
            long images = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM image;");
            long folders = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM library_folder;");
            long tags = conn.ExecuteScalar<long>("SELECT COUNT(*) FROM tag;");

            // 5. Library folders
            var folderPaths = conn.Query<string>("SELECT path FROM library_folder;").ToList();
            var folderStatuses = folderPaths.Select(p => new LibraryFolderStatus(p, CheckDirectoryExists(p))).ToList();

            return new BackupInspectionReport(
                FilePath: backupPath,
                FileDateUtc: fileInfo.LastWriteTimeUtc,
                FileSizeBytes: fileInfo.Length,
                SchemaVersion: schemaVersion,
                IsSupported: isSupported,
                ImageCount: images,
                FolderCount: folders,
                TagCount: tags,
                LibraryFolders: folderStatuses,
                IntegrityCheck: integrity ?? "ok");
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"File is not a valid SQLite database: {ex.Message}", ex);
        }
    }

    private static bool CheckDirectoryExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        try
        {
            var expanded = path.Trim();
            if (expanded.StartsWith("~/") || expanded.StartsWith("~\\") || expanded == "~")
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                expanded = Path.Combine(home, expanded.Length > 2 ? expanded[2..] : string.Empty);
            }
            else if (expanded.StartsWith('%') && expanded.EndsWith('%'))
            {
                expanded = Environment.ExpandEnvironmentVariables(expanded);
            }

            expanded = expanded.Trim();
            if (Directory.Exists(expanded)) return true;

            var trimmed = expanded.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(trimmed)) return true;

            var full = Path.GetFullPath(expanded);
            if (Directory.Exists(full)) return true;

            var fullTrimmed = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (Directory.Exists(fullTrimmed)) return true;

            // DirectoryInfo check (handles symlinks / special reparse points)
            if (new DirectoryInfo(expanded).Exists || new DirectoryInfo(fullTrimmed).Exists) return true;

            // Case-insensitive segment match fallback on Unix-like systems
            if (!OperatingSystem.IsWindows() && Path.IsPathRooted(fullTrimmed))
            {
                var segments = fullTrimmed.Split('/', StringSplitOptions.RemoveEmptyEntries);
                var current = "/";
                var allMatched = true;

                foreach (var seg in segments)
                {
                    if (!Directory.Exists(current))
                    {
                        allMatched = false;
                        break;
                    }

                    var match = Directory.EnumerateDirectories(current)
                        .FirstOrDefault(d => string.Equals(Path.GetFileName(d), seg, StringComparison.OrdinalIgnoreCase));

                    if (match == null)
                    {
                        allMatched = false;
                        break;
                    }

                    current = match;
                }

                if (allMatched && Directory.Exists(current)) return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RestoreSummary> RestoreAsync(string backupPath, bool confirm = false, CancellationToken cancellationToken = default)
    {
        if (!confirm)
        {
            throw new InvalidOperationException("Restore requires explicit confirmation.");
        }

        if (_scanJobManager != null && _scanJobManager.HasActiveScans)
        {
            throw new InvalidOperationException("Cannot restore database while a library scan is in progress.");
        }

        var report = InspectBackup(backupPath);
        var currentDbPath = Path.GetFullPath(_connectionFactory.DatabasePath);
        var targetBackupFullPath = Path.GetFullPath(backupPath);

        if (currentDbPath.Equals(targetBackupFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cannot restore database onto itself.");
        }

        // Enter exclusive maintenance lock
        using var lease = await _dbGate.EnterMaintenanceAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

        _thumbnailWorker?.Pause();

        // 1. WAL Checkpoint
        try
        {
            using var checkConn = new SqliteConnection($"Data Source={currentDbPath};");
            checkConn.Open();
            using var cmd = checkConn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to checkpoint WAL before database restore.");
        }

        // 2. Clear connection pools
        SqliteConnection.ClearAllPools();

        var timestamp = _clock.UtcNow.ToString("yyyyMMddHHmmss");
        var preRestorePath = $"{currentDbPath}.pre-restore-{timestamp}";

        bool swapBegan = false;
        try
        {
            // 3. Keep current database as .pre-restore-<timestamp>
            if (File.Exists(currentDbPath))
            {
                File.Copy(currentDbPath, preRestorePath, overwrite: false);
            }

            swapBegan = true;

            // 4. Overwrite current DB with backup
            File.Copy(backupPath, currentDbPath, overwrite: true);

            // Clean up old WAL/SHM
            var wal = currentDbPath + "-wal";
            if (File.Exists(wal)) File.Delete(wal);
            var shm = currentDbPath + "-shm";
            if (File.Exists(shm)) File.Delete(shm);

            // 5. Run migrations if backup had an older schema
            int finalVersion;
            using (var restoredConn = OpenDirectConnection(currentDbPath))
            {
                var directFactory = new DatabaseConnectionFactory(currentDbPath);
                var runner = new MigrationRunner(directFactory, _clock);
                finalVersion = runner.Migrate(restoredConn);
            }

            // 6. Read new counts
            long newImages;
            long newFolders;
            long newTags;
            using (var countConn = OpenDirectConnection(currentDbPath))
            {
                newImages = countConn.ExecuteScalar<long>("SELECT COUNT(*) FROM image;");
                newFolders = countConn.ExecuteScalar<long>("SELECT COUNT(*) FROM library_folder;");
                newTags = countConn.ExecuteScalar<long>("SELECT COUNT(*) FROM tag;");
            }

            var warnings = new List<string>();
            foreach (var lf in report.LibraryFolders)
            {
                if (!lf.Exists)
                {
                    warnings.Add($"Library folder does not exist on this machine: {lf.Path}");
                }
            }

            var summary = new RestoreSummary(
                BackupFilePath: backupPath,
                OriginalSchemaVersion: report.SchemaVersion,
                FinalSchemaVersion: finalVersion,
                ImageCount: newImages,
                FolderCount: newFolders,
                TagCount: newTags,
                Warnings: warnings);

            _logger.LogInformation("Database successfully restored from {Backup}. Schema version: {Orig} -> {Final}. Counts: {Images} images, {Folders} folders, {Tags} tags. Warnings: {WarningsCount}",
                backupPath, report.SchemaVersion, finalVersion, newImages, newFolders, newTags, warnings.Count);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Restore failed after swap began. Executing automatic rollback.");
            if (swapBegan && File.Exists(preRestorePath))
            {
                try
                {
                    SqliteConnection.ClearAllPools();
                    File.Copy(preRestorePath, currentDbPath, overwrite: true);
                    var wal = currentDbPath + "-wal";
                    if (File.Exists(wal)) File.Delete(wal);
                    var shm = currentDbPath + "-shm";
                    if (File.Exists(shm)) File.Delete(shm);
                    _logger.LogInformation("Automatic rollback succeeded. Restored previous database from {PreRestorePath}.", preRestorePath);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogCritical(rollbackEx, "CRITICAL: Automatic rollback failed after failed restore.");
                }
            }
            throw;
        }
        finally
        {
            _thumbnailWorker?.Resume();
            _eventEmitter?.EmitDataReloaded();
            _eventEmitter?.EmitLibraryChanged();
        }
    }

    public async Task<MoveResult> MoveAsync(string targetFolder, bool deleteOld = false, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFolder);

        if (_scanJobManager != null && _scanJobManager.HasActiveScans)
        {
            throw new InvalidOperationException("Cannot move database while a library scan is in progress.");
        }

        var fullTargetFolder = Path.GetFullPath(targetFolder);
        ValidateNotInsideLibrary(fullTargetFolder, "move destination folder");

        if (!Directory.Exists(fullTargetFolder))
        {
            Directory.CreateDirectory(fullTargetFolder);
        }

        // Verify folder is writable
        var probePath = Path.Combine(fullTargetFolder, ".probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllText(probePath, "probe");
            File.Delete(probePath);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Destination directory is not writable: {fullTargetFolder}. {ex.Message}", ex);
        }

        var currentDbPath = Path.GetFullPath(_connectionFactory.DatabasePath);
        var targetDbPath = Path.Combine(fullTargetFolder, Path.GetFileName(currentDbPath));

        if (File.Exists(targetDbPath))
        {
            throw new InvalidOperationException($"Target database file already exists: {targetDbPath}");
        }

        // Enter exclusive maintenance lock
        using var lease = await _dbGate.EnterMaintenanceAsync(TimeSpan.FromSeconds(30), cancellationToken).ConfigureAwait(false);

        _thumbnailWorker?.Pause();

        // 1. WAL Checkpoint
        try
        {
            using var checkConn = new SqliteConnection($"Data Source={currentDbPath};");
            checkConn.Open();
            using var cmd = checkConn.CreateCommand();
            cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            cmd.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to checkpoint WAL before database move.");
        }

        // 2. Clear pools
        SqliteConnection.ClearAllPools();

        // 3. Read source record counts for verification
        long srcImages;
        long srcFolders;
        long srcTags;
        using (var srcConn = new SqliteConnection($"Data Source={currentDbPath};Mode=ReadOnly;"))
        {
            srcConn.Open();
            srcImages = srcConn.ExecuteScalar<long>("SELECT COUNT(*) FROM image;");
            srcFolders = srcConn.ExecuteScalar<long>("SELECT COUNT(*) FROM library_folder;");
            srcTags = srcConn.ExecuteScalar<long>("SELECT COUNT(*) FROM tag;");
        }

        // 4. Copy to target
        File.Copy(currentDbPath, targetDbPath, overwrite: false);

        // 5. Verify target
        try
        {
            using var targetConn = new SqliteConnection($"Data Source={targetDbPath};Mode=ReadOnly;");
            targetConn.Open();

            var integrity = targetConn.ExecuteScalar<string>("PRAGMA integrity_check;");
            if (!string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Moved database failed integrity check: {integrity}");
            }

            long targetImages = targetConn.ExecuteScalar<long>("SELECT COUNT(*) FROM image;");
            long targetFolders = targetConn.ExecuteScalar<long>("SELECT COUNT(*) FROM library_folder;");
            long targetTags = targetConn.ExecuteScalar<long>("SELECT COUNT(*) FROM tag;");

            if (targetImages != srcImages || targetFolders != srcFolders || targetTags != srcTags)
            {
                throw new InvalidOperationException($"Record count mismatch after move: source=({srcImages},{srcFolders},{srcTags}), target=({targetImages},{targetFolders},{targetTags}).");
            }
        }
        catch (Exception)
        {
            // Verification failed -> rollback by deleting target
            if (File.Exists(targetDbPath))
            {
                try { File.Delete(targetDbPath); } catch { }
            }
            _thumbnailWorker?.Resume();
            throw;
        }

        // 6. Update settings.json
        _settingsService.Update(s => s.DbPath = targetDbPath);

        // 7. Handle old database file
        string? oldPathResult = null;
        if (deleteOld)
        {
            try
            {
                File.Delete(currentDbPath);
                var wal = currentDbPath + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
                var shm = currentDbPath + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old database file after move: {Path}", currentDbPath);
            }
        }
        else
        {
            var oldTimestamped = $"{currentDbPath}.old-{_clock.UtcNow:yyyyMMddHHmmss}";
            try
            {
                File.Move(currentDbPath, oldTimestamped, overwrite: true);
                var wal = currentDbPath + "-wal";
                if (File.Exists(wal)) File.Delete(wal);
                var shm = currentDbPath + "-shm";
                if (File.Exists(shm)) File.Delete(shm);
                oldPathResult = oldTimestamped;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to rename old database file to {Path}", oldTimestamped);
                oldPathResult = currentDbPath;
            }
        }

        _thumbnailWorker?.Resume();
        _eventEmitter?.EmitDataReloaded();
        _eventEmitter?.EmitLibraryChanged();

        _logger.LogInformation("Database successfully moved from {OldPath} to {NewPath}.", currentDbPath, targetDbPath);
        return new MoveResult(targetDbPath, oldPathResult, deleteOld);
    }
}
