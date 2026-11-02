using System.Net;
using System.Text;
using System.Text.Json;
using IconChest.Api.Tests.TestHelpers;
using Xunit;

namespace IconChest.Api.Tests;

[Collection("ApiCollection")]
public class QueryApiTests
{
    private readonly IconChestApiFactory _factory;

    public QueryApiTests(IconChestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task QueryEndpoints_RequireAuthentication()
    {
        var unauthenticatedClient = _factory.CreateClient();

        // 1. GET /api/query/help
        var helpRes = await unauthenticatedClient.GetAsync("/api/query/help");
        Assert.Equal(HttpStatusCode.Unauthorized, helpRes.StatusCode);

        // 2. POST /api/query/validate
        var valPayload = new StringContent(
            JsonSerializer.Serialize(new { text = "favourite" }),
            Encoding.UTF8,
            "application/json");
        var valRes = await unauthenticatedClient.PostAsync("/api/query/validate", valPayload);
        Assert.Equal(HttpStatusCode.Unauthorized, valRes.StatusCode);

        // 3. POST /api/query/from-filter
        var filterPayload = new StringContent(
            JsonSerializer.Serialize(new { types = new[] { "png" } }),
            Encoding.UTF8,
            "application/json");
        var filterRes = await unauthenticatedClient.PostAsync("/api/query/from-filter", filterPayload);
        Assert.Equal(HttpStatusCode.Unauthorized, filterRes.StatusCode);
    }

    [Fact]
    public async Task ValidateEndpoint_ReturnsValidAndNormalized_WhenQueryValid()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var payload = new StringContent(
            JsonSerializer.Serialize(new { text = "  ( TYPE = PNG || TYPE = JPG ) && RATING >= 4 " }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/query/validate", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("valid").GetBoolean());
        Assert.Equal(0, root.GetProperty("errors").GetArrayLength());
        Assert.False(root.GetProperty("mentionsHidden").GetBoolean());
        Assert.False(root.GetProperty("mentionsMissing").GetBoolean());
        Assert.Equal("(type == png or type == jpg) and rating >= 4", root.GetProperty("normalized").GetString());
    }

    [Fact]
    public async Task ValidateEndpoint_ReturnsErrors_WhenQueryInvalid()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var payload = new StringContent(
            JsonSerializer.Serialize(new { text = "tpye == png" }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/query/validate", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.GetProperty("valid").GetBoolean());
        var errors = root.GetProperty("errors");
        Assert.True(errors.GetArrayLength() > 0);
        Assert.Equal("unknown_field", errors[0].GetProperty("code").GetString());
        Assert.Equal("type", errors[0].GetProperty("suggestion").GetString());
    }

    [Fact]
    public async Task HelpEndpoint_ReturnsFullHelpModel()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/query/help");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("fields").GetArrayLength() >= 20);
        Assert.True(root.GetProperty("errorCodes").GetArrayLength() >= 24);
        Assert.True(root.GetProperty("examples").GetArrayLength() >= 30);
        Assert.Equal(2000, root.GetProperty("limits").GetProperty("maxQueryLength").GetInt32());
    }

    [Fact]
    public async Task FromFilterEndpoint_ConvertsStructuredFilter()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var payload = new StringContent(
            JsonSerializer.Serialize(new
            {
                types = new[] { "png", "svg" },
                ratingMin = 4,
                favourite = true
            }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/query/from-filter", payload);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.GetProperty("success").GetBoolean());
        var text = root.GetProperty("text").GetString();
        Assert.NotNull(text);
        Assert.Contains("type in (png, svg)", text);
        Assert.Contains("rating >= 4", text);
        Assert.Contains("favourite", text);
    }

    [Fact]
    public async Task SearchImages_WithQ_Succeeds()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/images?q=type%20%3D%3D%20png&limit=10&offset=0&sort=filename&dir=asc");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.True(root.TryGetProperty("total", out _));
        Assert.True(root.TryGetProperty("items", out _));
    }

    [Fact]
    public async Task SearchImages_WithInvalidQ_Returns400WithQueryErrors()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/images?q=tpye%20%3D%3D%20png");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("https://tools.ietf.org/html/rfc7807#status-400", root.GetProperty("type").GetString());
        Assert.True(root.TryGetProperty("queryErrors", out var queryErrors));
        Assert.True(queryErrors.GetArrayLength() > 0);
        Assert.Equal("unknown_field", queryErrors[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task SearchImages_WithQAndOtherFilter_Returns400BadRequest()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        // 'q' combined with structured 'ratingMin'
        var response = await client.GetAsync("/api/images?q=type%20%3D%3D%20png&ratingMin=4");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("cannot be combined", json);
    }

    [Fact]
    public async Task GetImageIds_WithQ_Succeeds()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();

        var response = await client.GetAsync("/api/images/ids?q=favourite");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
    }
}

