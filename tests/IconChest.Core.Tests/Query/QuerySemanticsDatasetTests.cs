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

public class QuerySemanticsDatasetTests : IDisposable
{
    private readonly string _tempFile;
    private readonly DatabaseConnectionFactory _factory;
    private readonly ImageSearchService _searchService;

    public QuerySemanticsDatasetTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(), $"iconchest_semantics_test_{Guid.NewGuid():N}.db");
        _factory = new DatabaseConnectionFactory(_tempFile);

        using (var conn = _factory.CreateAndOpenConnection())
        {
            var migrationRunner = new MigrationRunner(_factory);
            migrationRunner.Migrate(conn);
            SeedDataset(conn);
        }

        _searchService = new ImageSearchService(_factory);
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile)) File.Delete(_tempFile);
    }

    private void SeedDataset(SqliteConnection conn)
    {
        // 1. Library folders
        conn.Execute("INSERT INTO library_folder (id, path) VALUES (1, '/home/user/icons');");
        conn.Execute("INSERT INTO library_folder (id, path) VALUES (2, '/home/user/photos');");

        // 2. Tags
        conn.Execute(@"
            INSERT INTO tag (id, name) VALUES
            (1, 'ui'),
            (2, 'icon'),
            (3, 'logo'),
            (4, 'app'),
            (5, 'dark'),
            (6, 'élégant'),
            (7, 'アイコン'),
            (8, 'badge');");

        // 3. Images (40 images)
        // Hidden: 10, 20, 30, 40
        // Missing: 8, 18, 28, 38
        // Duplicate: 5, 15, 25, 35
        // Null width/height: 39, 40
        for (int id = 1; id <= 40; id++)
        {
            string format = id switch
            {
                <= 10 => "png",
                <= 18 => "jpg",
                <= 22 => "svg",
                <= 26 => "webp",
                <= 30 => "gif",
                <= 34 => "bmp",
                <= 38 => "ico",
                _ => "png"
            };

            int? width = id >= 39 ? null : (id <= 5 ? 64 : (id <= 10 ? 128 : (id <= 15 ? 256 : (id <= 20 ? 512 : 1024))));
            int? height = id >= 39 ? null : width;

            long sizeBytes = id switch
            {
                1 => 100,
                2 => 500,
                3 => 1024,
                4 => 2048,
                5 => 50 * 1024,
                6 => 100 * 1024,
                7 => 500 * 1024,
                8 => 1024 * 1024,
                9 => 2 * 1024 * 1024,
                10 => 5 * 1024 * 1024,
                _ => id * 1000
            };

            int rating = id % 6; // 0 to 5
            bool isFav = id % 2 == 1; // 1, 3, 5, 7, 9...
            bool isHidden = id % 10 == 0; // 10, 20, 30, 40
            bool isMissing = id % 10 == 8; // 8, 18, 28, 38
            bool isDup = id % 10 == 5; // 5, 15, 25, 35

            bool? hasTransparency = id switch
            {
                <= 10 => true,
                <= 18 => false,
                <= 26 => true,
                <= 34 => false,
                _ => null
            };

            string dirPath = (id % 3) switch
            {
                0 => "/home/user/icons",
                1 => "/home/user/icons/svg",
                _ => "/home/user/icons/raster/png"
            };
            string dirKey = PathKeyProvider.Default.GetDirectoryKey(dirPath);

            string title = id switch
            {
                1 => "Éléphant d'Afrique",
                2 => "Café bar",
                3 => "Search icon button",
                4 => "Arrow left",
                5 => "Arrow right",
                _ => $"Image Title {id}"
            };

            string desc = id switch
            {
                1 => "African elephant illustration",
                3 => "Navigation search button for header",
                _ => $"Description for item {id}"
            };

            string? date = id switch
            {
                1 => "2026-01-15T12:00:00.0000000Z",
                2 => "2026-03-29T10:00:00.0000000Z",
                3 => "2026-05-10T14:30:00.0000000Z",
                4 => "2026-10-25T01:30:00.0000000Z",
                5 => "2025-12-31T23:59:59.0000000Z",
                6 => "2024-06-01T08:00:00.0000000Z",
                <= 30 => $"2026-{(id % 12 + 1):D2}-01T12:00:00.0000000Z",
                _ => null
            };

            string sql = @"
                INSERT INTO image (
                    id, folder_id, path, path_key, file_name, extension, format,
                    width, height, size_bytes, hash, hash_algo, added_utc, title,
                    description, rating, is_favourite, is_hidden, is_missing, is_duplicate,
                    has_transparency, directory_path, directory_key,
                    created_utc, modified_utc, exif_date_utc, taken_override_utc
                ) VALUES (
                    @id, 1, @path, @pathKey, @fileName, @ext, @format,
                    @width, @height, @sizeBytes, @hash, 'xxh128', '2026-01-01T00:00:00.0000000Z', @title,
                    @desc, @rating, @isFav, @isHidden, @isMissing, @isDup,
                    @hasTrans, @dirPath, @dirKey,
                    @date, @date, @date, NULL
                );";

            conn.Execute(sql, new
            {
                id,
                path = $"{dirPath}/image{id}.{format}",
                pathKey = PathKeyProvider.Default.GetDirectoryKey($"{dirPath}/image{id}.{format}"),
                fileName = $"image{id}.{format}",
                ext = format,
                format,
                width,
                height,
                sizeBytes,
                hash = $"hash_{id:D4}",
                title,
                desc,
                rating,
                isFav = isFav ? 1 : 0,
                isHidden = isHidden ? 1 : 0,
                isMissing = isMissing ? 1 : 0,
                isDup = isDup ? 1 : 0,
                hasTrans = hasTransparency.HasValue ? (hasTransparency.Value ? 1 : 0) : (int?)null,
                dirPath,
                dirKey,
                date
            });
        }

        // 4. Image tags
        // Tag 1 (ui): 1, 2, 3, 4, 19, 21, 22
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (1, 1), (2, 1), (3, 1), (4, 1), (19, 1), (21, 1), (22, 1);");
        // Tag 2 (icon): 1, 3, 5, 7, 21
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (1, 2), (3, 2), (5, 2), (7, 2), (21, 2);");
        // Tag 3 (logo): 2, 4, 6, 22
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (2, 3), (4, 3), (6, 3), (22, 3);");
        // Tag 4 (app): 1, 9
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (1, 4), (9, 4);");
        // Tag 5 (dark): 1, 2, 11, 12
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (1, 5), (2, 5), (11, 5), (12, 5);");
        // Tag 6 (élégant): 15, 16
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (15, 6), (16, 6);");
        // Tag 7 (アイコン): 17
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (17, 7);");
        // Tag 8 (badge): 25, 26
        conn.Execute("INSERT INTO image_tag (image_id, tag_id) VALUES (25, 8), (26, 8);");
    }

    private List<long> ExecuteQuery(string queryText)
    {
        var comp = QueryEngine.ParseAndCompile(queryText, TimeZoneInfo.Utc);
        Assert.True(comp.IsValid, $"Query compilation failed: {string.Join("; ", comp.Errors.Select(e => e.Message))}");

        var req = new ImageSearchRequest
        {
            CustomWhereClause = comp.Query!.WhereSql,
            CustomWhereParameters = comp.Query.Parameters.ToDictionary(k => k.Key, v => v.Value),
            Hidden = InclusionState.Include,
            Missing = InclusionState.Include,
            Duplicate = InclusionState.Include,
            Limit = 1000
        };

        return _searchService.GetIds(req, 1000).OrderBy(x => x).ToList();
    }

    [Fact]
    public void Semantics_FormatPredicates()
    {
        // Notice default implicit filters exclude hidden (10, 20, 30, 40) and missing (8, 18, 28, 38)
        var pngs = ExecuteQuery("type == png");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 9, 39 }, pngs);

        var jpgs = ExecuteQuery("type == jpg");
        Assert.Equal(new long[] { 11, 12, 13, 14, 15, 16, 17 }, jpgs);

        var jpegs = ExecuteQuery("type == jpeg");
        Assert.Equal(new long[] { 11, 12, 13, 14, 15, 16, 17 }, jpegs);

        var svgs = ExecuteQuery("type == svg");
        Assert.Equal(new long[] { 19, 21, 22 }, svgs);

        var inTypes = ExecuteQuery("type in (svg, gif)");
        Assert.Equal(new long[] { 19, 21, 22, 27, 29 }, inTypes);

        var notInTypes = ExecuteQuery("type not in (png, jpg, svg, webp, bmp, ico)");
        Assert.Equal(new long[] { 27, 29 }, notInTypes); // only gif
    }

    [Fact]
    public void Semantics_DimensionsAndNullSemantics()
    {
        var w64 = ExecuteQuery("width == 64");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5 }, w64);

        var w128 = ExecuteQuery("width == 128");
        Assert.Equal(new long[] { 6, 7, 9 }, w128); // 8 is missing, 10 is hidden

        // width != 64 includes null width images (39)
        var notW64 = ExecuteQuery("width != 64 and type == png");
        Assert.Equal(new long[] { 6, 7, 9, 39 }, notW64);

        // not (width == 64) includes null width images (39)
        var notGroupW64 = ExecuteQuery("not (width == 64) and type == png");
        Assert.Equal(new long[] { 6, 7, 9, 39 }, notGroupW64);

        // width is empty matches 39
        var emptyWidth = ExecuteQuery("width is empty and type == png");
        Assert.Equal(new long[] { 39 }, emptyWidth);

        var notEmptyWidth = ExecuteQuery("width is not empty and type == png");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 9 }, notEmptyWidth);
    }

    [Fact]
    public void Semantics_TagOperations()
    {
        // tag == "ui"
        var ui = ExecuteQuery("tag == \"ui\"");
        Assert.Equal(new long[] { 1, 2, 3, 4, 19, 21, 22 }, ui);

        // tag in ("app", "badge")
        var appOrBadge = ExecuteQuery("tag in (\"app\", \"badge\")");
        Assert.Equal(new long[] { 1, 9, 25, 26 }, appOrBadge);

        // tag == "ui" and tag == "dark"
        var uiAndDark = ExecuteQuery("tag == \"ui\" and tag == \"dark\"");
        Assert.Equal(new long[] { 1, 2 }, uiAndDark);

        // tag != "ui" (images without tag ui)
        var notUi = ExecuteQuery("tag != \"ui\" and type == png");
        Assert.Equal(new long[] { 5, 6, 7, 9, 39 }, notUi);

        // tag is empty (untagged)
        var untagged = ExecuteQuery("tag is empty and type == png");
        Assert.Equal(new long[] { 39 }, untagged);

        // tag is not empty
        var tagged = ExecuteQuery("tag is not empty and type == png");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 9 }, tagged);

        // Unicode tag: élégant
        var elegant = ExecuteQuery("tag == \"élégant\"");
        Assert.Equal(new long[] { 15, 16 }, elegant);

        // Unicode tag case-insensitive
        var elegantUpper = ExecuteQuery("tag == \"ÉLÉGANT\"");
        Assert.Equal(new long[] { 15, 16 }, elegantUpper);

        // Unicode Japanese tag: アイコン
        var jp = ExecuteQuery("tag == \"アイコン\"");
        Assert.Equal(new long[] { 17 }, jp);

        // Non-existent tag returns empty list without error
        var nonexistent = ExecuteQuery("tag == \"ghost_tag\"");
        Assert.Empty(nonexistent);
    }

    [Fact]
    public void Semantics_SizeOperations()
    {
        var small = ExecuteQuery("size <= 1kb");
        Assert.Equal(new long[] { 1, 2, 3 }, small);

        var range = ExecuteQuery("size > 100 and size < 2048");
        Assert.Equal(new long[] { 2, 3 }, range);

        var large = ExecuteQuery("size >= 1mb");
        Assert.Equal(new long[] { 9 }, large); // 8 is missing, 10 is hidden
    }

    [Fact]
    public void Semantics_RatingsAndFavourites()
    {
        // favourite shorthand
        var favs = ExecuteQuery("favourite and type == png");
        Assert.Equal(new long[] { 1, 3, 5, 7, 9, 39 }, favs);

        // !favourite
        var notFavs = ExecuteQuery("!favourite and type == png");
        Assert.Equal(new long[] { 2, 4, 6 }, notFavs);

        // rating == 0 is empty
        var unrated = ExecuteQuery("rating is empty and type == png");
        Assert.Equal(new long[] { 6 }, unrated); // 6 % 6 == 0

        var rated = ExecuteQuery("rating is not empty and type == png");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 7, 9, 39 }, rated);

        var fiveStars = ExecuteQuery("rating == 5 and type == png");
        Assert.Equal(new long[] { 5 }, fiveStars); // 5 % 6 == 5
    }

    [Fact]
    public void Semantics_FolderOperations()
    {
        // folder == exact directory
        var rootIcons = ExecuteQuery("folder == \"/home/user/icons\" and type == png");
        Assert.Equal(new long[] { 3, 6, 9, 39 }, rootIcons);

        // folder under directory
        var underIcons = ExecuteQuery("folder under \"/home/user/icons\" and type == png");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 9, 39 }, underIcons);

        // folder like pattern
        var likeSvg = ExecuteQuery("folder like \"*svg*\" and type == png");
        Assert.Equal(new long[] { 1, 4, 7 }, likeSvg);
    }

    [Fact]
    public void Semantics_TextWildcardAndContains()
    {
        // title contains literal
        var arrow = ExecuteQuery("title contains \"Arrow\"");
        Assert.Equal(new long[] { 4, 5 }, arrow);

        // title like wildcard
        var elephant = ExecuteQuery("title like \"*léphant*\"");
        Assert.Equal(new long[] { 1 }, elephant);

        // Bare term search across filename, title, description
        var bareSearch = ExecuteQuery("elephant");
        Assert.Equal(new long[] { 1 }, bareSearch);

        // Bare search with wildcards
        var wildcardSearch = ExecuteQuery("\"*icon*\" and type == png");
        Assert.Equal(new long[] { 3 }, wildcardSearch);
    }

    [Fact]
    public void Semantics_TransparencyNullAndBoolean()
    {
        // transparent == true
        var transTrue = ExecuteQuery("transparent == true and type == png");
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7, 9 }, transTrue);

        // transparent == false
        var transFalse = ExecuteQuery("transparent == false and type == jpg");
        Assert.Equal(new long[] { 11, 12, 13, 14, 15, 16, 17 }, transFalse);

        // transparent != true includes null (39) and false
        var notTransTrue = ExecuteQuery("transparent != true and type == png");
        Assert.Equal(new long[] { 39 }, notTransTrue);
    }

    [Fact]
    public void Semantics_ImplicitFiltersAndMentions()
    {
        // By default: hidden (10, 20, 30, 40) and missing (8, 18, 28, 38) are EXCLUDED
        var allPngs = ExecuteQuery("type == png");
        Assert.DoesNotContain(10L, allPngs);
        Assert.DoesNotContain(8L, allPngs);

        // Mentioning hidden includes hidden files when matched
        var hiddenPngs = ExecuteQuery("type == png and hidden == true");
        Assert.Equal(new long[] { 10, 40 }, hiddenPngs);

        // Mentioning hidden without missing still excludes missing (8)
        var withHiddenMention = ExecuteQuery("type == png and (hidden == true or hidden == false)");
        Assert.Contains(10L, withHiddenMention);
        Assert.DoesNotContain(8L, withHiddenMention);

        // Mentioning missing includes missing files
        var missingPngs = ExecuteQuery("type == png and missing == true");
        Assert.Equal(new long[] { 8 }, missingPngs);

        // Mentioning both allows both
        var bothMentioned = ExecuteQuery("(hidden == true or missing == true) and type == png");
        Assert.Equal(new long[] { 8, 10, 40 }, bothMentioned);

        // The OR quirk: mentioning hidden in one branch disables implicit hidden exclusion globally
        var orQuirk = ExecuteQuery("type == svg or hidden == true");
        Assert.Contains(10L, orQuirk); // png 10 is included because hidden was mentioned
        Assert.Contains(20L, orQuirk); // svg 20 is included
    }
}
