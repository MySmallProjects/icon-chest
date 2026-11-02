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

public class ImageEditServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseConnectionFactory _connFactory;
    private readonly ImageEditService _editService;
    private readonly ImageRepository _imageRepo;
    private readonly LibraryFolderRepository _folderRepo;
    private readonly TagService _tagService;
    private readonly long _folderId;
    private readonly string _libDir;

    public ImageEditServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"edit-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var paths = new AppPaths(configDirOverride: _tempDir, dataDirOverride: _tempDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        var db = new IconChestDatabase(settings);
        db.Migrate();

        _connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        _imageRepo = new ImageRepository(_connFactory);
        _editService = new ImageEditService(_connFactory, _imageRepo);
        _folderRepo = new LibraryFolderRepository(_connFactory);
        _tagService = new TagService(_connFactory);

        _libDir = Path.Combine(_tempDir, "lib");
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
    public void BulkEdit_RatingValidation_RejectsInvalidRatings()
    {
        var rec = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "test.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/test.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "test.png",
            Extension = "png",
            Format = "png",
            Hash = "testhash000000000000000000000001",
            SizeBytes = 100,
            Width = 10,
            Height = 10,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O")
        };
        var imgId = _imageRepo.InsertBatch([rec])[0];

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _editService.EditImages([imgId], new ImageEditChanges { HasRating = true, Rating = -1 }));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _editService.EditImages([imgId], new ImageEditChanges { HasRating = true, Rating = 6 }));
    }

    [Fact]
    public void BulkEdit_PresenceVsNull_OnlySpecifiedFieldsChanged()
    {
        var rec = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "test2.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/test2.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "test2.png",
            Extension = "png",
            Format = "png",
            Hash = "testhash000000000000000000000002",
            SizeBytes = 100,
            Width = 10,
            Height = 10,
            Rating = 2,
            IsFavourite = false,
            Title = "Original Title",
            Description = "Original Notes",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O")
        };
        var imgId = _imageRepo.InsertBatch([rec])[0];

        // Update only Rating and IsFavourite
        _editService.EditImages([imgId], new ImageEditChanges
        {
            HasRating = true,
            Rating = 5,
            HasIsFavourite = true,
            IsFavourite = true
        });

        var updated = _imageRepo.GetById(imgId);
        Assert.NotNull(updated);
        Assert.Equal(5, updated.Rating);
        Assert.True(updated.IsFavourite);
        Assert.Equal("Original Title", updated.Title); // Unchanged!
        Assert.Equal("Original Notes", updated.Description); // Unchanged!
    }

    [Fact]
    public void BulkEdit_ResetTitleToNull()
    {
        var rec = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "test3.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/test3.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "test3.png",
            Extension = "png",
            Format = "png",
            Hash = "testhash000000000000000000000003",
            SizeBytes = 100,
            Width = 10,
            Height = 10,
            Title = "Some Title",
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O")
        };
        var imgId = _imageRepo.InsertBatch([rec])[0];

        _editService.EditImages([imgId], new ImageEditChanges
        {
            HasTitle = true,
            Title = null
        });

        var updated = _imageRepo.GetById(imgId);
        Assert.NotNull(updated);
        Assert.Equal("test3", updated.Title);
    }

    [Fact]
    public void BulkEdit_AddAndRemoveTags()
    {
        var tag1 = _tagService.Create("Tag1", bgColor: "#111111");
        var tag2 = _tagService.Create("Tag2", bgColor: "#222222");
        var tag3 = _tagService.Create("Tag3", bgColor: "#333333");

        var rec = new ImageRecord
        {
            FolderId = _folderId,
            Path = Path.Combine(_libDir, "test4.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(_libDir) + "/test4.png",
            DirectoryPath = _libDir,
            DirectoryKey = "",
            FileName = "test4.png",
            Extension = "png",
            Format = "png",
            Hash = "testhash000000000000000000000004",
            SizeBytes = 100,
            Width = 10,
            Height = 10,
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            AddedUtc = DateTime.UtcNow.ToString("O")
        };
        var imgId = _imageRepo.InsertBatch([rec])[0];

        using (var conn = _connFactory.CreateAndOpenConnection())
        {
            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@imgId, @tagId)", new { imgId, tagId = tag1.Id });
        }

        // Add tag2, tag3, remove tag1
        _editService.EditImages([imgId], new ImageEditChanges
        {
            AddTagIds = [tag2.Id, tag3.Id],
            RemoveTagIds = [tag1.Id]
        });

        var currentTags = _imageRepo.GetTagIdsForImage(imgId);
        Assert.DoesNotContain(tag1.Id, currentTags);
        Assert.Contains(tag2.Id, currentTags);
        Assert.Contains(tag3.Id, currentTags);
    }
}
