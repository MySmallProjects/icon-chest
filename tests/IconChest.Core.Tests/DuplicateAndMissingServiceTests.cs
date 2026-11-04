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
        var removed = _missingService.RemoveMissing(folderId: _folderId);
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

    [Fact]
    public void DuplicateService_GetSummaryAndSorting()
    {
        // Group A: 3 copies of 100 bytes = 200 reclaimable bytes
        var hashA = "hash_group_a_12345";
        for (int i = 0; i < 3; i++)
        {
            _imageRepo.InsertBatch([new ImageRecord
            {
                FolderId = _folderId,
                Path = Path.Combine(_libDir, $"a_{i}.png"),
                PathKey = $"a_{i}",
                DirectoryPath = _libDir,
                FileName = $"a_{i}.png",
                Extension = "png",
                Format = "png",
                Hash = hashA,
                SizeBytes = 100,
                Width = 10,
                Height = 10,
                IsDuplicate = true,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
                ModifiedUtc = DateTime.UtcNow.ToString("O"),
                AddedUtc = DateTime.UtcNow.ToString("O"),
                EffectiveDateUtc = DateTime.UtcNow.ToString("O")
            }]);
        }

        // Group B: 2 copies of 500 bytes = 500 reclaimable bytes
        var hashB = "hash_group_b_67890";
        for (int i = 0; i < 2; i++)
        {
            _imageRepo.InsertBatch([new ImageRecord
            {
                FolderId = _folderId,
                Path = Path.Combine(_libDir, $"b_{i}.png"),
                PathKey = $"b_{i}",
                DirectoryPath = _libDir,
                FileName = $"b_{i}.png",
                Extension = "png",
                Format = "png",
                Hash = hashB,
                SizeBytes = 500,
                Width = 10,
                Height = 10,
                IsDuplicate = true,
                CreatedUtc = DateTime.UtcNow.ToString("O"),
                ModifiedUtc = DateTime.UtcNow.ToString("O"),
                AddedUtc = DateTime.UtcNow.ToString("O"),
                EffectiveDateUtc = DateTime.UtcNow.ToString("O")
            }]);
        }

        // Summary test
        var summary = _duplicateService.GetSummary(sort: "reclaimable", dir: "desc");
        Assert.Equal(2, summary.GroupCount);
        Assert.Equal(5, summary.ImageCount);
        Assert.Equal(700, summary.ReclaimableBytes); // 200 + 500
        Assert.Equal(2, summary.GroupSizes.Count);
        // By reclaimable desc: Group B (500 reclaimable, 2 copies) should be first, Group A (200 reclaimable, 3 copies) second
        Assert.Equal(2, summary.GroupSizes[0]);
        Assert.Equal(3, summary.GroupSizes[1]);

        // Test display IDs
        var ids = _duplicateService.GetDuplicateImageIds(sort: "reclaimable", dir: "desc");
        Assert.Equal(5, ids.Count);

        // Test sort by copies desc: Group A (3 copies) first, Group B (2 copies) second
        var copiesSummary = _duplicateService.GetSummary(sort: "copies", dir: "desc");
        Assert.Equal(3, copiesSummary.GroupSizes[0]);
        Assert.Equal(2, copiesSummary.GroupSizes[1]);
    }

    [Fact]
    public void MissingService_SortingAndRemoveByIds()
    {
        var id1 = _imageRepo.InsertBatch([new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "zebra.png"),
            PathKey = "zebra",
            DirectoryPath = _libDir,
            FileName = "zebra.png",
            Extension = "png",
            Format = "png",
            Hash = "hash1",
            SizeBytes = 1000,
            Width = 10,
            Height = 10,
            IsMissing = true,
            MissingSinceUtc = "2026-01-01T00:00:00Z",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        }])[0];

        var id2 = _imageRepo.InsertBatch([new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "apple.png"),
            PathKey = "apple",
            DirectoryPath = _libDir,
            FileName = "apple.png",
            Extension = "png",
            Format = "png",
            Hash = "hash2",
            SizeBytes = 200,
            Width = 10,
            Height = 10,
            IsMissing = true,
            MissingSinceUtc = "2026-02-01T00:00:00Z",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        }])[0];

        // Sort by name ASC: apple then zebra
        var resName = _missingService.GetMissing(sort: "name", dir: "asc");
        Assert.Equal(2, resName.Total);
        Assert.Equal("apple.png", resName.Items[0].FileName);
        Assert.Equal("zebra.png", resName.Items[1].FileName);

        // Sort by size DESC: zebra (1000) then apple (200)
        var resSize = _missingService.GetMissing(sort: "size", dir: "desc");
        Assert.Equal(1000, resSize.Items[0].SizeBytes);
        Assert.Equal(200, resSize.Items[1].SizeBytes);

        // Remove by specific ID
        var removed = _missingService.RemoveMissing(ids: [id1]);
        Assert.Equal(1, removed);

        var remaining = _missingService.GetMissing();
        Assert.Equal(1, remaining.Total);
        Assert.Equal(id2, remaining.Items[0].Id);
    }
}
