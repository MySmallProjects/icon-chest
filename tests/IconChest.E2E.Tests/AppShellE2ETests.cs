using Microsoft.Playwright;
using Xunit;

namespace IconChest.E2E.Tests;

[Trait("Category", "E2E")]
[Collection("E2ECollection")]
public class AppShellE2ETests
{
    private readonly E2EFixture _fixture;

    public AppShellE2ETests(E2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("chromium")]
    public async Task AppShell_BootAndAuth_ViaHandoffSecret_LogsInAndStripsHash(string browserType)
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
            var consoleErrors = new List<string>();
            page.Console += (_, msg) =>
            {
                if (msg.Type == "error") consoleErrors.Add(msg.Text);
            };

            // Navigate using handoff URL fragment
            var handoffUrl = $"{_fixture.ApiUri}#handoff={_fixture.Secret}";
            await page.GotoAsync(handoffUrl);

            // Wait for icon bar to render
            await page.WaitForSelectorAsync("#icon-bar");

            // Verify hash fragment was removed from URL
            Assert.DoesNotContain("handoff=", page.Url);

            // Verify no console errors
            Assert.Empty(consoleErrors);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task AppShell_IconBar_ContainsAllEightIconsInOrder(string browserType)
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
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            var expected = new[] { "Folders", "Years", "Tags", "Favourites", "Duplicates", "Missing", "Help", "Settings" };
            var buttons = await page.QuerySelectorAllAsync(".icon-bar-btn");

            Assert.Equal(expected.Length, buttons.Count);
            for (int i = 0; i < expected.Length; i++)
            {
                var label = await buttons[i].GetAttributeAsync("aria-label");
                Assert.Equal(expected[i], label);
            }
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task AppShell_Layout_PanelAVisibilityTogglesByView(string browserType)
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
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            // On #folders: Panel A should be visible
            await page.ClickAsync("#nav-btn-folders");
            var panelA = await page.QuerySelectorAsync("#panel-a");
            Assert.NotNull(panelA);
            var isHiddenFolders = await panelA.GetAttributeAsync("hidden");
            Assert.Null(isHiddenFolders);

            // On #settings: Panel A should be hidden
            await page.ClickAsync("#nav-btn-settings");
            var isHiddenSettings = await panelA.GetAttributeAsync("hidden");
            Assert.NotNull(isHiddenSettings);

            // Return to #folders: Panel A returns
            await page.ClickAsync("#nav-btn-folders");
            var isHiddenReturn = await panelA.GetAttributeAsync("hidden");
            Assert.Null(isHiddenReturn);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task AppShell_Theme_TogglesAndPersists(string browserType)
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
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            // Navigate to settings appearance
            await page.GotoAsync($"{_fixture.ApiUri}#settings/appearance");
            await page.WaitForSelectorAsync("#setting-theme");

            // Select light theme
            await page.SelectOptionAsync("#setting-theme", "light");

            var htmlTheme = await page.GetAttributeAsync("html", "data-theme");
            Assert.Equal("light", htmlTheme);

            // Reload page and check that boot.js applied stored theme
            await page.ReloadAsync();
            var reloadedTheme = await page.GetAttributeAsync("html", "data-theme");
            Assert.Equal("light", reloadedTheme);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task AppShell_FileDrop_DoesNotNavigateAway(string browserType)
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
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            var initialUrl = page.Url;

            // Trigger drop event on document
            await page.EvaluateAsync(@"() => {
                const dropEvent = new DragEvent('drop', { cancelable: true, bubbles: true });
                document.dispatchEvent(dropEvent);
            }");

            Assert.Equal(initialUrl, page.Url);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }
}
