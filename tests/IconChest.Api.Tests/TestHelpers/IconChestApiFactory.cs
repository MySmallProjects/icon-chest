using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace IconChest.Api.Tests.TestHelpers;

[CollectionDefinition("ApiCollection")]
public class ApiCollection : ICollectionFixture<IconChestApiFactory>
{
}

public class IconChestApiFactory : WebApplicationFactory<Program>
{
    public const string DefaultSecret = "test-secret-12345";
    public string TempDir { get; }
    public string ConfigDir { get; }
    public string DataDir { get; }

    public IconChestApiFactory()
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"api-factory-{Guid.NewGuid():N}");
        ConfigDir = Path.Combine(TempDir, "config");
        DataDir = Path.Combine(TempDir, "data");
        Directory.CreateDirectory(ConfigDir);
        Directory.CreateDirectory(DataDir);

        Environment.SetEnvironmentVariable("ICONCHEST_CONFIG_DIR", ConfigDir);
        Environment.SetEnvironmentVariable("ICONCHEST_DATA_DIR", DataDir);
        Environment.SetEnvironmentVariable("ICONCHEST_API_SECRET", DefaultSecret);
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
    }

    public async Task<HttpClient> CreateAuthenticatedClientAsync(string secret = DefaultSecret)
    {
        var throttler = Services.GetService(typeof(IconChest.Api.Security.ILoginThrottler)) as IconChest.Api.Security.ILoginThrottler;
        throttler?.Reset();

        var client = CreateClient();
        var content = new StringContent(
            JsonSerializer.Serialize(new { secret }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/api/session", content);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException($"Failed to authenticate: {response.StatusCode}");
        }

        if (response.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
        {
            var cookie = cookieHeaders.FirstOrDefault(c => c.StartsWith("iconchest_session=", StringComparison.OrdinalIgnoreCase));
            if (cookie != null)
            {
                var cookieVal = cookie.Split(';')[0];
                client.DefaultRequestHeaders.Add("Cookie", cookieVal);
            }
        }

        return client;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            if (Directory.Exists(TempDir))
            {
                try { Directory.Delete(TempDir, recursive: true); } catch { }
            }
        }
    }
}
