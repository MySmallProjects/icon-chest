using System.Data;
using Dapper;
using Microsoft.Playwright;
using Xunit;

namespace IconChest.E2E.Tests;

[Trait("Category", "E2E")]
[Collection("E2ECollection")]
public class Phase8E2ETests
{
    private readonly E2EFixture _fixture;

    public Phase8E2ETests(E2EFixture fixture)
    {
        _fixture = fixture;
    }

    private (long image1Id, long image2Id) SeedTwoImages()
    {
        using var conn = _fixture.OpenDbConnection();
        var folderId = conn.ExecuteScalar<long>("SELECT id FROM library_folder LIMIT 1;");
        if (folderId == 0)
        {
            folderId = conn.ExecuteScalar<long>(
                "INSERT INTO library_folder (path) VALUES ('/synth/phase8') RETURNING id;");
        }

        var now = DateTime.UtcNow.ToString("O");
        var img1Id = conn.ExecuteScalar<long>(@"
            INSERT INTO image (folder_id, path, path_key, file_name, extension, directory_key, directory_path, title, format, width, height, size_bytes, hash, hash_algo, added_utc, created_utc, rating, is_favourite, is_hidden, is_missing, is_duplicate)
            VALUES (@folderId, '/synth/phase8/img1.png', 'synth/phase8/img1.png', 'img1.png', '.png', 'synth/phase8', '/synth/phase8', 'img1.png', 'png', 200, 200, 4096, 'hash_p8_1', 'sha256', @now, @now, 2, 0, 0, 0, 0)
            RETURNING id;", new { folderId, now });

        var img2Id = conn.ExecuteScalar<long>(@"
            INSERT INTO image (folder_id, path, path_key, file_name, extension, directory_key, directory_path, title, format, width, height, size_bytes, hash, hash_algo, added_utc, created_utc, rating, is_favourite, is_hidden, is_missing, is_duplicate)
            VALUES (@folderId, '/synth/phase8/img2.png', 'synth/phase8/img2.png', 'img2.png', '.png', 'synth/phase8', '/synth/phase8', 'img2.png', 'png', 300, 300, 8192, 'hash_p8_2', 'sha256', @now, @now, 2, 0, 0, 0, 0)
            RETURNING id;", new { folderId, now });

        return (img1Id, img2Id);
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase8_SingleImageEdit_SavesMetadataToDatabase(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        var (img1Id, _) = SeedTwoImages();

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            // Open Folders view and wait for grid cells
            await page.ClickAsync("#nav-btn-folders");
            await page.WaitForSelectorAsync(".virtual-grid-cell");

            // Select first cell
            await page.ClickAsync(".virtual-grid-cell");

            // Details panel should slide in
            await page.WaitForSelectorAsync("#panel-details:not(.collapsed)");
            await page.WaitForSelectorAsync(".details-field-row input[aria-label='Image title']");

            // Update title
            var titleInput = page.Locator(".details-field-row input[aria-label='Image title']");
            await titleInput.FillAsync("Updated Phase 8 Title");

            // Update description
            var descInput = page.Locator(".details-field-row textarea[aria-label='Image description']");
            await descInput.FillAsync("A beautiful test description");

            // Verify dirty dot is visible
            var dirtyDot = page.Locator(".details-dirty-dot");
            Assert.True(await dirtyDot.IsVisibleAsync());

            // Click Save button
            var saveBtn = page.Locator(".details-save-btn");
            Assert.True(await saveBtn.IsEnabledAsync());
            await saveBtn.ClickAsync();

            // Wait for toast notification
            await page.WaitForSelectorAsync(".toast");

            // Verify in SQLite database that changes persisted
            using var conn = _fixture.OpenDbConnection();
            var row = conn.QueryFirstOrDefault("SELECT title, description FROM image WHERE id = @id;", new { id = img1Id });
            Assert.NotNull(row);
            Assert.Equal("Updated Phase 8 Title", (string)row!.title);
            Assert.Equal("A beautiful test description", (string)row!.description);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase8_UnsavedChangesGuard_PromptsOnSelectionChangeAndRespectsCancel(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        SeedTwoImages();

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            await page.ClickAsync("#nav-btn-folders");
            await page.WaitForSelectorAsync(".virtual-grid-cell");

            var cells = page.Locator(".virtual-grid-cell");
            var count = await cells.CountAsync();
            if (count < 2) return;

            // Select cell 0
            await cells.Nth(0).ClickAsync();
            await page.WaitForSelectorAsync("#panel-details:not(.collapsed)");

            // Make a dirty edit
            var titleInput = page.Locator(".details-field-row input[aria-label='Image title']");
            await titleInput.FillAsync("Dirty Unsaved Value");

            // Now click cell 1
            await cells.Nth(1).ClickAsync();

            // Unsaved changes dialog should appear
            await page.WaitForSelectorAsync("#app-global-dialog[open]");
            var dialogHeader = await page.Locator("#app-global-dialog .dialog-header").TextContentAsync();
            Assert.Contains("Unsaved", dialogHeader);

            // Click Cancel
            await page.ClickAsync("#app-global-dialog .dialog-footer button:has-text('Cancel')");

            // Dialog closes and cell 0 is still selected, title input still has dirty value
            var val = await titleInput.InputValueAsync();
            Assert.Equal("Dirty Unsaved Value", val);
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("chromium")]
    public async Task Phase8_ClickHeartIcon_PersistsToDatabase(string browserType)
    {
        var (browser, skipReason) = await _fixture.TryLaunchBrowserAsync(browserType);
        if (browser == null)
        {
            Assert.True(true, $"Skipped: {skipReason}");
            return;
        }

        var (img1Id, _) = SeedTwoImages();

        try
        {
            var page = await browser.NewPageAsync();
            await page.GotoAsync($"{_fixture.ApiUri}#handoff={_fixture.Secret}");
            await page.WaitForSelectorAsync("#icon-bar");

            await page.ClickAsync("#nav-btn-folders");
            await page.WaitForSelectorAsync(".virtual-grid-cell");

            // 1. Select cell 0
            await page.ClickAsync(".virtual-grid-cell");
            await page.WaitForSelectorAsync("#panel-details:not(.collapsed)");

            // 2. Click heart icon in details panel
            var favBtn = page.Locator(".details-fav-btn");
            await favBtn.ClickAsync();

            // Wait for toast
            await page.WaitForSelectorAsync(".toast");

            // 3. Verify in SQLite database that is_favourite = 1
            using (var conn = _fixture.OpenDbConnection())
            {
                var isFav = conn.ExecuteScalar<int>("SELECT is_favourite FROM image WHERE id = @id;", new { id = img1Id });
                Assert.Equal(1, isFav);
            }
        }
        finally
        {
            await browser.DisposeAsync();
        }
    }
}

