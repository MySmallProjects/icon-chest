using System.Net;
using System.Text;
using System.Text.Json;
using IconChest.Api.Tests.TestHelpers;
using Xunit;

namespace IconChest.Api.Tests;

[Collection("ApiCollection")]
public class AuthAndSecurityTests
{
    private readonly IconChestApiFactory _factory;

    public AuthAndSecurityTests(IconChestApiFactory factory)
    {
        _factory = factory;
    }

    [Theory]
    [InlineData("/api/system/info")]
    [InlineData("/api/settings")]
    [InlineData("/api/folders")]
    [InlineData("/api/tags")]
    [InlineData("/api/images")]
    [InlineData("/api/duplicates")]
    [InlineData("/api/missing")]
    [InlineData("/api/thumbnails/status")]
    [InlineData("/api/events")]
    public async Task ProtectedEndpoints_Return401_WhenUnauthenticated(string path)
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Session_LoginWithValidSecret_Returns200AndCookie()
    {
        var client = _factory.CreateClient();
        var content = new StringContent(
            JsonSerializer.Serialize(new { secret = IconChestApiFactory.DefaultSecret }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/session", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var sessionCookie = cookies.FirstOrDefault(c => c.StartsWith("iconchest_session="));
        Assert.NotNull(sessionCookie);
        Assert.Contains("HttpOnly", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SameSite=Strict", sessionCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Path=/", sessionCookie, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Session_GetSession_ReturnsCurrentState()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.GetAsync("/api/session");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        Assert.True(doc.RootElement.GetProperty("authenticated").GetBoolean());
    }

    [Fact]
    public async Task Session_DeleteSession_LogsOut()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var response = await client.DeleteAsync("/api/session");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Verification: cookie cleared
        Assert.True(response.Headers.TryGetValues("Set-Cookie", out var cookies));
        var sessionCookie = cookies.FirstOrDefault(c => c.StartsWith("iconchest_session="));
        Assert.NotNull(sessionCookie);
        Assert.True(sessionCookie.Contains("expires=Thu, 01 Jan 1970", StringComparison.OrdinalIgnoreCase) ||
                    sessionCookie.Contains("max-age=0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task HostHeader_ForeignHost_RejectedWith400()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Host", "attacker.com");

        var response = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OriginHeader_ForeignOriginOnPost_RejectedWith403()
    {
        var client = await _factory.CreateAuthenticatedClientAsync();
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/folders")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", "http://evil-site.com");

        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SecurityHeaders_PresentOnResponses()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        Assert.True(response.Headers.Contains("X-Content-Type-Options"));
        Assert.True(response.Headers.Contains("X-Frame-Options"));
        Assert.True(response.Headers.Contains("Referrer-Policy"));
    }

    [Fact]
    public async Task LoginThrottling_FiveFailures_Triggers429WithRetryAfter()
    {
        var throttler = _factory.Services.GetService(typeof(IconChest.Api.Security.ILoginThrottler)) as IconChest.Api.Security.ILoginThrottler;
        throttler?.Reset();
        try
        {
            var client = _factory.CreateClient();
            var content = new StringContent(
                JsonSerializer.Serialize(new { secret = "wrong-secret-fail" }),
                Encoding.UTF8,
                "application/json");

            // First 5 attempts fail with 401
            for (int i = 0; i < 5; i++)
            {
                var res = await client.PostAsync("/api/session", content);
                Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
            }

            // 6th attempt is throttled to 429
            var throttledRes = await client.PostAsync("/api/session", content);
            Assert.Equal(HttpStatusCode.TooManyRequests, throttledRes.StatusCode);
            Assert.True(throttledRes.Headers.Contains("Retry-After"));
        }
        finally
        {
            throttler?.Reset();
        }
    }
}
