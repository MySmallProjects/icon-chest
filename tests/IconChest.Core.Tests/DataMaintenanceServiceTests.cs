using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Events;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IconChest.Core.Tests;

public sealed class DataMaintenanceServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _dataDir;
    private readonly string _configDir;
    private readonly string _dbPath;
    private readonly string _libraryDir;
    private readonly AppPaths _paths;
    private readonly SettingsService _settingsService;
    private readonly DbGate _dbGate;
    private readonly DatabaseConnectionFactory _connectionFactory;
    private readonly LibraryFolderRepository _folderRepo;
    private readonly ImageRepository _imageRepo;
    private readonly LibraryEventEmitter _eventEmitter;
    private readonly IconChestDatabase _database;

    public DataMaintenanceServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "iconchest-maint-test-" + Guid.NewGuid().ToString("N"));
        _dataDir = Path.Combine(_testDir, "data");
        _configDir = Path.Combine(_testDir, "config");
        _libraryDir = Path.Combine(_testDir, "library");

        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_configDir);
        Directory.CreateDirectory(_libraryDir);

        _paths = new AppPaths(_configDir, _dataDir, isWindowsOverride: false);
        _settingsService = new SettingsService(_paths);
        _dbPath = _paths.DefaultDatabasePath;

        _dbGate = new DbGate();
        _database = new IconChestDatabase(_dbPath, SystemClock.Instance, null, _dbGate);
        _database.Migrate();

        _connectionFactory = new DatabaseConnectionFactory(_dbPath, _dbGate);
        _folderRepo = new LibraryFolderRepository(_connectionFactory);
        _imageRepo = new ImageRepository(_connectionFactory);
        _eventEmitter = new LibraryEventEmitter();

        // Seed a library folder and image
        _folderRepo.Add(_libraryDir);
        _imageRepo.InsertBatch([new ImageRecord
        {
            FolderId = 1,
            Path = Path.Combine(_libraryDir, "icon.png"),
            PathKey = Path.Combine(_libraryDir, "icon.png"),
            FileName = "icon.png",
            Extension = "png",
            Format = "png",
            Width = 32,
            Height = 32,
            SizeBytes = 1024,
            Hash = "dummyhash123",
            HashAlgo = "xxh128",
            Title = "icon",
            AddedUtc = DateTime.UtcNow.ToString("O"),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        }]);

        using var conn = _connectionFactory.CreateAndOpenConnection();
        conn.Execute(@"
            INSERT INTO tag (name, fg_color, bg_color) VALUES ('TestTag', '#FFFFFF', '#000000');
            INSERT INTO image_tag (image_id, tag_id) VALUES (1, 1);");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_testDir, true); } catch { }
    }

    private DataMaintenanceService CreateService(IScanJobManager? scanManager = null, IThumbnailWorker? worker = null)
    {
        return new DataMaintenanceService(
            _connectionFactory,
            _settingsService,
            _dbGate,
            _folderRepo,
            scanManager,
            worker,
            _eventEmitter,
            SystemClock.Instance);
    }

    [Fact]
    public async Task Backup_CreatesValidBackup_AndRejectsInsideLibrary()
    {
        var service = CreateService();

        // 1. Default backup in backup folder
        var res = await service.BackupAsync();
        Assert.True(File.Exists(res.Path));
        Assert.True(res.SizeBytes > 0);

        // 2. Reject backup inside library folder
        var insideLibPath = Path.Combine(_libraryDir, "backup.db");
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.BackupAsync(insideLibPath);
        });

        // 3. Overwrite protection
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.BackupAsync(res.Path, overwrite: false);
        });
    }

    [Fact]
    public async Task Inspect_ValidBackup_ReportsAccurateDetails()
    {
        var service = CreateService();
        var backup = await service.BackupAsync();

        var report = service.InspectBackup(backup.Path);

        Assert.Equal(backup.Path, report.FilePath);
        Assert.True(report.IsSupported);
        Assert.Equal(1, report.ImageCount);
        Assert.Equal(1, report.FolderCount);
        Assert.Equal(1, report.TagCount);
        Assert.Single(report.LibraryFolders);
        Assert.True(report.LibraryFolders[0].Exists);
    }

    [Fact]
    public void Inspect_NonSqliteOrCorruptFile_Throws()
    {
        var service = CreateService();
        var badFile = Path.Combine(_testDir, "corrupt.db");
        File.WriteAllText(badFile, "This is not a sqlite database file at all!");

        Assert.Throws<InvalidOperationException>(() => service.InspectBackup(badFile));

        var emptyFile = Path.Combine(_testDir, "empty.db");
        File.WriteAllBytes(emptyFile, Array.Empty<byte>());

        Assert.Throws<InvalidOperationException>(() => service.InspectBackup(emptyFile));
    }

    [Fact]
    public async Task Restore_ReplacesDatabase_AndCreatesPreRestoreFile()
    {
        var service = CreateService();
        var backup = await service.BackupAsync();

        // Mutate live DB (insert another image)
        _imageRepo.InsertBatch([new ImageRecord
        {
            FolderId = 1,
            Path = Path.Combine(_libraryDir, "icon2.png"),
            PathKey = Path.Combine(_libraryDir, "icon2.png"),
            FileName = "icon2.png",
            Extension = "png",
            Format = "png",
            Width = 64,
            Height = 64,
            SizeBytes = 2048,
            Hash = "hash2",
            HashAlgo = "xxh128",
            Title = "icon2",
            AddedUtc = DateTime.UtcNow.ToString("O"),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        }]);

        using (var conn = _connectionFactory.CreateAndOpenConnection())
        {
            Assert.Equal(2, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image;"));
        }

        // Restore backup
        var summary = await service.RestoreAsync(backup.Path, confirm: true);

        Assert.Equal(1, summary.ImageCount);
        Assert.Equal(1, summary.TagCount);

        // Pre-restore file should exist
        var preRestoreFiles = Directory.GetFiles(_dataDir, "*.pre-restore-*");
        Assert.NotEmpty(preRestoreFiles);

        // Current DB has 1 image again
        using (var conn = _connectionFactory.CreateAndOpenConnection())
        {
            Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image;"));
        }
    }

    [Fact]
    public async Task Restore_RefusesWithoutConfirm_OrWhenScanRunning()
    {
        var service = CreateService();
        var backup = await service.BackupAsync();

        // 1. Without confirm
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.RestoreAsync(backup.Path, confirm: false);
        });

        // 2. Scan running
        var scanStub = new FakeScanJobManager(hasActiveScans: true);
        var scanningService = CreateService(scanManager: scanStub);

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await scanningService.RestoreAsync(backup.Path, confirm: true);
        });
    }

    [Fact]
    public async Task Move_RelocatesDatabase_UpdatesSettings_AndRenamesOld()
    {
        var service = CreateService();
        var newFolder = Path.Combine(_testDir, "new-db-dir");

        var moveResult = await service.MoveAsync(newFolder, deleteOld: false);

        Assert.True(File.Exists(moveResult.NewDbPath));
        Assert.NotNull(moveResult.OldDbPath);
        Assert.True(File.Exists(moveResult.OldDbPath));
        Assert.Equal(moveResult.NewDbPath, _settingsService.Current.DbPath);

        // Old file was renamed to .old-timestamp
        Assert.Contains(".old-", moveResult.OldDbPath);

        // Verify data intact in new location
        var newFactory = new DatabaseConnectionFactory(moveResult.NewDbPath);
        using var conn = newFactory.CreateAndOpenConnection();
        Assert.Equal(1, conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image;"));
    }

    [Fact]
    public async Task Move_RejectsTargetInsideLibrary_OrExistingTarget()
    {
        var service = CreateService();

        // Target inside library folder
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.MoveAsync(_libraryDir);
        });

        // Target already has file
        var existingDir = Path.Combine(_testDir, "existing-dir");
        Directory.CreateDirectory(existingDir);
        var existingFile = Path.Combine(existingDir, Path.GetFileName(_dbPath));
        File.WriteAllText(existingFile, "already here");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await service.MoveAsync(existingDir);
        });
    }

    private sealed class FakeScanJobManager : IScanJobManager
    {
        public bool HasActiveScans { get; }
        public FakeScanJobManager(bool hasActiveScans) => HasActiveScans = hasActiveScans;
        public ScanJobInfo StartScan(long folderId) => throw new NotImplementedException();
        public IReadOnlyList<ScanJobInfo> GetRecentJobs(int limit = 20) => Array.Empty<ScanJobInfo>();
        public ScanJobInfo? GetJob(string jobId) => null;
        public bool CancelJob(string jobId) => false;
    }
}

