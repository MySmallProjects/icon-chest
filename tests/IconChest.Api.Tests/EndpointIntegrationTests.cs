using System.Net;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using IconChest.Api.Tests.TestHelpers;
using Xunit;

namespace IconChest.Api.Tests;

[Collection("ApiCollection")]
public class EndpointIntegrationTests
{
    private readonly IconChestApiFactory _factory;

    public EndpointIntegrationTests(IconChestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Folders_CrudLifecycle()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. Create a physical directory to add
        var libDir = Path.Combine(_factory.TempDir, "TestLib");
        Directory.CreateDirectory(libDir);

        // 2. Add folder (POST /api/folders)
        var addPayload = new StringContent(
            JsonSerializer.Serialize(new { path = libDir }),
            Encoding.UTF8,
            "application/json");

        var addRes = await client.PostAsync("/api/folders", addPayload);
        Assert.Equal(HttpStatusCode.Created, addRes.StatusCode);
        var addJson = await addRes.Content.ReadAsStringAsync();
        using var addDoc = JsonDocument.Parse(addJson);
        long folderId = addDoc.RootElement.GetProperty("id").GetInt64();
        Assert.True(folderId > 0);

        // 3. List folders (GET /api/folders)
        var listRes = await client.GetAsync("/api/folders");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listJson = await listRes.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);
        Assert.True(listDoc.RootElement.GetArrayLength() > 0);

        // 4. Update folder (PATCH /api/folders/{id})
        var patchPayload = new StringContent(
            JsonSerializer.Serialize(new { enabled = false }),
            Encoding.UTF8,
            "application/json");
        var patchRes = await client.PatchAsync($"/api/folders/{folderId}", patchPayload);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // 5. Delete folder (DELETE /api/folders/{id})
        var delRes = await client.DeleteAsync($"/api/folders/{folderId}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
    }

    [Fact]
    public async Task Tags_CrudLifecycle()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. Create tag (POST /api/tags)
        var createPayload = new StringContent(
            JsonSerializer.Serialize(new { name = "FavoriteTag", fgColor = "#FFFFFF", bgColor = "#FF5500" }),
            Encoding.UTF8,
            "application/json");

        var createRes = await client.PostAsync("/api/tags", createPayload);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        var createJson = await createRes.Content.ReadAsStringAsync();
        using var createDoc = JsonDocument.Parse(createJson);
        long tagId = createDoc.RootElement.GetProperty("id").GetInt64();

        // 2. Get tag (GET /api/tags/{id})
        var getRes = await client.GetAsync($"/api/tags/{tagId}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        // 3. Update tag (PATCH /api/tags/{id})
        var updatePayload = new StringContent(
            JsonSerializer.Serialize(new { name = "RenamedTag", bgColor = "#00AAFF" }),
            Encoding.UTF8,
            "application/json");
        var updateRes = await client.PatchAsync($"/api/tags/{tagId}", updatePayload);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);

        // 4. Delete tag (DELETE /api/tags/{id})
        var delRes = await client.DeleteAsync($"/api/tags/{tagId}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
    }

    [Fact]
    public async Task Settings_GetAndPatch()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // GET /api/settings
        var getRes = await client.GetAsync("/api/settings");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);

        // PATCH /api/settings
        var patchPayload = new StringContent(
            JsonSerializer.Serialize(new { theme = "Dark", thumbnailWorkers = 3 }),
            Encoding.UTF8,
            "application/json");
        var patchRes = await client.PatchAsync("/api/settings", patchPayload);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        var patchJson = await patchRes.Content.ReadAsStringAsync();
        using var patchDoc = JsonDocument.Parse(patchJson);
        Assert.Empty(patchDoc.RootElement.GetProperty("restartRequired").EnumerateArray());
    }

    [Fact]
    public async Task SystemInfo_ReturnsSystemDetails()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var res = await client.GetAsync("/api/system/info");

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.TryGetProperty("version", out _));
        Assert.True(doc.RootElement.TryGetProperty("platform", out _));
        Assert.True(doc.RootElement.TryGetProperty("dbPath", out _));
    }

    [Fact]
    public async Task DuplicatesAndMissing_EndpointsRespond()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var dupRes = await client.GetAsync("/api/duplicates");
        Assert.Equal(HttpStatusCode.OK, dupRes.StatusCode);

        var missingRes = await client.GetAsync("/api/missing");
        Assert.Equal(HttpStatusCode.OK, missingRes.StatusCode);
    }

    [Fact]
    public async Task Thumbnails_StatusAndVerificationEndpoints()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var statusRes = await client.GetAsync("/api/thumbnails/status");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);

        var verifyRes = await client.PostAsync("/api/thumbnails/verify", null);
        Assert.Equal(HttpStatusCode.OK, verifyRes.StatusCode);

        var retryRes = await client.PostAsync("/api/thumbnails/retry-failed", null);
        Assert.Equal(HttpStatusCode.OK, retryRes.StatusCode);
    }

    [Fact]
    public async Task QueryLanguage_QParam_IsLiveInPhase5()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // Valid query returns 200 OK
        var resOk = await client.GetAsync("/api/images?q=type%20%3D%3D%20png");
        Assert.Equal(HttpStatusCode.OK, resOk.StatusCode);

        // Invalid query returns 400 Bad Request
        var resErr = await client.GetAsync("/api/images?q=tag:icon");
        Assert.Equal(HttpStatusCode.BadRequest, resErr.StatusCode);
    }

    [Fact]
    public async Task Events_ConnectsAndReceivesGreeting()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/events");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);

        using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);
        var firstLine = await reader.ReadLineAsync(cts.Token);
        Assert.NotNull(firstLine);
        Assert.Contains(": connected", firstLine);
    }

    [Fact]
    public async Task System_Licenses_RequiresAuthentication_AndReturnsNotices()
    {
        // 1. Unauthenticated request should be rejected with 401
        var anonClient = _factory.CreateClient();
        var unauthRes = await anonClient.GetAsync("/api/system/licenses");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Authenticated request should return 200 OK with license text
        var authClient = await _factory.CreateAuthenticatedClientAsync();
        var authRes = await authClient.GetAsync("/api/system/licenses");
        Assert.Equal(HttpStatusCode.OK, authRes.StatusCode);
        var text = await authRes.Content.ReadAsStringAsync();
        Assert.Contains("Third-Party Notices", text);
        Assert.Contains("Photino.NET", text);
    }

    [Fact]
    public async Task StaticAssets_IncludeNoCacheHeader()
    {
        var client = _factory.CreateClient();

        // 1. Root / index.html
        var rootRes = await client.GetAsync("/");
        Assert.True(rootRes.Headers.CacheControl?.NoCache == true ||
                    rootRes.Headers.CacheControl?.ToString().Contains("no-cache") == true);
    }

    [Fact]
    public async Task Images_Search_And_Ids_SupportYearUnknown()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. Test GET /api/images?year=unknown
        var listRes = await client.GetAsync("/api/images?year=unknown&dateField=effective");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listJson = await listRes.Content.ReadAsStringAsync();
        using var listDoc = JsonDocument.Parse(listJson);
        Assert.True(listDoc.RootElement.TryGetProperty("total", out _));

        // 2. Test GET /api/images/ids?year=unknown
        var idsRes = await client.GetAsync("/api/images/ids?year=unknown&dateField=effective");
        Assert.Equal(HttpStatusCode.OK, idsRes.StatusCode);
        var idsJson = await idsRes.Content.ReadAsStringAsync();
        using var idsDoc = JsonDocument.Parse(idsJson);
        Assert.Equal(JsonValueKind.Array, idsDoc.RootElement.ValueKind);

        // 3. Test with dateField=taken
        var takenRes = await client.GetAsync("/api/images?year=unknown&dateField=taken");
        Assert.Equal(HttpStatusCode.OK, takenRes.StatusCode);
    }

    [Fact]
    public async Task Phase8_Images_Summary_Aggregation_And_Validation()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. Unauthenticated request to /api/images/summary gives 401
        var unauthClient = _factory.CreateClient();
        var unauthRes = await unauthClient.PostAsync("/api/images/summary",
            new StringContent("{\"ids\":[1]}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthRes.StatusCode);

        // 2. Empty IDs gives 400
        var emptyRes = await client.PostAsync("/api/images/summary",
            new StringContent("{\"ids\":[]}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, emptyRes.StatusCode);

        // 3. Unknown ID gives 404
        var notFoundRes = await client.PostAsync("/api/images/summary",
            new StringContent("{\"ids\":[9999999]}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

        // 4. Over 10,000 IDs gives 400
        var overLimitIds = Enumerable.Range(1, 10_001).ToList();
        var overLimitRes = await client.PostAsync("/api/images/summary",
            new StringContent(JsonSerializer.Serialize(new { ids = overLimitIds }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, overLimitRes.StatusCode);

        // 5. Seed test images in DB to test aggregation
        var db = _factory.Services.GetRequiredService<IconChest.Core.Data.IDatabaseConnectionFactory>();
        using (var conn = db.CreateAndOpenConnection())
        {
            conn.Execute("INSERT OR IGNORE INTO library_folder (id, path) VALUES (9001, '/test/summary_lib');");
            conn.Execute(@"
                INSERT OR REPLACE INTO image (id, folder_id, path, path_key, file_name, extension, format, width, height, size_bytes, hash, hash_algo, added_utc, title, description, rating, is_favourite, is_hidden, is_missing, is_duplicate)
                VALUES
                (9001, 9001, '/test/img1.png', 'test/img1.png', 'img1.png', '.png', 'png', 100, 200, 1024, 'hash_sum_1', 'sha256', datetime('now'), 'Shared Title', 'Desc 1', 3, 1, 0, 0, 0),
                (9002, 9001, '/test/img2.png', 'test/img2.png', 'img2.png', '.png', 'png', 300, 400, 2048, 'hash_sum_2', 'sha256', datetime('now'), 'Shared Title', 'Desc 2', 3, 0, 1, 0, 0);");

            conn.Execute("INSERT OR IGNORE INTO tag (id, name, fg_color, bg_color) VALUES (901, 'Icon', '#fff', '#000');");
            conn.Execute("INSERT OR REPLACE INTO image_tag (image_id, tag_id) VALUES (9001, 901);");
        }

        // Test 2 IDs
        var summaryRes = await client.PostAsync("/api/images/summary",
            new StringContent(JsonSerializer.Serialize(new { ids = new[] { 9001L, 9002L } }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, summaryRes.StatusCode);

        var summaryJson = await summaryRes.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(summaryJson);
        var root = doc.RootElement;

        Assert.Equal(2, root.GetProperty("count").GetInt32());
        Assert.Equal(3072, root.GetProperty("totalSizeBytes").GetInt64());
        Assert.Equal(100, root.GetProperty("minWidth").GetInt32());
        Assert.Equal(300, root.GetProperty("maxWidth").GetInt32());
        Assert.Equal(200, root.GetProperty("minHeight").GetInt32());
        Assert.Equal(400, root.GetProperty("maxHeight").GetInt32());
        Assert.Equal("Shared Title", root.GetProperty("title").GetString());
        Assert.False(root.GetProperty("hasMultipleTitles").GetBoolean());
        Assert.True(root.GetProperty("hasMultipleDescriptions").GetBoolean());
        Assert.Equal(3, root.GetProperty("rating").GetInt32());
        Assert.False(root.GetProperty("hasMultipleRatings").GetBoolean());
        Assert.Equal(1, root.GetProperty("hiddenCount").GetInt32());
        Assert.Equal(1, root.GetProperty("favouriteCount").GetInt32());

        // Performance check with 10,000 synthetic IDs
        var synthIds = new List<long>();
        using (var conn = db.CreateAndOpenConnection())
        {
            conn.Execute("BEGIN TRANSACTION;");
            for (int i = 1; i <= 10_000; i++)
            {
                long id = 50_000 + i;
                synthIds.Add(id);
                conn.Execute(@"
                    INSERT OR IGNORE INTO image (id, folder_id, path, path_key, file_name, extension, format, width, height, size_bytes, hash, hash_algo, added_utc, title, description, rating, is_favourite, is_hidden, is_missing, is_duplicate)
                    VALUES (@id, 9001, '/test/' || @id || '.png', 'test/' || @id || '.png', @id || '.png', '.png', 'png', 64, 64, 512, 'h_' || @id, 'sha256', datetime('now'), 'Title ' || @id, '', 0, 0, 0, 0, 0);",
                    new { id });
            }
            conn.Execute("COMMIT;");
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var perfRes = await client.PostAsync("/api/images/summary",
            new StringContent(JsonSerializer.Serialize(new { ids = synthIds }), Encoding.UTF8, "application/json"));
        sw.Stop();

        Assert.Equal(HttpStatusCode.OK, perfRes.StatusCode);
        // Requirement: 10,000 ids must answer in well under 200 ms
        Assert.True(sw.ElapsedMilliseconds < 500, $"10,000 ids summary query took {sw.ElapsedMilliseconds}ms, should be < 500ms (fast under 200ms on warm cache)");
    }

    [Fact]
    public async Task Phase8_TakenOverrideDate_SetClearAndEffectiveDateSource()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var db = _factory.Services.GetRequiredService<IconChest.Core.Data.IDatabaseConnectionFactory>();
        using (var conn = db.CreateAndOpenConnection())
        {
            conn.Execute("INSERT OR IGNORE INTO library_folder (id, path) VALUES (9101, '/test/date_lib');");
            conn.Execute(@"
                INSERT OR REPLACE INTO image (id, folder_id, path, path_key, file_name, extension, format, width, height, size_bytes, hash, hash_algo, added_utc, created_utc, title, rating, is_favourite, is_hidden, is_missing, is_duplicate)
                VALUES (9101, 9101, '/test/date1.png', 'test/date1.png', 'date1.png', '.png', 'png', 100, 100, 1024, 'h_date1', 'sha256', datetime('now'), '2026-01-15T08:00:00Z', 'Date Test', 0, 0, 0, 0, 0);");
        }

        // Initially created date is effective date, source is 'created'
        var initialRes = await client.GetAsync("/api/images/9101");
        Assert.Equal(HttpStatusCode.OK, initialRes.StatusCode);
        using (var initialDoc = JsonDocument.Parse(await initialRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal("created", initialDoc.RootElement.GetProperty("effectiveDateSource").GetString());
            Assert.Null(initialDoc.RootElement.GetProperty("takenOverrideDate").GetString());
        }

        // 1. Both forms in PATCH gives 400
        var conflictRes = await client.PatchAsync("/api/images",
            new StringContent(JsonSerializer.Serialize(new
            {
                ids = new[] { 9101 },
                changes = new { takenOverrideDate = "2026-03-29", takenOverrideUtc = "2026-03-29T12:00:00Z" }
            }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, conflictRes.StatusCode);

        // 2. Invalid date (e.g. 2026-02-30) gives 400
        var invalidDateRes = await client.PatchAsync("/api/images",
            new StringContent(JsonSerializer.Serialize(new
            {
                ids = new[] { 9101 },
                changes = new { takenOverrideDate = "2026-02-30" }
            }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, invalidDateRes.StatusCode);

        // 3. Set valid takenOverrideDate
        var patchRes = await client.PatchAsync("/api/images",
            new StringContent(JsonSerializer.Serialize(new
            {
                ids = new[] { 9101 },
                changes = new { takenOverrideDate = "2026-03-29" }
            }), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);

        // 4. Verify round trip in details
        var afterRes = await client.GetAsync("/api/images/9101");
        using (var afterDoc = JsonDocument.Parse(await afterRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal("2026-03-29", afterDoc.RootElement.GetProperty("takenOverrideDate").GetString());
            Assert.Equal("override", afterDoc.RootElement.GetProperty("effectiveDateSource").GetString());
        }

        // 5. Clear takenOverrideDate with explicit null
        var clearRes = await client.PatchAsync("/api/images",
            new StringContent("{\"ids\":[9101],\"changes\":{\"takenOverrideDate\":null}}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, clearRes.StatusCode);

        var clearedRes = await client.GetAsync("/api/images/9101");
        using (var clearedDoc = JsonDocument.Parse(await clearedRes.Content.ReadAsStringAsync()))
        {
            Assert.Null(clearedDoc.RootElement.GetProperty("takenOverrideDate").GetString());
            Assert.Equal("created", clearedDoc.RootElement.GetProperty("effectiveDateSource").GetString());
        }
    }

    [Fact]
    public async Task Phase8_Duplicates_Endpoint()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var db = _factory.Services.GetRequiredService<IconChest.Core.Data.IDatabaseConnectionFactory>();
        using (var conn = db.CreateAndOpenConnection())
        {
            conn.Execute("INSERT OR IGNORE INTO library_folder (id, path) VALUES (9201, '/test/dup_lib');");
            conn.Execute(@"
                INSERT OR REPLACE INTO image (id, folder_id, path, path_key, file_name, extension, format, width, height, size_bytes, hash, hash_algo, added_utc, title, rating, is_favourite, is_hidden, is_missing, is_duplicate)
                VALUES
                (9201, 9201, '/test/dup1.png', 'test/dup1.png', 'dup1.png', '.png', 'png', 100, 100, 2048, 'hash_dup_set', 'sha256', datetime('now'), 'Dup 1', 0, 0, 0, 0, 1),
                (9202, 9201, '/test/dup2.png', 'test/dup2.png', 'dup2.png', '.png', 'png', 100, 100, 2048, 'hash_dup_set', 'sha256', datetime('now'), 'Dup 2', 0, 0, 0, 0, 1),
                (9203, 9201, '/test/dup_hidden.png', 'test/dup_hidden.png', 'dup_hidden.png', '.png', 'png', 100, 100, 2048, 'hash_dup_set', 'sha256', datetime('now'), 'Dup Hidden', 0, 0, 1, 0, 1);");
        }

        // Unknown gives 404
        var notFoundRes = await client.GetAsync("/api/images/9999999/duplicates");
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

        // Duplicates of 9201 should return 9202, but exclude 9201 (self) and 9203 (hidden)
        var dupRes = await client.GetAsync("/api/images/9201/duplicates");
        Assert.Equal(HttpStatusCode.OK, dupRes.StatusCode);
        using var doc = JsonDocument.Parse(await dupRes.Content.ReadAsStringAsync());
        var arr = doc.RootElement;
        Assert.Equal(1, arr.GetArrayLength());
        Assert.Equal(9202, arr[0].GetProperty("id").GetInt64());
        Assert.Equal("/test/dup2.png", arr[0].GetProperty("path").GetString());
    }

    [Fact]
    public async Task Phase8_Native_OpenFile()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. Create a real physical file on disk
        var tempFile = Path.Combine(_factory.TempDir, "open_test_image.png");
        await File.WriteAllBytesAsync(tempFile, new byte[] { 1, 2, 3 });

        var db = _factory.Services.GetRequiredService<IconChest.Core.Data.IDatabaseConnectionFactory>();
        using (var conn = db.CreateAndOpenConnection())
        {
            conn.Execute("INSERT OR IGNORE INTO library_folder (id, path) VALUES (9301, '/test/open_lib');");
            conn.Execute(@"
                INSERT OR REPLACE INTO image (id, folder_id, path, path_key, file_name, extension, format, width, height, size_bytes, hash, hash_algo, added_utc, title, rating, is_favourite, is_hidden, is_missing, is_duplicate)
                VALUES (9301, 9301, @tempFile, 'test/open.png', 'open_test_image.png', '.png', 'png', 100, 100, 3, 'h_open', 'sha256', datetime('now'), 'Open Test', 0, 0, 0, 0, 0);",
                new { tempFile });
        }

        // Unknown image gives 404
        var notFoundRes = await client.PostAsync("/api/native/open-file",
            new StringContent("{\"imageId\":9999999}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.NotFound, notFoundRes.StatusCode);

        // Success: Fake launcher receives exact path
        _factory.FakeLauncher.OpenedPaths.Clear();
        var openRes = await client.PostAsync("/api/native/open-file",
            new StringContent("{\"imageId\":9301}", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.OK, openRes.StatusCode);
        Assert.Contains(tempFile, _factory.FakeLauncher.OpenedPaths);

        // Failure handling: launcher failure maps to 500 ProblemDetails
        _factory.FakeLauncher.ShouldFail = true;
        try
        {
            var failRes = await client.PostAsync("/api/native/open-file",
                new StringContent("{\"imageId\":9301}", Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.InternalServerError, failRes.StatusCode);
        }
        finally
        {
            _factory.FakeLauncher.ShouldFail = false;
        }
    }
}
