using System.Net;
using IconChest.Api.Tests.TestHelpers;
using Xunit;

namespace IconChest.Api.Tests;

[Collection("ApiCollection")]
public class HealthEndpointTests
{
    private readonly IconChestApiFactory _factory;

    public HealthEndpointTests(IconChestApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetHealth_ReturnsOk()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("ok", content);
    }
}
