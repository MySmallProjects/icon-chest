using System.Diagnostics;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Images;
using IconChest.Core.Settings;
using IconChest.Core.Trees;
using Xunit;
using Xunit.Abstractions;

namespace IconChest.Core.Tests;

public sealed class LargeDatasetFixture : IDisposable
{
    public string TempDir { get; }
    public DatabaseConnectionFactory ConnFactory { get; }
    public ImageSearchService SearchService { get; }
    public TreeService TreeService { get; }

    public LargeDatasetFixture()
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"perf-fixture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDir);

        var paths = new AppPaths(configDirOverride: TempDir, dataDirOverride: TempDir, isWindowsOverride: false);
        var settings = new SettingsService(paths);
        var db = new IconChestDatabase(settings);
        db.Migrate();

        ConnFactory = new DatabaseConnectionFactory(db.DatabasePath);
        SearchService = new ImageSearchService(ConnFactory);
        TreeService = new TreeService(ConnFactory);

        SeedLargeDataset();
    }

    private void SeedLargeDataset()
    {
        using var conn = ConnFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();

        // 1. Insert 5 library folders
        for (int f = 1; f <= 5; f++)
        {
            conn.Execute(
                "INSERT INTO library_folder (id, path, enabled) VALUES (@id, @path, 1)",
                new { id = f, path = $"/photos/folder{f}" },
                tx);
        }

        // 2. Insert 200 tags
        for (int t = 1; t <= 200; t++)
        {
            conn.Execute(
                "INSERT INTO tag (id, name, fg_color, bg_color) VALUES (@id, @name, '#FFFFFF', '#4A5568')",
                new { id = t, name = $"Tag_{t}" },
                tx);
        }

        // 3. Insert 50,000 synthetic image rows in chunks of 5,000
        const int totalImages = 50_000;
        const int chunkSize = 5_000;

        for (int offset = 0; offset < totalImages; offset += chunkSize)
        {
            var images = new List<object>(chunkSize);
            for (int i = 1; i <= chunkSize; i++)
            {
                int imgId = offset + i;
                int folderId = (imgId % 5) + 1;
                string dir = (imgId % 20 == 0) ? "subfolder" : "";
                string ext = (imgId % 4 == 0) ? "svg" : (imgId % 3 == 0) ? "jpg" : "png";
                int year = 2020 + (imgId % 5);
                int month = (imgId % 12) + 1;
                int day = (imgId % 28) + 1;
                string dateStr = $"{year:D4}-{month:D2}-{day:D2}T12:00:00Z";

                images.Add(new
                {
                    id = imgId,
                    folder_id = folderId,
                    path = $"/photos/folder{folderId}/{dir}/img_{imgId}.{ext}",
                    path_key = $"/photos/folder{folderId}/{dir}/img_{imgId}.{ext}",
                    directory_path = $"/photos/folder{folderId}/{dir}",
                    directory_key = $"/photos/folder{folderId}/{dir}",
                    file_name = $"img_{imgId}.{ext}",
                    extension = ext,
                    format = ext == "jpg" ? "jpeg" : ext,
                    width = 256,
                    height = 256,
                    size_bytes = 1000 + (imgId % 10000),
                    hash = $"hash_{imgId:D8}_{imgId:D8}_{imgId:D8}_{imgId:D8}",
                    hash_algo = "xxh128",
                    created_utc = dateStr,
                    modified_utc = dateStr,
                    added_utc = dateStr,
                    title = $"Image Title {imgId}",
                    description = $"Synthetic test image number {imgId}",
                    rating = imgId % 6,
                    is_favourite = (imgId % 10 == 0) ? 1 : 0,
                    is_hidden = 0,
                    is_missing = 0,
                    is_duplicate = 0,
                    has_transparency = 1,
                    status = "ok"
                });
            }

            conn.Execute(@"
                INSERT INTO image (
                    id, folder_id, path, path_key, directory_path, directory_key,
                    file_name, extension, format, width, height, size_bytes,
                    hash, hash_algo, created_utc, modified_utc, added_utc,
                    title, description, rating, is_favourite, is_hidden, is_missing,
                    is_duplicate, has_transparency, status
                ) VALUES (
                    @id, @folder_id, @path, @path_key, @directory_path, @directory_key,
                    @file_name, @extension, @format, @width, @height, @size_bytes,
                    @hash, @hash_algo, @created_utc, @modified_utc, @added_utc,
                    @title, @description, @rating, @is_favourite, @is_hidden, @is_missing,
                    @is_duplicate, @has_transparency, @status
                );", images, tx);
        }

        // 4. Insert ~250,000 tag links
        for (int offset = 0; offset < totalImages; offset += chunkSize)
        {
            var links = new List<object>(chunkSize * 5);
            for (int i = 1; i <= chunkSize; i++)
            {
                int imgId = offset + i;
                for (int t = 1; t <= 5; t++)
                {
                    int tagId = ((imgId + t) % 200) + 1;
                    links.Add(new { imgId, tagId });
                }
            }

            conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@imgId, @tagId);", links, tx);
        }

        tx.Commit();
    }

    public void Dispose()
    {
        if (Directory.Exists(TempDir))
        {
            try { Directory.Delete(TempDir, recursive: true); } catch { }
        }
    }
}

public class ImageQueryPerformanceTests : IClassFixture<LargeDatasetFixture>
{
    private readonly LargeDatasetFixture _fixture;
    private readonly ITestOutputHelper _output;

    public ImageQueryPerformanceTests(LargeDatasetFixture fixture, ITestOutputHelper output)
    {
        _fixture = fixture;
        _output = output;
    }

    [Fact]
    public void Benchmark_SearchFilterByTag_SortByDate_PagesExecuteUnder100Ms()
    {
        var request = new ImageSearchRequest
        {
            Tags = ["Tag_42"],
            TagMode = TagFilterMode.Any,
            Sort = "effectivedate",
            Dir = "desc",
            Limit = 50,
            Offset = 0
        };

        // Warm-up query
        _fixture.SearchService.Search(request);

        // Page 1 to 10
        for (int page = 0; page < 10; page++)
        {
            request.Offset = page * 50;
            var sw = Stopwatch.StartNew();
            var result = _fixture.SearchService.Search(request);
            sw.Stop();

            _output.WriteLine($"Page {page + 1}: {result.Items.Count} items in {sw.ElapsedMilliseconds} ms (Total: {result.Total})");
            Assert.True(sw.ElapsedMilliseconds < 100, $"Query on page {page + 1} took {sw.ElapsedMilliseconds}ms (must be under 100ms)");
            Assert.NotEmpty(result.Items);
        }
    }

    [Fact]
    public void Benchmark_FolderTree_ExecutesUnder100Ms()
    {
        // Warm up
        _fixture.TreeService.GetFolderTree();

        var sw = Stopwatch.StartNew();
        var roots = _fixture.TreeService.GetFolderTree();
        sw.Stop();

        _output.WriteLine($"Folder tree: {roots.Count} roots in {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 100, $"Folder tree took {sw.ElapsedMilliseconds}ms (must be under 100ms)");
        Assert.Equal(5, roots.Count);
    }

    [Fact]
    public void Benchmark_YearTree_ExecutesUnder100Ms()
    {
        // Warm up
        _fixture.TreeService.GetYearTree("effective");

        var sw = Stopwatch.StartNew();
        var yearTree = _fixture.TreeService.GetYearTree("effective");
        sw.Stop();

        _output.WriteLine($"Year tree: {yearTree.Years.Count} years in {sw.ElapsedMilliseconds} ms");
        Assert.True(sw.ElapsedMilliseconds < 100, $"Year tree took {sw.ElapsedMilliseconds}ms (must be under 100ms)");
        Assert.NotEmpty(yearTree.Years);
    }

    [Fact]
    public void Benchmark_GetIdsEndpoint_ExecutesUnder100Ms()
    {
        var request = new ImageSearchRequest
        {
            Year = 2022,
            Types = ["png"],
            Sort = "effectivedate",
            Dir = "desc"
        };

        // Warm up
        _fixture.SearchService.GetIds(request);

        long minElapsed = long.MaxValue;
        IReadOnlyList<long> ids = [];
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            ids = _fixture.SearchService.GetIds(request);
            sw.Stop();
            minElapsed = Math.Min(minElapsed, sw.ElapsedMilliseconds);
        }

        _output.WriteLine($"IDs endpoint: returned {ids.Count} IDs in {minElapsed} ms");
        Assert.True(minElapsed < 100, $"IDs endpoint took {minElapsed}ms (must be under 100ms)");
        Assert.NotEmpty(ids);
    }

    [Fact]
    public void ExplainQueryPlan_ShowsIndexCoverageIsUsed()
    {
        using var conn = _fixture.ConnFactory.CreateAndOpenConnection();

        // 1. Tag search query plan
        const string tagSql = @"
            EXPLAIN QUERY PLAN
            SELECT i.id, i.file_name
            FROM image i
            WHERE i.is_hidden = 0 AND i.is_missing = 0
              AND i.id IN (SELECT image_id FROM image_tag WHERE tag_id = 42)
            ORDER BY i.effective_date_utc DESC
            LIMIT 50;";

        var tagPlan = conn.Query<dynamic>(tagSql).ToList();
        var tagPlanDetail = string.Join("\n", tagPlan.Select(p => (string)p.detail));
        _output.WriteLine($"Tag Plan:\n{tagPlanDetail}");

        Assert.Contains("USING INDEX", tagPlanDetail);

        // 2. Folder tree directory counts query plan
        const string treeSql = @"
            EXPLAIN QUERY PLAN
            SELECT folder_id, directory_key, directory_path, COUNT(*)
            FROM image
            WHERE is_hidden = 0 AND is_missing = 0
            GROUP BY folder_id, directory_key, directory_path;";

        var treePlan = conn.Query<dynamic>(treeSql).ToList();
        var treePlanDetail = string.Join("\n", treePlan.Select(p => (string)p.detail));
        _output.WriteLine($"Tree Plan:\n{treePlanDetail}");

        Assert.Contains("INDEX", treePlanDetail);
    }
}
