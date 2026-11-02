using System.Net;
using System.Text;
using System.Text.Json;
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
}
