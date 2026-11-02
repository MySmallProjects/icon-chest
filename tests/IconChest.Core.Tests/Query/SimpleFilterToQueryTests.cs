using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Migrations;
using IconChest.Core.Images;
using IconChest.Core.Query;
using IconChest.Core.Scanning;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class SimpleFilterToQueryTests : IDisposable
{
    private readonly string _tempFile;
    private readonly DatabaseConnectionFactory _factory;
    private readonly ImageSearchService _searchService;

    public SimpleFilterToQueryTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"iconchest_filter_test_{Guid.NewGuid():N}.db");
        _factory = new DatabaseConnectionFactory(_tempFile);

        using (var conn = _factory.CreateAndOpenConnection())
        {
            var runner = new MigrationRunner(_factory);
            runner.Migrate(conn);
            SeedData(conn);
        }

        _searchService = new ImageSearchService(_factory);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private static void SeedData(SqliteConnection conn)
    {
        conn.Execute("INSERT INTO library_folder (id, path) VALUES (1, '/home/user/icons');");
        conn.Execute("INSERT INTO tag (id, name) VALUES (1, 'ui'), (2, 'icon'), (3, 'logo');");

        for (int i = 1; i <= 30; i++)
        {
            string fmt = i % 2 == 0 ? "png" : "jpg";
            int rating = i % 6;
            bool fav = i % 2 == 1;
            int width = i * 32;
            int height = i * 32;
            long size = i * 1024;
            string date = $"2026-{(i % 12 + 1):D2}-01T12:00:00.0000000Z";
            string dir = i % 2 == 0 ? "/home/user/icons" : "/home/user/icons/sub";
            string dirKey = PathKeyProvider.Default.GetDirectoryKey(dir);

            conn.Execute(@"
                INSERT INTO image (
                    id, folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, added_utc, title,
                    description, rating, is_favourite, is_hidden, is_missing, is_duplicate,
                    directory_path, directory_key, created_utc
                ) VALUES (
                    @id, 1, @path, @pathKey, @name, @fmt, @fmt,
                    @width, @height, @size, @hash, 'xxh128', '2026-01-01', @title,
                    '', @rating, @fav, 0, 0, 0,
                    @dir, @dirKey, @date
                );", new
            {
                id = i,
                path = $"{dir}/img{i}.{fmt}",
                pathKey = PathKeyProvider.Default.GetDirectoryKey($"{dir}/img{i}.{fmt}"),
                name = $"img{i}.{fmt}",
                fmt,
                width,
                height,
                size,
                hash = $"h{i}",
                title = $"Title {i}",
                rating,
                fav = fav ? 1 : 0,
                dir,
                dirKey,
                date
            });

            if (i % 3 == 0) conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, 1);", new { id = i });
            if (i % 5 == 0) conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, 2);", new { id = i });
        }
    }

    [Fact]
    public void Convert_25FilterCombinations_ReturnSameIdsAsStructuredFilter()
    {
        var testRequests = new List<ImageSearchRequest>
        {
            new() { Types = new List<string> { "png" } },
            new() { Types = new List<string> { "jpg" } },
            new() { Types = new List<string> { "png", "jpg" } },
            new() { MinWidth = 100 },
            new() { MaxWidth = 500 },
            new() { MinHeight = 200, MaxHeight = 800 },
            new() { MinSize = 5000 },
            new() { MaxSize = 20000 },
            new() { RatingMin = 3 },
            new() { RatingMin = 5 },
            new() { Favourite = true },
            new() { Favourite = false },
            new() { Text = "img1" },
            new() { Text = "Title" },
            new() { Year = 2026 },
            new() { Year = 2026, Month = 5 },
            new() { DateFrom = "2026-02-01", DateTo = "2026-06-01" },
            new() { Tags = new List<string> { "ui" } },
            new() { Tags = new List<string> { "icon" } },
            new() { Tags = new List<string> { "ui", "icon" }, TagMode = TagFilterMode.Any },
            new() { Tags = new List<string> { "ui", "icon" }, TagMode = TagFilterMode.All },
            new() { Directory = "/home/user/icons", IncludeSubfolders = false },
            new() { Directory = "/home/user/icons", IncludeSubfolders = true },
            new() { Types = new List<string> { "png" }, Favourite = true, RatingMin = 4 },
            new() { MinWidth = 200, MaxSize = 50000, Types = new List<string> { "jpg" } }
        };

        Assert.True(testRequests.Count >= 25);

        foreach (var req in testRequests)
        {
            var convertRes = SimpleFilterToQuery.Convert(req);
            Assert.True(convertRes.Success, $"Failed converting: {convertRes.UnsupportedReason}");
            Assert.NotNull(convertRes.QueryText);

            var structuredIds = _searchService.GetIds(req, 1000).OrderBy(x => x).ToList();

            var comp = QueryEngine.ParseAndCompile(convertRes.QueryText, TimeZoneInfo.Utc);
            Assert.True(comp.IsValid, $"Compiled query invalid for '{convertRes.QueryText}': {string.Join("; ", comp.Errors.Select(e => e.Message))}");

            var queryReq = new ImageSearchRequest
            {
                CustomWhereClause = comp.Query!.WhereSql,
                CustomWhereParameters = comp.Query.Parameters.ToDictionary(k => k.Key, v => v.Value),
                Hidden = InclusionState.Include,
                Missing = InclusionState.Include,
                Duplicate = InclusionState.Include
            };

            var queryIds = _searchService.GetIds(queryReq, 1000).OrderBy(x => x).ToList();
            Assert.Equal(structuredIds, queryIds);
        }
    }

    [Fact]
    public void Convert_UnsupportedFilters_ReportsUnsupportedReason()
    {
        // 1. Month without Year
        var req1 = new ImageSearchRequest { Month = 5 };
        var res1 = SimpleFilterToQuery.Convert(req1);
        Assert.False(res1.Success);
        Assert.Contains("Month", res1.UnsupportedReason);

        // 2. FolderId without Directory
        var req2 = new ImageSearchRequest { FolderId = 123 };
        var res2 = SimpleFilterToQuery.Convert(req2);
        Assert.False(res2.Success);
        Assert.Contains("Folder ID", res2.UnsupportedReason);
    }
}
