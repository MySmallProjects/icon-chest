using System.Diagnostics;
using IconChest.Desktop;
using Microsoft.Playwright;
using Xunit;

namespace IconChest.E2E.Tests;

[CollectionDefinition("E2ECollection")]
public class E2ECollectionDefinition : ICollectionFixture<E2EFixture>
{
}

public class E2EFixture : IAsyncLifetime
{
    public string TempDir { get; private set; } = null!;
    public ApiProcessManager ApiManager { get; private set; } = null!;
    public Uri ApiUri { get; private set; } = null!;
    public string Secret { get; private set; } = null!;
    public IPlaywright? PlaywrightInstance { get; private set; }

    public async Task InitializeAsync()
    {
        TempDir = Path.Combine(Path.GetTempPath(), "iconchest_e2e_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TempDir);

        ApiManager = new ApiProcessManager(readinessTimeout: TimeSpan.FromSeconds(20));
        Secret = SecretGenerator.GenerateSecret();

        var env = new Dictionary<string, string>
        {
            ["ICONCHEST_CONFIG_DIR"] = TempDir,
            ["ICONCHEST_DATA_DIR"] = TempDir
        };

        // Start API server
        ApiUri = await ApiManager.StartAsync(secret: Secret, environmentVariables: env);

        try
        {
            PlaywrightInstance = await Playwright.CreateAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to initialize Playwright: {0}", ex.Message);
        }
    }

    public async Task<(IBrowser? Browser, string? SkipReason)> TryLaunchBrowserAsync(string browserType = "chromium")
    {
        if (PlaywrightInstance == null)
        {
            return (null, "Playwright driver could not be initialized.");
        }

        try
        {
            var browserTypeInstance = browserType.ToLowerInvariant() switch
            {
                "webkit" => PlaywrightInstance.Webkit,
                _ => PlaywrightInstance.Chromium
            };

            var browser = await browserTypeInstance.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true
            });

            return (browser, null);
        }
        catch (PlaywrightException ex) when (ex.Message.Contains("Executable doesn't exist") || ex.Message.Contains("not found"))
        {
            return (null, $"Playwright browser '{browserType}' is not installed. Run 'playwright install' or pwsh install script.");
        }
        catch (Exception ex)
        {
            return (null, $"Could not launch browser '{browserType}': {ex.Message}");
        }
    }

    public async Task DisposeAsync()
    {
        if (PlaywrightInstance != null)
        {
            PlaywrightInstance.Dispose();
            PlaywrightInstance = null;
        }

        if (ApiManager != null)
        {
            await ApiManager.StopAsync();
        }

        Environment.SetEnvironmentVariable("ICONCHEST_CONFIG_DIR", null);
        Environment.SetEnvironmentVariable("ICONCHEST_DATA_DIR", null);

        if (Directory.Exists(TempDir))
        {
            try { Directory.Delete(TempDir, true); } catch {}
        }
    }
}
