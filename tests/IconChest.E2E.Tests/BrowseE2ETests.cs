using System.Data;
using Dapper;
using Microsoft.Playwright;
using Xunit;

namespace IconChest.E2E.Tests;

[Trait("Category", "E2E")]
[Collection("E2ECollection")]
public class BrowseE2ETests
{
    private readonly E2EFixture _fixture;

    public BrowseE2ETests(E2EFixture fixture)
    {
        _fixture = fixture;
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Browse_NavigateViews_RendersActiveViewAndPanelA(string browserType)
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

            // 1. Click Folders
            await page.ClickAsync("#nav-btn-folders");
            await page.WaitForSelectorAsync(".folders-nav");
            Assert.Contains("#folders", page.Url);

            // 2. Click Years
            await page.ClickAsync("#nav-btn-years");
            await page.WaitForSelectorAsync(".years-nav");
            Assert.Contains("#years", page.Url);

            // 3. Click Tags
            await page.ClickAsync("#nav-btn-tags");
            await page.WaitForSelectorAsync(".tags-nav");
            Assert.Contains("#tags", page.Url);

            // 4. Click Favourites (Panel A is hidden)
            await page.ClickAsync("#nav-btn-favourites");
            var panelAHidden = await page.IsHiddenAsync("#panel-a");
            Assert.True(panelAHidden);
            Assert.Contains("#favourites", page.Url);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Browse_VirtualGrid_With100kItems_BoundsDomNodeCount(string browserType)
    {
        // Seed 100,000 synthetic items into sqlite database to stress-test virtual grid recycling
        using (var conn = _fixture.OpenDbConnection())
        {
            conn.Execute("BEGIN TRANSACTION;");
            // Check count
            int count = conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image;");
            if (count < 100_000)
            {
                var folderId = conn.ExecuteScalar<long>("SELECT id FROM library_folder LIMIT 1;");
                if (folderId == 0)
                {
                    folderId = conn.ExecuteScalar<long>(
                        "INSERT INTO library_folder (path) VALUES ('/synth/icons') RETURNING id;");
                }

                int toInsert = 100_000 - count;
                for (int i = 0; i < toInsert; i += 1000)
                {
                    int batch = Math.Min(1000, toInsert - i);
                    var rows = new List<object>();
                    for (int j = 0; j < batch; j++)
                    {
                        int id = count + i + j + 1;
                        rows.Add(new
                        {
                            folderId,
                            path = $"/synth/icons/icon_{id:D6}.png",
                            pathKey = $"synth/icons/icon_{id:D6}.png",
                            fileName = $"icon_{id:D6}.png",
                            extension = ".png",
                            dirKey = "synth/icons",
                            dirPath = "/synth/icons",
                            title = $"icon_{id:D6}.png",
                            format = "png",
                            width = 128,
                            height = 128,
                            size = 1024,
                            hash = $"hash_{id:D6}",
                            hashAlgo = "sha256",
                            addedUtc = DateTime.UtcNow.ToString("O"),
                            createdUtc = DateTime.UtcNow.ToString("O"),
                            rating = 3,
                            isFav = (id % 10 == 0) ? 1 : 0
                        });
                    }

                    conn.Execute(@"
                        INSERT INTO image (folder_id, path, path_key, file_name, extension, directory_key, directory_path, title, format, width, height, size_bytes, hash, hash_algo, added_utc, created_utc, rating, is_favourite, is_hidden, is_missing, is_duplicate)
                        VALUES (@folderId, @path, @pathKey, @fileName, @extension, @dirKey, @dirPath, @title, @format, @width, @height, @size, @hash, @hashAlgo, @addedUtc, @createdUtc, @rating, @isFav, 0, 0, 0);",
                        rows);
                }
            }
            conn.Execute("COMMIT;");
        }

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

            await page.ClickAsync("#nav-btn-folders");
            await page.WaitForSelectorAsync(".virtual-grid-scroll");

            // Wait for count badge to report ~100k images
            await page.WaitForFunctionAsync(@"() => {
                const badge = document.querySelector('.browser-count-badge');
                return badge && badge.textContent.includes('100,000');
            }");

            // Verify DOM element count is strictly bounded (should be under 200 elements, not 100,000)
            int cellCount = await page.EvaluateAsync<int>(@"() => document.querySelectorAll('.grid-cell').length");
            Assert.True(cellCount > 0, "Grid cells should be rendered");
            Assert.True(cellCount < 200, $"Grid DOM cell count ({cellCount}) should be strictly bounded");

            // Scroll deeply down into virtual grid
            await page.EvaluateAsync(@"() => {
                const scrollEl = document.querySelector('.virtual-grid-scroll');
                scrollEl.scrollTop = 500000;
            }");

            // Wait a brief moment for scroll and render
            await page.WaitForTimeoutAsync(200);

            int cellCountAfterScroll = await page.EvaluateAsync<int>(@"() => document.querySelectorAll('.grid-cell').length");
            Assert.True(cellCountAfterScroll < 200, $"Recycled cell count ({cellCountAfterScroll}) must remain strictly bounded after scrolling 100k list");
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Browse_InteractiveControls_ZoomSliderAndBackgroundToggle(string browserType)
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
            await page.WaitForSelectorAsync(".browser-zoom-slider");

            // 1. Test background toggle
            var bgBtn = await page.QuerySelectorAsync(".browser-bg-btn");
            Assert.NotNull(bgBtn);

            string initialBg = await page.EvaluateAsync<string>(@"() => localStorage.getItem('iconchest_grid_bg') || 'checker'");
            await bgBtn.ClickAsync();
            string newBg = await page.EvaluateAsync<string>(@"() => localStorage.getItem('iconchest_grid_bg')");
            Assert.NotEqual(initialBg, newBg);

            // 2. Test Zoom slider
            await page.EvaluateAsync(@"() => {
                const slider = document.querySelector('.browser-zoom-slider');
                slider.value = '240';
                slider.dispatchEvent(new Event('input', { bubbles: true }));
            }");

            string zoomLabel = await page.InnerTextAsync(".browser-zoom-label");
            Assert.Equal("240px", zoomLabel);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Browse_PreviewModal_OpensAndClosesWithEscape(string browserType)
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
            await page.WaitForSelectorAsync(".grid-cell");

            // Click Preview button
            await page.ClickAsync(".browser-footer button:has-text('Preview')");

            // Verify preview overlay is visible
            await page.WaitForSelectorAsync(".preview-overlay:not([hidden])");

            // Press Escape to close
            await page.Keyboard.PressAsync("Escape");

            // Verify overlay is hidden
            await page.WaitForSelectorAsync(".preview-overlay[hidden]");
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Browse_DetailsPanel_OpensOnSelectAndShowsInfo(string browserType)
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
            await page.WaitForSelectorAsync(".grid-cell");

            // Click first grid cell
            await page.ClickAsync(".grid-cell:first-child");

            // Verify details panel is open (not collapsed)
            await page.WaitForSelectorAsync(".panel-details:not(.collapsed)");

            // Verify details header has Close button and close it
            await page.ClickAsync(".panel-details-close-btn");

            // Verify details panel collapsed
            await page.WaitForSelectorAsync(".panel-details.collapsed");
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }
}

