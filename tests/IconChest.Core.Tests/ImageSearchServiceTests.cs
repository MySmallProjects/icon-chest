using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Images;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Tags;
using Xunit;

namespace IconChest.Core.Tests;

public class ImageSearchServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseConnectionFactory _connFactory;
    private readonly ImageSearchService _searchService;
    private readonly ImageRepository _imageRepo;
    private readonly LibraryFolderRepository _folderRepo;
    private readonly TagService _tagService;
    private readonly long _folder1Id;
    private readonly long _folder2Id;
    private readonly string _f1;
    private readonly string _f2;

    public ImageSearchServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"search-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var paths = new AppPaths(configDirOverride: _tempDir, dataDirOverride: _tempDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        var db = new IconChestDatabase(settings);
        db.Migrate();

        _connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        _searchService = new ImageSearchService(_connFactory);
        _imageRepo = new ImageRepository(_connFactory);
        _folderRepo = new LibraryFolderRepository(_connFactory);
        _tagService = new TagService(_connFactory);

        _f1 = Path.Combine(_tempDir, "f1");
        _f2 = Path.Combine(_tempDir, "f2");
        Directory.CreateDirectory(_f1);
        Directory.CreateDirectory(_f2);
        _folder1Id = _folderRepo.Add(_f1);
        _folder2Id = _folderRepo.Add(_f2);

        SeedData(_f1, _f2);
    }

    private void SeedData(string f1, string f2)
    {
        var tagWeb = _tagService.Create("Web", bgColor: "#111111");
        var tagIcon = _tagService.Create("Icon", bgColor: "#222222");
        var tagPhoto = _tagService.Create("Photo", bgColor: "#333333");

        var p1 = Path.Combine(f1, "icons", "app.png");
        var p2 = Path.Combine(f1, "icons", "sub", "button.svg");
        var p3 = Path.Combine(f2, "sunset.jpg");
        var p4 = Path.Combine(f2, "copy_app.png");

        var a1 = PathKeyProvider.Default.Analyze(p1);
        var a2 = PathKeyProvider.Default.Analyze(p2);
        var a3 = PathKeyProvider.Default.Analyze(p3);
        var a4 = PathKeyProvider.Default.Analyze(p4);

        var rec1 = new ImageRecord
        {
            FolderId = _folder1Id,
            Path = p1,
            PathKey = a1.PathKey,
            DirectoryPath = a1.DirectoryPath,
            DirectoryKey = a1.DirectoryKey,
            FileName = a1.FileName,
            Extension = a1.Extension,
            Format = "png",
            Hash = "hash0000000000000000000000000001",
            SizeBytes = 1024,
            Width = 64,
            Height = 64,
            Rating = 5,
            IsFavourite = true,
            Title = "Application Icon",
            Description = "Home screen launcher",
            EffectiveDateUtc = "2024-01-15T10:00:00Z",
            CreatedUtc = "2024-01-15T10:00:00Z",
            ModifiedUtc = "2024-01-15T10:00:00Z",
            AddedUtc = "2024-01-15T10:00:00Z"
        };

        var rec2 = new ImageRecord
        {
            FolderId = _folder1Id,
            Path = p2,
            PathKey = a2.PathKey,
            DirectoryPath = a2.DirectoryPath,
            DirectoryKey = a2.DirectoryKey,
            FileName = a2.FileName,
            Extension = a2.Extension,
            Format = "svg",
            Hash = "hash0000000000000000000000000002",
            SizeBytes = 2048,
            Width = 128,
            Height = 128,
            Rating = 3,
            IsFavourite = false,
            Title = "Submit Button",
            Description = "",
            EffectiveDateUtc = "2024-02-20T10:00:00Z",
            CreatedUtc = "2024-02-20T10:00:00Z",
            ModifiedUtc = "2024-02-20T10:00:00Z",
            AddedUtc = "2024-02-20T10:00:00Z"
        };

        var rec3 = new ImageRecord
        {
            FolderId = _folder2Id,
            Path = p3,
            PathKey = a3.PathKey,
            DirectoryPath = a3.DirectoryPath,
            DirectoryKey = a3.DirectoryKey,
            FileName = a3.FileName,
            Extension = a3.Extension,
            Format = "jpeg",
            Hash = "hash0000000000000000000000000003",
            SizeBytes = 50000,
            Width = 800,
            Height = 600,
            Rating = 4,
            IsFavourite = true,
            IsHidden = true,
            Title = "Beach Sunset",
            EffectiveDateUtc = "2023-07-10T18:00:00Z",
            CreatedUtc = "2023-07-10T18:00:00Z",
            ModifiedUtc = "2023-07-10T18:00:00Z",
            AddedUtc = "2023-07-10T18:00:00Z"
        };

        var rec4 = new ImageRecord
        {
            FolderId = _folder2Id,
            Path = p4,
            PathKey = a4.PathKey,
            DirectoryPath = a4.DirectoryPath,
            DirectoryKey = a4.DirectoryKey,
            FileName = a4.FileName,
            Extension = a4.Extension,
            Format = "png",
            Hash = "hash0000000000000000000000000001", // duplicate hash
            SizeBytes = 1024,
            Width = 64,
            Height = 64,
            Rating = 0,
            EffectiveDateUtc = "2024-03-01T10:00:00Z",
            CreatedUtc = "2024-03-01T10:00:00Z",
            ModifiedUtc = "2024-03-01T10:00:00Z",
            AddedUtc = "2024-03-01T10:00:00Z"
        };

        var ids = _imageRepo.InsertBatch([rec1, rec2, rec3, rec4]);
        var id1 = ids[0];
        var id2 = ids[1];
        var id3 = ids[2];

        using (var conn = _connFactory.CreateAndOpenConnection())
        {
            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, @tid)", new { id = id1, tid = tagWeb.Id });
            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, @tid)", new { id = id1, tid = tagIcon.Id });
            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, @tid)", new { id = id2, tid = tagWeb.Id });
            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, @tid)", new { id = id3, tid = tagPhoto.Id });
        }

        _imageRepo.RecomputeAllDuplicates();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Search_DefaultParameters_ExcludesHiddenAndMissing()
    {
        var result = _searchService.Search(new ImageSearchRequest());
        // Hidden (sunset.jpg) is excluded by default, so 3 non-hidden remain
        Assert.Equal(3, result.Total);
    }

    [Fact]
    public void Search_ByFolderAndSubfolders()
    {
        var iconsDir = Path.Combine(_f1, "icons");

        // Exact folder without subfolders
        var r1 = _searchService.Search(new ImageSearchRequest
        {
            FolderId = _folder1Id,
            Directory = iconsDir,
            IncludeSubfolders = false
        });
        Assert.Single(r1.Items);
        Assert.Equal("app.png", r1.Items[0].FileName);

        // Include subfolders
        var r2 = _searchService.Search(new ImageSearchRequest
        {
            FolderId = _folder1Id,
            Directory = iconsDir,
            IncludeSubfolders = true
        });
        Assert.Equal(2, r2.Items.Count);
    }

    [Fact]
    public void Search_ByYearAndMonth()
    {
        var res2024 = _searchService.Search(new ImageSearchRequest { Year = 2024 });
        Assert.Equal(3, res2024.Total);

        var resJan2024 = _searchService.Search(new ImageSearchRequest { Year = 2024, Month = 1 });
        Assert.Single(resJan2024.Items);
        Assert.Equal("app.png", resJan2024.Items[0].FileName);
    }

    [Fact]
    public void Search_ByTagModeAnyAndAll()
    {
        // Any of [Web, Icon] -> should match id1 and id2
        var resAny = _searchService.Search(new ImageSearchRequest
        {
            Tags = ["Web", "Icon"],
            TagMode = TagFilterMode.Any
        });
        Assert.Equal(2, resAny.Total);

        // All of [Web, Icon] -> should only match id1
        var resAll = _searchService.Search(new ImageSearchRequest
        {
            Tags = ["Web", "Icon"],
            TagMode = TagFilterMode.All
        });
        Assert.Single(resAll.Items);
        Assert.Equal("app.png", resAll.Items[0].FileName);
    }

    [Fact]
    public void Search_ByTypes()
    {
        var resSvg = _searchService.Search(new ImageSearchRequest { Types = ["svg"] });
        Assert.Single(resSvg.Items);
        Assert.Equal("button.svg", resSvg.Items[0].FileName);
    }

    [Fact]
    public void Search_ByDimensionsAndRating()
    {
        var res = _searchService.Search(new ImageSearchRequest
        {
            MinWidth = 100,
            RatingMin = 3
        });
        Assert.Single(res.Items);
        Assert.Equal("button.svg", res.Items[0].FileName);
    }

    [Fact]
    public void Search_ByFavourite()
    {
        var res = _searchService.Search(new ImageSearchRequest { Favourite = true });
        Assert.Single(res.Items); // id1 is favourite and not hidden
        Assert.Equal("app.png", res.Items[0].FileName);
    }

    [Fact]
    public void Search_ByText()
    {
        var resTitle = _searchService.Search(new ImageSearchRequest { Text = "Submit" });
        Assert.Single(resTitle.Items);
        Assert.Equal("button.svg", resTitle.Items[0].FileName);

        var resNotes = _searchService.Search(new ImageSearchRequest { Text = "launcher" });
        Assert.Single(resNotes.Items);
        Assert.Equal("app.png", resNotes.Items[0].FileName);
    }

    [Fact]
    public void Search_DuplicateFilter()
    {
        var resDupOnly = _searchService.Search(new ImageSearchRequest
        {
            Duplicate = InclusionState.Only
        });
        Assert.Equal(2, resDupOnly.Total); // id1 and id4 share the same hash
    }

    [Fact]
    public void SearchIds_ReturnsOnlyIdsList()
    {
        var ids = _searchService.GetIds(new ImageSearchRequest { Sort = "filename", Dir = "asc" });
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public void Search_SortingAndPaging()
    {
        var page1 = _searchService.Search(new ImageSearchRequest
        {
            Sort = "filename",
            Dir = "asc",
            Limit = 2,
            Offset = 0
        });
        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(3, page1.Total);

        var page2 = _searchService.Search(new ImageSearchRequest
        {
            Sort = "filename",
            Dir = "asc",
            Limit = 2,
            Offset = 2
        });
        Assert.Single(page2.Items);
    }
}
