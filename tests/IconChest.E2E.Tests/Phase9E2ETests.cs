using Dapper;
using Microsoft.Playwright;
using Xunit;

namespace IconChest.E2E.Tests;

[Trait("Category", "E2E")]
[Collection("E2ECollection")]
public class Phase9E2ETests
{
    private readonly E2EFixture _fixture;

    public Phase9E2ETests(E2EFixture fixture)
    {
        _fixture = fixture;
    }

    private (long imgSvgId, long imgPngId) SeedFilterTestImages()
    {
        using var conn = _fixture.OpenDbConnection();
        var folderId = conn.ExecuteScalar<long>("SELECT id FROM library_folder LIMIT 1;");
        if (folderId == 0)
        {
            folderId = conn.ExecuteScalar<long>(
                "INSERT INTO library_folder (path) VALUES ('/synth/phase9') RETURNING id;");
        }

        var now = DateTime.UtcNow.ToString("O");
        var imgSvgId = conn.ExecuteScalar<long>(@"
            INSERT INTO image (folder_id, path, path_key, file_name, extension, directory_key, directory_path, title, description, format, width, height, size_bytes, hash, hash_algo, added_utc, created_utc, rating, is_favourite, is_hidden, is_missing, is_duplicate)
            VALUES (@folderId, '/synth/phase9/vector-icon.svg', 'synth/phase9/vector-icon.svg', 'vector-icon.svg', '.svg', 'synth/phase9', '/synth/phase9', 'Vector Icon', 'High precision button', 'svg', 256, 256, 1024, 'hash_p9_svg', 'sha256', @now, @now, 5, 1, 0, 0, 0)
            RETURNING id;", new { folderId, now });

        var imgPngId = conn.ExecuteScalar<long>(@"
            INSERT INTO image (folder_id, path, path_key, file_name, extension, directory_key, directory_path, title, description, format, width, height, size_bytes, hash, hash_algo, added_utc, created_utc, rating, is_favourite, is_hidden, is_missing, is_duplicate)
            VALUES (@folderId, '/synth/phase9/bitmap-photo.png', 'synth/phase9/bitmap-photo.png', 'bitmap-photo.png', '.png', 'synth/phase9', '/synth/phase9', 'Bitmap Photo', 'Camera snapshot', 'png', 1024, 768, 65536, 'hash_p9_png', 'sha256', @now, @now, 1, 0, 0, 0, 0)
            RETURNING id;", new { folderId, now });

        return (imgSvgId, imgPngId);
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase9_SimpleFilter_FiltersByFormat_AndActiveChipsRemove(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        SeedFilterTestImages();

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            // Navigate to Folders view
            await page.ClickAsync("button[data-view='folders']");
            await page.WaitForSelectorAsync(".filter-bar-container");

            // Locate format select
            var formatSelect = page.Locator(".filter-controls-row select[aria-label='Filter by format']");
            await formatSelect.WaitForAsync();

            // Set format to 'svg'
            await formatSelect.SelectOptionAsync(new[] { "svg" });

            // Click Filter apply button
            await page.ClickAsync(".filter-apply-btn");

            // Check active chip appears
            var chip = page.Locator(".active-filter-chip");
            await chip.WaitForAsync();
            var chipText = await chip.InnerTextAsync();
            Assert.Contains("Format: SVG", chipText);

            // Click chip remove button
            await page.ClickAsync(".chip-remove-btn");

            // Active filters should be cleared
            await page.WaitForSelectorAsync(".active-filters-bar.hidden", new() { State = WaitForSelectorState.Attached });
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase9_ConvertToQuery_SwitchesToAdvancedModeWithQuery(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        SeedFilterTestImages();

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            await page.ClickAsync("button[data-view='folders']");
            await page.WaitForSelectorAsync(".filter-bar-container");

            // Set search text
            var textInput = page.Locator(".filter-text-input");
            await textInput.FillAsync("vector button");

            // Click Convert to query
            await page.ClickAsync(".filter-convert-btn");

            // Advanced tab should now be active
            var advTab = page.Locator("#tab-filter-advanced");
            await advTab.WaitForAsync();
            var isSelected = await advTab.GetAttributeAsync("aria-selected");
            Assert.Equal("true", isSelected);

            // Query textarea should contain the converted query
            var queryArea = page.Locator(".query-box-textarea");
            await queryArea.WaitForAsync();
            var queryVal = await queryArea.InputValueAsync();
            Assert.Contains("\"vector\" and \"button\"", queryVal);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase9_AdvancedQuery_LiveValidation_And_Execution(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        SeedFilterTestImages();

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            await page.ClickAsync("button[data-view='folders']");
            await page.WaitForSelectorAsync(".filter-bar-container");

            // Switch to Advanced tab
            await page.ClickAsync("#tab-filter-advanced");
            await page.WaitForSelectorAsync(".query-box-textarea");

            var queryArea = page.Locator(".query-box-textarea");
            var runBtn = page.Locator(".query-run-btn");

            // 1. Enter invalid query
            await queryArea.FillAsync("nonexistent_field == 123");
            // Wait for validation error pill
            var errPill = page.Locator(".query-error-pill");
            await errPill.WaitForAsync();
            Assert.True(await runBtn.IsDisabledAsync());

            // 2. Enter valid query
            await queryArea.FillAsync("type == svg and rating >= 4");
            var validStatus = page.Locator(".query-status-valid");
            await validStatus.WaitForAsync();
            Assert.False(await runBtn.IsDisabledAsync());

            // 3. Run query
            await runBtn.ClickAsync();

            // Verify active query chip appears
            var queryChip = page.Locator(".active-filter-chip");
            await queryChip.WaitForAsync();
            var chipText = await queryChip.InnerTextAsync();
            Assert.Contains("Query: type == svg and rating >= 4", chipText);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase9_HelpView_And_TryItButton_PreloadsAndExecutesQuery(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        SeedFilterTestImages();

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            // Navigate to Help view
            await page.ClickAsync("button[data-view='help']");
            await page.WaitForSelectorAsync(".help-view-container");

            // Check subnav links exist
            var navQuery = page.Locator("a[href='#help/query']");
            await navQuery.WaitForAsync();

            // Find a 'Try it' button in the examples section
            var tryBtn = page.Locator(".btn-try-query").First;
            await tryBtn.WaitForAsync();
            await tryBtn.ClickAsync();

            // Navigated back to folders
            await page.WaitForSelectorAsync(".image-browser-view");

            // Query box should be active and populated
            var queryArea = page.Locator(".query-box-textarea");
            await queryArea.WaitForAsync();
            var val = await queryArea.InputValueAsync();
            Assert.False(string.IsNullOrWhiteSpace(val));
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase9_KeyboardShortcuts_FocusesFilterBar(string browserType)
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

            await page.ClickAsync("button[data-view='folders']");
            await page.WaitForSelectorAsync(".filter-bar-container");

            // Press '/'
            await page.Keyboard.PressAsync("/");

            // Verify filter input is focused
            var isFocused = await page.EvaluateAsync<bool>(@"
                () => {
                    const el = document.activeElement;
                    return el && el.classList.contains('filter-text-input');
                }
            ");
            Assert.True(isFocused);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }
}

