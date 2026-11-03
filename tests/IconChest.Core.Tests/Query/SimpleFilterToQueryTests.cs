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
        conn.Execute("INSERT INTO library_folder (id, path) VALUES (1, '/home/user/icons'), (2, '/home/user/photos');");
        conn.Execute("INSERT INTO tag (id, name) VALUES (1, 'ui'), (2, 'icon'), (3, 'logo');");

        for (int i = 1; i <= 60; i++)
        {
            string fmt = i % 3 == 0 ? "svg" : (i % 2 == 0 ? "png" : "jpg");
            int rating = i % 6;
            bool fav = i % 2 == 1;
            int width = i * 32;
            int height = i * 32;
            long size = i * 1024;
            string date = $"2026-{(i % 12 + 1):D2}-01T12:00:00.0000000Z";
            string dir = i % 2 == 0 ? "/home/user/icons" : "/home/user/icons/sub";
            int folderId = i % 4 == 0 ? 2 : 1;
            if (folderId == 2) dir = "/home/user/photos";
            string dirKey = PathKeyProvider.Default.GetDirectoryKey(dir);

            int isHidden = (i == 10 || i == 20) ? 1 : 0;
            int isDuplicate = (i == 15 || i == 25) ? 1 : 0;

            conn.Execute(@"
                INSERT INTO image (
                    id, folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, added_utc, title,
                    description, rating, is_favourite, is_hidden, is_missing, is_duplicate,
                    directory_path, directory_key, created_utc, modified_utc
                ) VALUES (
                    @id, @folderId, @path, @pathKey, @name, @fmt, @fmt,
                    @width, @height, @size, @hash, 'xxh128', '2026-01-01', @title,
                    @desc, @rating, @fav, @isHidden, 0, @isDuplicate,
                    @dir, @dirKey, @date, @date
                );", new
            {
                id = i,
                folderId,
                path = $"{dir}/img{i}.{fmt}",
                pathKey = PathKeyProvider.Default.GetDirectoryKey($"{dir}/img{i}.{fmt}"),
                name = $"img{i}.{fmt}",
                fmt,
                width,
                height,
                size,
                hash = $"h{i}",
                title = $"Title {i} vector",
                desc = $"Description note {i}",
                rating,
                fav = fav ? 1 : 0,
                isHidden,
                isDuplicate,
                dir,
                dirKey,
                date
            });

            if (i % 3 == 0) conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, 1);", new { id = i });
            if (i % 5 == 0) conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, 2);", new { id = i });
            if (i % 7 == 0) conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (@id, 3);", new { id = i });
        }
    }

    [Fact]
    public void Convert_Over60FilterCombinations_ReturnSameIdsAsStructuredFilter()
    {
        string? FolderResolver(long id) => id switch
        {
            1 => "/home/user/icons",
            2 => "/home/user/photos",
            _ => null
        };

        var testRequests = new List<ImageSearchRequest>
        {
            // Types
            new() { Types = new List<string> { "png" } },
            new() { Types = new List<string> { "jpg" } },
            new() { Types = new List<string> { "svg" } },
            new() { Types = new List<string> { "png", "jpg" } },
            new() { Types = new List<string> { "png", "svg" } },

            // Dimensions
            new() { MinWidth = 100 },
            new() { MaxWidth = 500 },
            new() { MinHeight = 200, MaxHeight = 800 },
            new() { MinWidth = 64, MaxWidth = 256 },
            new() { MinHeight = 512 },

            // Sizes
            new() { MinSize = 5000 },
            new() { MaxSize = 20000 },
            new() { MinSize = 10000, MaxSize = 40000 },

            // Ratings
            new() { RatingMin = 3 },
            new() { RatingMin = 5 },
            new() { RatingMax = 0 }, // Unrated
            new() { RatingMax = 2 },
            new() { RatingMin = 2, RatingMax = 4 },
            new() { RatingMin = 4, RatingMax = 4 },

            // Favourite
            new() { Favourite = true },
            new() { Favourite = false },

            // Text searches
            new() { Text = "img1" },
            new() { Text = "Title" },
            new() { Text = "vector" },
            new() { Text = "Title vector" }, // multiple words

            // Dates & Ranges
            new() { Year = 2026 },
            new() { Year = 2026, Month = 5 },
            new() { DateFrom = "2026-02-01", DateTo = "2026-06-01" },
            new() { RangeField = "created", DateFrom = "2026-01-01", DateTo = "2026-04-01" },
            new() { RangeField = "modified", DateFrom = "2026-03-01" },
            new() { RangeField = "effective", DateTo = "2026-05-01" },

            // Tags (Scope)
            new() { Tags = new List<string> { "ui" } },
            new() { Tags = new List<string> { "icon" } },
            new() { Tags = new List<string> { "ui", "icon" }, TagMode = TagFilterMode.Any },
            new() { Tags = new List<string> { "ui", "icon" }, TagMode = TagFilterMode.All },

            // FilterTags (Filter Bar)
            new() { FilterTags = new List<string> { "logo" } },
            new() { FilterTags = new List<string> { "ui", "logo" }, FilterTagMode = TagFilterMode.Any },
            new() { FilterTags = new List<string> { "ui", "icon" }, FilterTagMode = TagFilterMode.All },

            // Untagged
            new() { Untagged = true },

            // Folders
            new() { Directory = "/home/user/icons", IncludeSubfolders = false },
            new() { Directory = "/home/user/icons", IncludeSubfolders = true },
            new() { FilterFolderId = 1 },
            new() { FilterFolderId = 2 },

            // Inclusions (Hidden, Duplicate)
            new() { Hidden = InclusionState.Only },
            new() { Hidden = InclusionState.Include },
            new() { Duplicate = InclusionState.Only },
            new() { Duplicate = InclusionState.Exclude },

            // Pairs
            new() { Types = new List<string> { "png" }, Favourite = true },
            new() { Types = new List<string> { "jpg" }, RatingMin = 4 },
            new() { Types = new List<string> { "svg" }, FilterFolderId = 1 },
            new() { RatingMax = 0, Types = new List<string> { "png" } },
            new() { Untagged = true, Types = new List<string> { "jpg" } },
            new() { FilterTags = new List<string> { "ui" }, RatingMin = 3 },
            new() { DateFrom = "2026-01-01", DateTo = "2026-06-01", RatingMin = 2 },
            new() { MinWidth = 200, MaxSize = 50000 },
            new() { Text = "img", Favourite = true },
            new() { FilterFolderId = 1, RangeField = "created", DateFrom = "2026-02-01" },
            new() { Hidden = InclusionState.Include, RatingMin = 4 },
            new() { Duplicate = InclusionState.Only, Types = new List<string> { "png" } },

            // Triplets and Complex Combinations
            new() { Types = new List<string> { "png" }, Favourite = true, RatingMin = 4 },
            new() { MinWidth = 200, MaxSize = 50000, Types = new List<string> { "jpg" } },
            new() { FilterFolderId = 1, FilterTags = new List<string> { "ui" }, RatingMin = 2 },
            new() { FilterFolderId = 2, Types = new List<string> { "png", "jpg" }, Favourite = false },
            new() { Untagged = true, MinWidth = 100, MaxWidth = 1000, RatingMin = 1 },
            new() { FilterTags = new List<string> { "icon" }, RangeField = "effective", DateFrom = "2026-01-01", DateTo = "2026-07-01" },
            new() { Text = "vector", Types = new List<string> { "png", "svg" }, RatingMin = 3 },
            new() { Directory = "/home/user/icons", IncludeSubfolders = true, FilterTags = new List<string> { "ui" }, Types = new List<string> { "png" } },
            new() { RangeField = "created", DateFrom = "2026-02-01", RatingMin = 2, RatingMax = 5, Types = new List<string> { "svg" } },
            new() { FilterFolderId = 1, RatingMax = 0, Hidden = InclusionState.Include },
            new() { Text = "Title", Favourite = true, Types = new List<string> { "jpg" }, MinHeight = 100 },
            new() { FilterTags = new List<string> { "ui", "icon" }, FilterTagMode = TagFilterMode.Any, Favourite = true, RatingMin = 1 }
        };

        Assert.True(testRequests.Count >= 60, $"Expected >= 60 test combinations, got {testRequests.Count}");

        foreach (var req in testRequests)
        {
            var convertRes = SimpleFilterToQuery.Convert(req, FolderResolver);
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
            Assert.True(structuredIds.SequenceEqual(queryIds),
                $"Mismatch for query: '{convertRes.QueryText}'. Structured: [{string.Join(",", structuredIds)}], Query: [{string.Join(",", queryIds)}]");
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
