using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Duplicates;
using IconChest.Core.Missing;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using Xunit;

namespace IconChest.Core.Tests;

public class DuplicateAndMissingServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseConnectionFactory _connFactory;
    private readonly ImageRepository _imageRepo;
    private readonly LibraryFolderRepository _folderRepo;
    private readonly DuplicateService _duplicateService;
    private readonly MissingService _missingService;
    private readonly long _folderId;
    private readonly string _libDir;

    public DuplicateAndMissingServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"dup-missing-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var paths = new AppPaths(configDirOverride: _tempDir, dataDirOverride: _tempDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        var db = new IconChestDatabase(settings);
        db.Migrate();

        _connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        _imageRepo = new ImageRepository(_connFactory);
        _folderRepo = new LibraryFolderRepository(_connFactory);
        _duplicateService = new DuplicateService(_connFactory);
        _missingService = new MissingService(_connFactory);

        _libDir = Path.Combine(_tempDir, "Library");
        Directory.CreateDirectory(_libDir);
        _folderId = _folderRepo.Add(_libDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void DuplicateService_ReturnsGroupedDuplicates()
    {
        // 2 images with same hash
        var hash = "duphash1234567890123456789012345";
        var r1 = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "img1.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/img1.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "img1.png",
            Extension = "png",
            Format = "png",
            Hash = hash,
            SizeBytes = 500,
            Width = 10,
            Height = 10,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        };

        var r2 = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "img2.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/img2.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "img2.png",
            Extension = "png",
            Format = "png",
            Hash = hash,
            SizeBytes = 500,
            Width = 10,
            Height = 10,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        };

        _imageRepo.InsertBatch([r1, r2]);
        _imageRepo.RecomputeAllDuplicates();

        var result = _duplicateService.GetDuplicates(offset: 0, limit: 10);
        Assert.Equal(1, result.TotalGroups);
        Assert.Single(result.Groups);

        var group = result.Groups[0];
        Assert.Equal(hash, group.Hash);
        Assert.Equal(500, group.SizeBytes);
        Assert.Equal(2, group.Items.Count);
    }

    [Fact]
    public void MissingService_ListAndRemove()
    {
        var rec = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "missing.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/missing.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "missing.png",
            Extension = "png",
            Format = "png",
            Hash = "hashmissing12345",
            SizeBytes = 200,
            Width = 10,
            Height = 10,
            IsMissing = true,
            MissingSinceUtc = DateTime.UtcNow.ToString("O"),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        };

        var id = _imageRepo.InsertBatch([rec])[0];

        var missingList = _missingService.GetMissing(offset: 0, limit: 10);
        Assert.Equal(1, missingList.Total);
        Assert.Single(missingList.Items);
        Assert.Equal(id, missingList.Items[0].Id);

        // Remove missing image record
        var removed = _missingService.RemoveMissing(_folderId);
        Assert.Equal(1, removed);

        Assert.Null(_imageRepo.GetById(id));
    }

    [Fact]
    public void MissingService_FolderIdsWithMissing()
    {
        var rec = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "missing2.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/missing2.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "missing2.png",
            Extension = "png",
            Format = "png",
            Hash = "hashmissing123456",
            SizeBytes = 200,
            Width = 10,
            Height = 10,
            IsMissing = true,
            MissingSinceUtc = DateTime.UtcNow.ToString("O"),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        };

        _imageRepo.InsertBatch([rec]);

        var folderIds = _missingService.GetFolderIdsWithMissing();
        Assert.Contains(_folderId, folderIds);
    }
}
