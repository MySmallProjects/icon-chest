using System.Net;
using IconChest.Api.Tests.TestHelpers;
using Xunit;

namespace IconChest.Api.Tests;

[Collection("ApiCollection")]
public class StaticHostingTests
{
    private readonly IconChestApiFactory _factory;

    public StaticHostingTests(IconChestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Root_ServesIndexHtml_WithNoCache()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("IconChest", content);

        var hasCacheControl = response.Headers.TryGetValues("Cache-Control", out var ccVals) ||
                              response.Content.Headers.TryGetValues("Cache-Control", out ccVals);
        Assert.True(hasCacheControl);
        var cacheControl = string.Join(", ", ccVals!);
        Assert.Contains("no-cache", cacheControl, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AppJs_ServesStaticJavaScript()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/app.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("IconChest", content);
    }

    [Fact]
    public async Task UnknownNonApiRoute_FallsBackToIndexHtml()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/folders/123/browser");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("IconChest", content);
    }

    [Fact]
    public async Task Dotfiles_RejectedWith404()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/.env");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
