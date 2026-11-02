using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Trees;
using Xunit;

namespace IconChest.Core.Tests;

public class TreeServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseConnectionFactory _connFactory;
    private readonly TreeService _treeService;
    private readonly ImageRepository _imageRepo;
    private readonly LibraryFolderRepository _folderRepo;
    private readonly long _folderId;
    private readonly string _libDir;

    public TreeServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tree-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var paths = new AppPaths(configDirOverride: _tempDir, dataDirOverride: _tempDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        var db = new IconChestDatabase(settings);
        db.Migrate();

        _connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        _treeService = new TreeService(_connFactory);
        _imageRepo = new ImageRepository(_connFactory);
        _folderRepo = new LibraryFolderRepository(_connFactory);

        _libDir = Path.Combine(_tempDir, "Library");
        Directory.CreateDirectory(_libDir);
        _folderId = _folderRepo.Add(_libDir);

        SeedData();
    }

    private void SeedData()
    {
        var rootPath = Path.Combine(_libDir, "root.png");
        var aPath = Path.Combine(_libDir, "icons", "a.png");
        var bPath = Path.Combine(_libDir, "icons", "sub", "b.png");
        var unkPath = Path.Combine(_libDir, "icons", "unknown_date.png");

        var a1 = PathKeyProvider.Default.Analyze(rootPath);
        var a2 = PathKeyProvider.Default.Analyze(aPath);
        var a3 = PathKeyProvider.Default.Analyze(bPath);
        var a4 = PathKeyProvider.Default.Analyze(unkPath);

        var records = new List<ImageRecord>
        {
            // Root image (dir = "") with date 2024-05-10
            new ImageRecord
            {
                FolderId = _folderId,
                Path = rootPath,
                PathKey = a1.PathKey,
                DirectoryPath = a1.DirectoryPath,
                DirectoryKey = a1.DirectoryKey,
                FileName = a1.FileName,
                Extension = a1.Extension,
                Format = "png",
                Hash = "hash1",
                SizeBytes = 100,
                Width = 10,
                Height = 10,
                CreatedUtc = "2024-05-10T00:00:00Z",
                ModifiedUtc = "2024-05-10T00:00:00Z",
                AddedUtc = "2024-05-10T00:00:00Z"
            },
            // Child image (dir = "icons") with date 2024-05-20
            new ImageRecord
            {
                FolderId = _folderId,
                Path = aPath,
                PathKey = a2.PathKey,
                DirectoryPath = a2.DirectoryPath,
                DirectoryKey = a2.DirectoryKey,
                FileName = a2.FileName,
                Extension = a2.Extension,
                Format = "png",
                Hash = "hash2",
                SizeBytes = 100,
                Width = 10,
                Height = 10,
                CreatedUtc = "2024-05-20T00:00:00Z",
                ModifiedUtc = "2024-05-20T00:00:00Z",
                AddedUtc = "2024-05-20T00:00:00Z"
            },
            // Grandchild image (dir = "icons/sub") with date 2023-11-01
            new ImageRecord
            {
                FolderId = _folderId,
                Path = bPath,
                PathKey = a3.PathKey,
                DirectoryPath = a3.DirectoryPath,
                DirectoryKey = a3.DirectoryKey,
                FileName = a3.FileName,
                Extension = a3.Extension,
                Format = "png",
                Hash = "hash3",
                SizeBytes = 100,
                Width = 10,
                Height = 10,
                CreatedUtc = "2023-11-01T00:00:00Z",
                ModifiedUtc = "2023-11-01T00:00:00Z",
                AddedUtc = "2023-11-01T00:00:00Z"
            },
            // Image with null dates
            new ImageRecord
            {
                FolderId = _folderId,
                Path = unkPath,
                PathKey = a4.PathKey,
                DirectoryPath = a4.DirectoryPath,
                DirectoryKey = a4.DirectoryKey,
                FileName = a4.FileName,
                Extension = a4.Extension,
                Format = "png",
                Hash = "hash4",
                SizeBytes = 100,
                Width = 10,
                Height = 10,
                CreatedUtc = null,
                ModifiedUtc = null,
                ExifDateUtc = null,
                TakenOverrideUtc = null,
                AddedUtc = "2024-01-01T00:00:00Z"
            }
        };

        _imageRepo.InsertBatch(records);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetFolderTree_ReturnsCorrectHierarchyAndCounts()
    {
        var roots = _treeService.GetFolderTree();
        Assert.Single(roots);

        var libNode = roots[0];
        Assert.Equal(_libDir, libNode.Path);
        // Direct count at root is 1 ("root.png"), total recursive count is 4
        Assert.Equal(1, libNode.Count);
        Assert.Equal(4, libNode.TotalCount);

        Assert.Single(libNode.Children);
        var iconsNode = libNode.Children[0];
        Assert.Equal("icons", iconsNode.Name);
        Assert.Equal(2, iconsNode.Count); // a.png, unknown_date.png
        Assert.Equal(3, iconsNode.TotalCount);  // a.png, unknown_date.png + b.png in sub

        Assert.Single(iconsNode.Children);
        var subNode = iconsNode.Children[0];
        Assert.Equal("sub", subNode.Name);
        Assert.Equal(1, subNode.Count);
        Assert.Equal(1, subNode.TotalCount);
    }

    [Fact]
    public void GetYearTree_ReturnsYearsMonthsAndUnknownBucket()
    {
        var tree = _treeService.GetYearTree("effective");

        // Years present: 2024 (2 images: root.png, a.png), 2023 (1 image: b.png)
        var y2024 = tree.Years.FirstOrDefault(y => y.Year == 2024);
        Assert.NotNull(y2024);
        Assert.Equal(2, y2024.Count);
        var m5 = y2024.Months.FirstOrDefault(m => m.Month == 5);
        Assert.NotNull(m5);
        Assert.Equal(2, m5.Count);

        var y2023 = tree.Years.FirstOrDefault(y => y.Year == 2023);
        Assert.NotNull(y2023);
        Assert.Equal(1, y2023.Count);

        // Unknown bucket has 1 image (unknown_date.png)
        Assert.Equal(1, tree.UnknownCount);
    }
}
