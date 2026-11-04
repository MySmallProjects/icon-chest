using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IconChest.Api.Endpoints;
using IconChest.Api.Tests.TestHelpers;
using IconChest.Core.Data;
using IconChest.Core.Tags;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace IconChest.Api.Tests;

[Collection("ApiCollection")]
public sealed class Phase10ApiTests
{
    private readonly IconChestApiFactory _factory;

    public Phase10ApiTests(IconChestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task DbGateMiddleware_Returns503WithRetryAfter_WhenMaintenanceActive()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var dbGate = _factory.Services.GetRequiredService<IDbGate>();

        // 1. Normal request succeeds
        var okRes = await client.GetAsync("/api/folders");
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);

        // 2. Enter maintenance
        using (var lease = await dbGate.EnterMaintenanceAsync(TimeSpan.FromSeconds(5)))
        {
            var mRes = await client.GetAsync("/api/folders");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, mRes.StatusCode);
            Assert.Equal("5", mRes.Headers.GetValues("Retry-After").FirstOrDefault());
            Assert.Equal("application/problem+json", mRes.Content.Headers.ContentType?.MediaType);

            var problem = await mRes.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Contains("maintenance", problem.GetProperty("type").GetString());
        }

        // 3. Normal request succeeds again
        var okRes2 = await client.GetAsync("/api/folders");
        Assert.Equal(HttpStatusCode.OK, okRes2.StatusCode);
    }

    [Fact]
    public async Task DuplicatesEndpoints_SummaryAndIds_ReturnExpectedStructure()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. GET /api/duplicates/summary
        var summaryRes = await client.GetAsync("/api/duplicates/summary?sort=reclaimable&dir=desc");
        Assert.Equal(HttpStatusCode.OK, summaryRes.StatusCode);

        var summary = await summaryRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(summary.TryGetProperty("groupCount", out _));
        Assert.True(summary.TryGetProperty("imageCount", out _));
        Assert.True(summary.TryGetProperty("reclaimableBytes", out _));
        Assert.True(summary.TryGetProperty("groupSizes", out _));

        // 2. GET /api/duplicates/ids
        var idsRes = await client.GetAsync("/api/duplicates/ids?sort=reclaimable&dir=desc");
        Assert.Equal(HttpStatusCode.OK, idsRes.StatusCode);
        var ids = await idsRes.Content.ReadFromJsonAsync<List<long>>();
        Assert.NotNull(ids);
    }

    [Fact]
    public async Task MissingEndpoints_RemoveAndOrphansCleanup()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. GET /api/missing with sort
        var missingRes = await client.GetAsync("/api/missing?sort=name&dir=asc");
        Assert.Equal(HttpStatusCode.OK, missingRes.StatusCode);

        // 2. POST /api/missing/remove
        var removeRes = await client.PostAsJsonAsync("/api/missing/remove", new RemoveMissingRequest());
        Assert.Equal(HttpStatusCode.OK, removeRes.StatusCode);
        var removeJson = await removeRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(removeJson.TryGetProperty("removedCount", out _));

        // 3. POST /api/thumbnails/cleanup-orphans
        var orphansRes = await client.PostAsync("/api/thumbnails/cleanup-orphans", null);
        Assert.Equal(HttpStatusCode.OK, orphansRes.StatusCode);
        var orphansJson = await orphansRes.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(orphansJson.TryGetProperty("cleanedCount", out _));
    }

    [Fact]
    public async Task DataEndpoints_BackupInspectRestoreAndDownload()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. GET /api/data/info
        var infoRes = await client.GetAsync("/api/data/info");
        Assert.Equal(HttpStatusCode.OK, infoRes.StatusCode);
        var info = await infoRes.Content.ReadFromJsonAsync<DataInfo>();
        Assert.NotNull(info);
        Assert.NotNull(info.DbPath);

        // 2. POST /api/data/backup
        var backupRes = await client.PostAsJsonAsync("/api/data/backup", new BackupRequest());
        Assert.Equal(HttpStatusCode.OK, backupRes.StatusCode);
        var backupResult = await backupRes.Content.ReadFromJsonAsync<BackupResult>();
        Assert.NotNull(backupResult);
        var backupFileName = Path.GetFileName(backupResult.Path);

        // 3. GET /api/data/backups/{name}/download
        var dlRes = await client.GetAsync($"/api/data/backups/{backupFileName}/download");
        Assert.Equal(HttpStatusCode.OK, dlRes.StatusCode);
        Assert.Equal("application/x-sqlite3", dlRes.Content.Headers.ContentType?.MediaType);

        // Traversal attempt should 404
        var traversalRes = await client.GetAsync("/api/data/backups/..%2F..%2Fsecret.txt/download");
        Assert.Equal(HttpStatusCode.NotFound, traversalRes.StatusCode);

        // 4. POST /api/data/restore/inspect
        var inspectRes = await client.PostAsJsonAsync("/api/data/restore/inspect", new RestoreInspectRequest(Name: backupFileName));
        Assert.Equal(HttpStatusCode.OK, inspectRes.StatusCode);
        var report = await inspectRes.Content.ReadFromJsonAsync<BackupInspectionReport>();
        Assert.NotNull(report);
        Assert.True(report.IsSupported);

        // 5. POST /api/data/restore without confirm -> 400
        var unconfirmedRes = await client.PostAsJsonAsync("/api/data/restore", new RestoreRequest(Name: backupFileName, Confirm: false));
        Assert.Equal(HttpStatusCode.BadRequest, unconfirmedRes.StatusCode);

        // With confirm -> 200
        var restoreRes = await client.PostAsJsonAsync("/api/data/restore", new RestoreRequest(Name: backupFileName, Confirm: true));
        Assert.Equal(HttpStatusCode.OK, restoreRes.StatusCode);
        var summary = await restoreRes.Content.ReadFromJsonAsync<RestoreSummary>();
        Assert.NotNull(summary);
    }

    [Fact]
    public async Task TagExportImport_RoundTripAndMerge()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. GET /api/tags/export
        var exportRes = await client.GetAsync("/api/tags/export");
        Assert.Equal(HttpStatusCode.OK, exportRes.StatusCode);
        var doc = await exportRes.Content.ReadFromJsonAsync<TagExportDocument>();
        Assert.NotNull(doc);
        Assert.Equal("iconchest-tags", doc.Format);

        // 2. POST /api/tags/import
        var importDoc = new TagExportDocument(
            Format: "iconchest-tags",
            Version: 1,
            ExportedUtc: DateTime.UtcNow.ToString("O"),
            Tags: new List<TagExportItem>
            {
                new("ApiImportTag1", "#FFFFFF", "#1E40AF"),
                new("ApiImportTag2", "#000000", "#F59E0B")
            });

        var importRes = await client.PostAsJsonAsync("/api/tags/import", importDoc);
        Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);
        var result = await importRes.Content.ReadFromJsonAsync<TagImportResult>();
        Assert.NotNull(result);
        Assert.True(result.AddedCount >= 0);
    }

    [Fact]
    public async Task NativeOpenFolder_WhitelistedTargets_WorksForLoopback()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 1. Whitelisted target 'logs'
        var res = await client.PostAsJsonAsync("/api/native/open-folder", new OpenFolderRequest { Target = "logs" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains(_factory.FakeLauncher.OpenedPaths, p => p.Contains("logs"));

        // 2. Non-whitelisted target -> 400
        var badRes = await client.PostAsJsonAsync("/api/native/open-folder", new OpenFolderRequest { Target = "/etc/passwd" });
        Assert.Equal(HttpStatusCode.BadRequest, badRes.StatusCode);
    }

    [Fact]
    public async Task SystemInfo_ContainsLanUrlsAndCanRestart()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync("/api/system/info");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.TryGetProperty("lanUrls", out var lanUrlsProp));
        Assert.Equal(JsonValueKind.Array, lanUrlsProp.ValueKind);
        Assert.True(json.TryGetProperty("canRestart", out var canRestartProp));
        Assert.True(canRestartProp.ValueKind == JsonValueKind.True || canRestartProp.ValueKind == JsonValueKind.False);
    }
}

