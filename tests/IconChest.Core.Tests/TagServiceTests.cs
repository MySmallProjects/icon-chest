using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Tags;
using Xunit;

namespace IconChest.Core.Tests;

public class TagServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly DatabaseConnectionFactory _connFactory;
    private readonly TagService _tagService;
    private readonly ImageRepository _imageRepo;
    private readonly LibraryFolderRepository _folderRepo;

    public TagServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"tag-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        var paths = new AppPaths(configDirOverride: _tempDir, dataDirOverride: _tempDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        var db = new IconChestDatabase(settings);
        db.Migrate();

        _connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        _tagService = new TagService(_connFactory);
        _imageRepo = new ImageRepository(_connFactory);
        _folderRepo = new LibraryFolderRepository(_connFactory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }
    }

    [Fact]
    public void Create_ValidNameAndColor_Succeeds()
    {
        var tag = _tagService.Create("Icon", fgColor: "#FFFFFF", bgColor: "#FF0000");

        Assert.True(tag.Id > 0);
        Assert.Equal("Icon", tag.Name);
        Assert.Equal("#FF0000", tag.BgColor);
        Assert.Equal(0, tag.ImageCount);
    }

    [Fact]
    public void Create_DuplicateCaseInsensitive_ThrowsTagConflictException()
    {
        _tagService.Create("Animals", bgColor: "#123456");

        Assert.Throws<TagConflictException>(() => _tagService.Create("animals", bgColor: "#654321"));
        Assert.Throws<TagConflictException>(() => _tagService.Create("ANIMALS", bgColor: "#654321"));
    }

    [Fact]
    public void Create_InvalidColor_ThrowsValidationException()
    {
        Assert.Throws<TagValidationException>(() => _tagService.Create("Tag", bgColor: "not-a-color"));
        Assert.Throws<TagValidationException>(() => _tagService.Create("Tag", bgColor: "#12"));
        Assert.Throws<TagValidationException>(() => _tagService.Create("Tag", bgColor: "#1234567"));
        Assert.Throws<TagValidationException>(() => _tagService.Create("", bgColor: "#ffffff"));
    }

    [Fact]
    public void Update_RenamesAndUpdatesColor()
    {
        var tag = _tagService.Create("OldName", bgColor: "#112233");
        var updated = _tagService.Update(tag.Id, name: "NewName", bgColor: "#AABBCC");

        Assert.Equal("NewName", updated.Name);
        Assert.Equal("#AABBCC", updated.BgColor);

        var fetched = _tagService.GetById(tag.Id);
        Assert.NotNull(fetched);
        Assert.Equal("NewName", fetched.Name);
    }

    [Fact]
    public void Update_DuplicateName_ThrowsTagConflictException()
    {
        _tagService.Create("TagA", bgColor: "#111111");
        var tagB = _tagService.Create("TagB", bgColor: "#222222");

        Assert.Throws<TagConflictException>(() => _tagService.Update(tagB.Id, name: "taga", bgColor: "#333333"));
    }

    [Fact]
    public void Delete_UnlinksFromImages_RemovesTag()
    {
        var folderDir = Path.Combine(_tempDir, "lib");
        Directory.CreateDirectory(folderDir);
        var folderId = _folderRepo.Add(folderDir);

        var img = new ImageRecord
        {
            FolderId = folderId,
            Path = Path.Combine(folderDir, "test.png"),
            PathKey = PathKeyProvider.Default.GetDirectoryKey(folderDir) + "/test.png",
            DirectoryPath = folderDir,
            DirectoryKey = "",
            FileName = "test.png",
            Extension = "png",
            Format = "png",
            Hash = "testhash123456789012345678901234",
            SizeBytes = 100,
            Width = 100,
            Height = 100,
            AddedUtc = DateTime.UtcNow.ToString("O"),
            CreatedUtc = DateTime.UtcNow.ToString("O"),
            ModifiedUtc = DateTime.UtcNow.ToString("O"),
            EffectiveDateUtc = DateTime.UtcNow.ToString("O")
        };
        var insertedIds = _imageRepo.InsertBatch([img]);
        var imgId = insertedIds[0];

        var tag = _tagService.Create("Deletable", bgColor: "#445566");
        using (var conn = _connFactory.CreateAndOpenConnection())
        {
            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@imgId, @tagId)", new { imgId, tagId = tag.Id });
        }

        var tagWithCount = _tagService.GetById(tag.Id);
        Assert.NotNull(tagWithCount);
        Assert.Equal(1, tagWithCount.ImageCount);

        var deleted = _tagService.Delete(tag.Id);
        Assert.True(deleted);

        Assert.Null(_tagService.GetById(tag.Id));
        var imageTags = _imageRepo.GetTagIdsForImage(imgId);
        Assert.Empty(imageTags);

        // Image record still exists intact
        var reloadedImg = _imageRepo.GetById(imgId);
        Assert.NotNull(reloadedImg);
    }
}
