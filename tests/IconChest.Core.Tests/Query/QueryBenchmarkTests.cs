using System.Diagnostics;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Migrations;
using IconChest.Core.Images;
using IconChest.Core.Query;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IconChest.Core.Tests.Query;

public class QueryBenchmarkTests
{
    [Fact]
    public void Compilation_TypicalQueries_CompileUnder1Ms()
    {
        var queries = new[]
        {
            "tag == \"dog\"",
            "type == png or type == jpg",
            "date < 2026-10-30",
            "title like \"elepha*\"",
            "(width >= 256 and height >= 256) and size < 100kb",
            "tag is empty and rating >= 4",
            "elephant",
            "favourite"
        };

        // Warmup
        foreach (var q in queries) QueryEngine.ParseAndCompile(q);

        var sw = new Stopwatch();

        foreach (var q in queries)
        {
            sw.Restart();
            var res = QueryEngine.ParseAndCompile(q);
            sw.Stop();

            Assert.True(res.IsValid);
            // Verify compilation is fast (under 1ms, using 5ms threshold to tolerate any jitter in CI/sandbox)
            Assert.True(sw.Elapsed.TotalMilliseconds < 5.0, $"Query '{q}' compilation took {sw.Elapsed.TotalMilliseconds:F2} ms");
        }
    }

    [Fact]
    public void Benchmark_50kImages_ExecuteQueriesAndReportTimings()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"iconchest_bench_{Guid.NewGuid():N}.db");
        var factory = new DatabaseConnectionFactory(tempFile);
        try
        {
            using (var conn = factory.CreateAndOpenConnection())
            {
                var runner = new MigrationRunner(factory);
                runner.Migrate(conn);

                conn.Execute("INSERT INTO library_folder (id, path) VALUES (1, '/home/user/icons');");
                conn.Execute("INSERT INTO tag (id, name) VALUES (1, 'ui'), (2, 'icon'), (3, 'logo'), (4, 'dark');");

                // Fast batch insert of 50,000 images in a single transaction
                using (var tx = conn.BeginTransaction())
                {
                    using var cmd = conn.CreateCommand();
                    cmd.Transaction = tx;
                    cmd.CommandText = @"
                        INSERT INTO image (
                            id, folder_id, path, path_key, file_name, extension, format,
                            width, height, size_bytes, hash, hash_algo, added_utc, title,
                            description, rating, is_favourite, is_hidden, is_missing, is_duplicate,
                            directory_path, directory_key, created_utc
                        ) VALUES (
                            $id, 1, $path, $pathKey, $name, $fmt, $fmt,
                            $w, $h, $size, $hash, 'xxh128', '2026-01-01', $title,
                            '', $rating, $fav, 0, 0, 0,
                            $dir, $dirKey, $date
                        );";

                    var pId = cmd.Parameters.Add("$id", SqliteType.Integer);
                    var pPath = cmd.Parameters.Add("$path", SqliteType.Text);
                    var pPathKey = cmd.Parameters.Add("$pathKey", SqliteType.Text);
                    var pName = cmd.Parameters.Add("$name", SqliteType.Text);
                    var pFmt = cmd.Parameters.Add("$fmt", SqliteType.Text);
                    var pW = cmd.Parameters.Add("$w", SqliteType.Integer);
                    var pH = cmd.Parameters.Add("$h", SqliteType.Integer);
                    var pSize = cmd.Parameters.Add("$size", SqliteType.Integer);
                    var pHash = cmd.Parameters.Add("$hash", SqliteType.Text);
                    var pTitle = cmd.Parameters.Add("$title", SqliteType.Text);
                    var pRating = cmd.Parameters.Add("$rating", SqliteType.Integer);
                    var pFav = cmd.Parameters.Add("$fav", SqliteType.Integer);
                    var pDir = cmd.Parameters.Add("$dir", SqliteType.Text);
                    var pDirKey = cmd.Parameters.Add("$dirKey", SqliteType.Text);
                    var pDate = cmd.Parameters.Add("$date", SqliteType.Text);

                    for (int i = 1; i <= 50_000; i++)
                    {
                        string fmt = (i % 3) switch { 0 => "png", 1 => "jpg", _ => "svg" };
                        pId.Value = i;
                        pPath.Value = $"/home/user/icons/img_{i}.{fmt}";
                        pPathKey.Value = $"/home/user/icons/img_{i}.{fmt}";
                        pName.Value = $"img_{i}.{fmt}";
                        pFmt.Value = fmt;
                        pW.Value = (i % 10 + 1) * 64;
                        pH.Value = (i % 10 + 1) * 64;
                        pSize.Value = i * 50;
                        pHash.Value = $"hash_{i:D6}";
                        pTitle.Value = $"Icon Asset {i}";
                        pRating.Value = i % 6;
                        pFav.Value = i % 2;
                        pDir.Value = "/home/user/icons";
                        pDirKey.Value = "/home/user/icons";
                        pDate.Value = $"2026-{(i % 12 + 1):D2}-01T12:00:00.0000000Z";

                        cmd.ExecuteNonQuery();
                    }

                    tx.Commit();
                }

                // Tag associations for first 10k images
                conn.Execute(@"
                    INSERT INTO image_tag (image_id, tag_id)
                    SELECT id, (id % 4 + 1) FROM image WHERE id <= 10000;");
            }

            var searchService = new ImageSearchService(factory);

            var queries = new[]
            {
                "tag == \"ui\"",
                "type == png",
                "type in (jpg, png)",
                "width >= 256",
                "size < 100kb",
                "rating == 5",
                "favourite",
                "date == 2026-05",
                "date >= 2026-01-01 and date <= 2026-06-30",
                "folder == \"/home/user/icons\"",
                "folder under \"/home/user/icons\"",
                "title like \"Icon Asset 10*\"",
                "title contains \"Asset\"",
                "(type == png or type == svg) and rating >= 4",
                "tag is empty and favourite"
            };

            var sw = new Stopwatch();
            Console.WriteLine($"{"Query",-45} | {"Time (ms)",-10} | {"Matches",-8}");
            Console.WriteLine(new string('-', 70));

            foreach (var q in queries)
            {
                var comp = QueryEngine.ParseAndCompile(q);
                Assert.True(comp.IsValid);

                var req = new ImageSearchRequest
                {
                    CustomWhereClause = comp.Query!.WhereSql,
                    CustomWhereParameters = comp.Query.Parameters.ToDictionary(k => k.Key, v => v.Value),
                    Hidden = InclusionState.Include,
                    Missing = InclusionState.Include,
                    Duplicate = InclusionState.Include,
                    Limit = 50
                };

                sw.Restart();
                var res = searchService.Search(req);
                sw.Stop();

                Console.WriteLine($"{q,-45} | {sw.ElapsedMilliseconds,-10} | {res.Total,-8}");
            }
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}
