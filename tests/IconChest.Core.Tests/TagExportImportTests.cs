using System.Text.Json;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Events;
using IconChest.Core.Tags;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IconChest.Core.Tests;

public sealed class TagExportImportTests : IDisposable
{
    private readonly string _testDb;
    private readonly DatabaseConnectionFactory _connectionFactory;
    private readonly TagService _tagService;

    public TagExportImportTests()
    {
        _testDb = Path.Combine(Path.GetTempPath(), $"iconchest-tags-test-{Guid.NewGuid():N}.db");
        var database = new IconChestDatabase(_testDb);
        database.Migrate();

        _connectionFactory = new DatabaseConnectionFactory(_testDb);
        _tagService = new TagService(_connectionFactory, new LibraryEventEmitter(), SystemClock.Instance);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { File.Delete(_testDb); } catch { }
    }

    [Fact]
    public void Export_ProducesValidDocument()
    {
        _tagService.Create("Vector", "#FFFFFF", "#1E40AF");
        _tagService.Create("Raster", "#000000", "#F59E0B");

        var doc = _tagService.ExportTags();

        Assert.Equal("iconchest-tags", doc.Format);
        Assert.Equal(1, doc.Version);
        Assert.Equal(2, doc.Tags.Count);

        var json = JsonSerializer.Serialize(doc);
        Assert.Contains("iconchest-tags", json);
        Assert.Contains("Vector", json);
        Assert.Contains("#1E40AF", json);
    }

    [Fact]
    public void Import_MergeByNameCaseInsensitive_ExistingTagWinsColor()
    {
        // Existing local tag: Blue background
        _tagService.Create("Nature", "#FFFFFF", "#0000FF");

        var doc = new TagExportDocument(
            Format: "iconchest-tags",
            Version: 1,
            ExportedUtc: DateTime.UtcNow.ToString("O"),
            Tags: new List<TagExportItem>
            {
                // Different case, trying to set Red background -> local should win!
                new("nature", "#000000", "#FF0000"),
                // New tag
                new("Urban", "#FFFFFF", "#10B981")
            });

        var result = _tagService.ImportTags(doc);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(1, result.SkippedCount);
        Assert.Equal(0, result.InvalidCount);
        Assert.Equal(0, result.DuplicateInFileCount);

        var all = _tagService.GetAll();
        Assert.Equal(2, all.Count);

        var nature = all.First(t => t.Name.Equals("Nature", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("#0000FF", nature.BgColor); // Existing color retained!
    }

    [Fact]
    public void Import_DuplicatesInFile_KeepsFirstAndCountsDuplicates()
    {
        var doc = new TagExportDocument(
            Format: "iconchest-tags",
            Version: 1,
            ExportedUtc: DateTime.UtcNow.ToString("O"),
            Tags: new List<TagExportItem>
            {
                new("UniqueTag", "#FFFFFF", "#111111"),
                new("uniquetag", "#000000", "#222222"),
                new("UNIQUETAG", "#FFFFFF", "#333333")
            });

        var result = _tagService.ImportTags(doc);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(2, result.DuplicateInFileCount);
        Assert.Equal(0, result.SkippedCount);

        var all = _tagService.GetAll();
        Assert.Single(all);
        Assert.Equal("#111111", all[0].BgColor); // First entry in file kept
    }

    [Fact]
    public void Import_InvalidEntries_SkippedAndReported()
    {
        var doc = new TagExportDocument(
            Format: "iconchest-tags",
            Version: 1,
            ExportedUtc: DateTime.UtcNow.ToString("O"),
            Tags: new List<TagExportItem>
            {
                new("", "#FFFFFF", "#111111"),                           // empty name
                new(new string('A', 51), "#FFFFFF", "#111111"),         // > 50 chars
                new("Bad\tName", "#FFFFFF", "#111111"),                 // control character
                new("BadColor", "not-a-color", "#111111"),              // invalid hex
                new("GoodTag", "#FFFFFF", "#10B981")                    // valid
            });

        var result = _tagService.ImportTags(doc);

        Assert.Equal(1, result.AddedCount);
        Assert.Equal(4, result.InvalidCount);
        Assert.NotEmpty(result.Errors);
    }

    [Fact]
    public void Import_NewerVersionOrBadFormat_Throws()
    {
        var futureDoc = new TagExportDocument("iconchest-tags", 2, DateTime.UtcNow.ToString("O"), Array.Empty<TagExportItem>());
        Assert.Throws<TagValidationException>(() => _tagService.ImportTags(futureDoc));

        var wrongFormat = new TagExportDocument("wrong-format", 1, DateTime.UtcNow.ToString("O"), Array.Empty<TagExportItem>());
        Assert.Throws<TagValidationException>(() => _tagService.ImportTags(wrongFormat));
    }

    [Fact]
    public void Import_10000Tags_PerformanceIsAcceptable()
    {
        var tags = new List<TagExportItem>(10000);
        for (int i = 0; i < 10000; i++)
        {
            tags.Add(new($"Tag_{i:D5}", "#FFFFFF", "#1F2937"));
        }

        var doc = new TagExportDocument("iconchest-tags", 1, DateTime.UtcNow.ToString("O"), tags);

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = _tagService.ImportTags(doc);
        sw.Stop();

        Assert.Equal(10000, result.AddedCount);
        Assert.True(sw.ElapsedMilliseconds < 5000, $"Expected 10,000 tag import under 5s, took {sw.ElapsedMilliseconds}ms");
    }
}

