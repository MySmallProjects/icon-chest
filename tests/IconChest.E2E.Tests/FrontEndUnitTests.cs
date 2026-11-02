using Microsoft.Playwright;
using Xunit;

namespace IconChest.E2E.Tests;

[Trait("Category", "E2E")]
[Collection("E2ECollection")]
public class FrontEndUnitTests
{
    private readonly E2EFixture _fixture;

    public FrontEndUnitTests(E2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("chromium")]
    [InlineData("webkit")]
    public async Task JavaScriptModules_ResizerClampMaths_EvaluatesCorrectly(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync(_fixture.ApiUri.ToString());

            // Evaluate clampWidth function from /js/lib/resizer.js
            var result = await page.EvaluateAsync<int>(@"
                async () => {
                    const { clampWidth, MIN_PANEL_A_WIDTH, MAX_PANEL_A_WIDTH } = await import('/js/lib/resizer.js');
                    const w1 = clampWidth(100, 1920); // Clamped to min 180
                    const w2 = clampWidth(800, 1920); // Clamped to max 600
                    const w3 = clampWidth(350, 600);  // 50% window width = 300, clamped to 300
                    return (w1 === 180 && w2 === 600 && w3 === 300) ? 1 : 0;
                }
            ");

            Assert.Equal(1, result);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task JavaScriptModules_StoreReactivity_UpdatesAndNotifies(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync(_fixture.ApiUri.ToString());

            var result = await page.EvaluateAsync<bool>(@"
                async () => {
                    const { store } = await import('/js/lib/store.js');
                    let notifiedVal = null;
                    const unsub = store.subscribe('testKey', (val) => {
                        notifiedVal = val;
                    });
                    store.set('testKey', 'hello-iconchest');
                    unsub();
                    return notifiedVal === 'hello-iconchest';
                }
            ");

            Assert.True(result);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task JavaScriptModules_ApiErrorParsing_HandlesProblemDetails(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync(_fixture.ApiUri.ToString());

            var result = await page.EvaluateAsync<bool>(@"
                async () => {
                    const { ApiError } = await import('/js/lib/api.js');
                    const err = new ApiError(400, 'Bad Request', 'Validation failed', { path: 'Path required' }, 'trace-123');
                    return err.status === 400 &&
                           err.title === 'Bad Request' &&
                           err.errors.path === 'Path required' &&
                           err.traceId === 'trace-123';
                }
            ");

            Assert.True(result);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }
}
