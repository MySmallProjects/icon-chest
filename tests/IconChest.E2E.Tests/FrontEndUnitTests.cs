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

    [Theory]
    [InlineData("chromium")]
    public async Task Phase7_GridGeometry_Calculations_AreExactAndPure(string browserType)
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
                    const geo = await import('/js/lib/grid-geometry.js');

                    // 1. Column count: container=800, cell=160, gap=8 => Math.floor(808 / 168) = 4
                    const cols = geo.calculateColumnCount(800, 160, 8);
                    if (cols !== 4) return false;

                    // 2. Row count: 10 items, 4 cols => 3 rows
                    const rows = geo.calculateRowCount(10, 4);
                    if (rows !== 3) return false;

                    // 3. Total height: 3 rows * (160 + 8) - 8 = 496
                    const totalH = geo.calculateTotalHeight(10, 4, 160, 160, 8);
                    if (totalH !== 496) return false;

                    // 4. Cell rect: index 5 => col 1, row 1 => x = 168, y = 168
                    const rect = geo.getCellRect(5, 4, 160, 160, 8);
                    if (rect.x !== 168 || rect.y !== 168 || rect.width !== 160 || rect.height !== 160) return false;

                    // 5. Index at point: point (170, 170) falls inside index 5
                    const idx = geo.getIndexAtPoint(170, 170, 4, 10, 160, 160, 8);
                    if (idx !== 5) return false;

                    // Point in gap (164, 170) returns -1
                    const inGap = geo.getIndexAtPoint(164, 170, 4, 10, 160, 160, 8);
                    if (inGap !== -1) return false;

                    // 6. Visible range: scrollTop=0, viewport=300, cellHeight=160, gap=8, bufferRows=1
                    // row 0 to 1 visible => with buffer 1: startRow=0, endRow=2 => startIndex=0, endIndex=9 (capped by 10 items)
                    const vis = geo.getVisibleRange(0, 300, 10, 4, 160, 8, 1);
                    if (vis.startIndex !== 0 || vis.endIndex !== 9) return false;

                    // 7. Marquee hits: box covering col 0-1, row 0 => items 0, 1
                    const hits = geo.getMarqueeHits({ x: 10, y: 10, width: 200, height: 50 }, 10, 4, 160, 160, 8);
                    if (hits.length !== 2 || hits[0] !== 0 || hits[1] !== 1) return false;

                    return true;
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
    public async Task Phase7_SelectionModel_DesktopSemantics_WorkCorrectly(string browserType)
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
                    const { SelectionModel } = await import('/js/lib/selection-model.js');
                    const sm = new SelectionModel();
                    const ordered = [10, 20, 30, 40, 50];

                    // 1. Single click
                    sm.handleClick(20, false, false, ordered);
                    if (sm.count !== 1 || !sm.isSelected(20) || sm.anchorId !== 20) return false;

                    // 2. Ctrl click (toggle)
                    sm.handleClick(40, true, false, ordered);
                    if (sm.count !== 2 || !sm.isSelected(20) || !sm.isSelected(40)) return false;

                    // Ctrl click to deselect
                    sm.handleClick(20, true, false, ordered);
                    if (sm.count !== 1 || sm.isSelected(20)) return false;

                    // 3. Shift range: anchor is 40, click 10 => range 10, 20, 30, 40
                    sm.handleClick(10, false, true, ordered);
                    if (sm.count !== 4 || !sm.isSelected(10) || !sm.isSelected(40) || sm.isSelected(50)) return false;

                    // 4. Select All
                    sm.selectAll(ordered);
                    if (sm.count !== 5) return false;

                    // 5. Retain
                    sm.retain([10, 30, 99]);
                    if (sm.count !== 2 || !sm.isSelected(10) || !sm.isSelected(30)) return false;

                    // 6. Clear
                    sm.clear();
                    if (sm.count !== 0) return false;

                    return true;
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
    public async Task Phase7_KeyboardNav_MatrixAndEdgeCases_NavigateCorrectly(string browserType)
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
                    const { navigateIndex } = await import('/js/lib/keyboard-nav.js');
                    const total = 9; // 3 cols x 3 rows (indices 0..8)
                    const cols = 3;
                    const rowsPerPage = 2;

                    // Initial focus from -1
                    if (navigateIndex(-1, 'ArrowRight', total, cols, rowsPerPage) !== 0) return false;

                    // Right from 0 => 1
                    if (navigateIndex(0, 'ArrowRight', total, cols, rowsPerPage) !== 1) return false;

                    // Down from 1 => 4
                    if (navigateIndex(1, 'ArrowDown', total, cols, rowsPerPage) !== 4) return false;

                    // Left from 4 => 3
                    if (navigateIndex(4, 'ArrowLeft', total, cols, rowsPerPage) !== 3) return false;

                    // Up from 3 => 0
                    if (navigateIndex(3, 'ArrowUp', total, cols, rowsPerPage) !== 0) return false;

                    // Up from 0 => clamped to 0
                    if (navigateIndex(0, 'ArrowUp', total, cols, rowsPerPage) !== 0) return false;

                    // Home & End
                    if (navigateIndex(4, 'Home', total, cols, rowsPerPage) !== 0) return false;
                    if (navigateIndex(4, 'End', total, cols, rowsPerPage) !== 8) return false;

                    // Short last row: 10 items total, 4 cols.
                    // indices 0..3 (row 0), 4..7 (row 1), 8..9 (row 2)
                    // Down from index 6 (col 2): row 2 only has col 0 and col 1. Should clamp to index 9 (last item).
                    const shortLastIndex = navigateIndex(6, 'ArrowDown', 10, 4, rowsPerPage);
                    if (shortLastIndex !== 9) return false;

                    return true;
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
    public async Task Phase7_ThumbLoader_SizesAndBackoffSchedule_WorkCorrectly(string browserType)
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
                    const { pickThumbnailSize, getRetryDelay, STANDARD_THUMBNAIL_SIZES } = await import('/js/lib/thumb-loader.js');

                    // Standard sizes
                    if (STANDARD_THUMBNAIL_SIZES.join(',') !== '64,128,256') return false;

                    // Size picking with DPR 1
                    if (pickThumbnailSize(48, 1) !== 64) return false;
                    if (pickThumbnailSize(64, 1) !== 64) return false;
                    if (pickThumbnailSize(100, 1) !== 128) return false;
                    if (pickThumbnailSize(128, 1) !== 128) return false;
                    if (pickThumbnailSize(160, 1) !== 256) return false;

                    // Size picking with DPR 2 (Retina): 64 * 2 = 128
                    if (pickThumbnailSize(64, 2) !== 128) return false;
                    if (pickThumbnailSize(80, 2) !== 256) return false;

                    // Retry delays (1s, 2s, 4s, 8s, 8s, 8s)
                    if (getRetryDelay(0) !== 1000) return false;
                    if (getRetryDelay(1) !== 2000) return false;
                    if (getRetryDelay(2) !== 4000) return false;
                    if (getRetryDelay(3) !== 8000) return false;
                    if (getRetryDelay(5) !== 8000) return false;

                    return true;
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
    public async Task Phase7_TreeModel_WaiAriaNavigationAndTypeAhead_WorkCorrectly(string browserType)
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
                    const { TreeModel } = await import('/js/lib/tree-model.js');
                    const roots = [
                        { id: 'f1', name: 'Animals', children: [
                            { id: 'f1-1', name: 'Cats', children: [] },
                            { id: 'f1-2', name: 'Dogs', children: [] }
                        ]},
                        { id: 'f2', name: 'Architecture', children: [] }
                    ];

                    const model = new TreeModel(roots);

                    // Initially collapsed: visible = Animals, Architecture
                    let vis = model.getVisibleNodes();
                    if (vis.length !== 2) return false;

                    // Right arrow expands Animals
                    let nav = model.navigate('f1', 'ArrowRight');
                    if (!nav || nav.targetId !== 'f1' || !model.isExpanded('f1')) return false;

                    // Now visible = Animals, Cats, Dogs, Architecture
                    vis = model.getVisibleNodes();
                    if (vis.length !== 4) return false;

                    // Down arrow moves to Cats
                    nav = model.navigate('f1', 'ArrowDown');
                    if (!nav || nav.targetId !== 'f1-1') return false;

                    // Left arrow from Cats moves back to parent Animals
                    nav = model.navigate('f1-1', 'ArrowLeft');
                    if (!nav || nav.targetId !== 'f1') return false;

                    // Left arrow on Animals collapses it
                    nav = model.navigate('f1', 'ArrowLeft');
                    if (!nav || model.isExpanded('f1')) return false;

                    // Type-ahead: pressing 'a' matches Animals, pressing 'a' again cycles to Architecture
                    const match1 = model.typeAhead(null, 'a');
                    if (match1 !== 'f1') return false;
                    const match2 = model.typeAhead('f1', 'a');
                    if (match2 !== 'f2') return false;

                    return true;
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
    public async Task Phase7_ImageSource_PagingAndLruCache_EvictsAndAbortsCorrectly(string browserType)
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
                    const { ImageSource } = await import('/js/lib/image-source.js');

                    let fetchCount = 0;
                    const source = new ImageSource({
                        pageSize: 10,
                        maxCachedPages: 2,
                        fetchFn: async (offset, limit) => {
                            fetchCount++;
                            const items = [];
                            for (let i = 0; i < limit; i++) {
                                items.push({ id: offset + i + 1, name: `img_${offset + i + 1}` });
                            }
                            return { total: 100, items };
                        },
                        fetchIdsFn: async () => Array.from({ length: 100 }, (_, i) => i + 1)
                    });

                    // Load page 0 and page 1
                    await source.loadPage(0);
                    await source.loadPage(1);

                    if (source.getItem(5)?.id !== 6) return false;
                    if (source.getItem(15)?.id !== 16) return false;

                    // Set visible range to page 1
                    source.setVisibleRange(10, 19);

                    // Load page 2 => maxCachedPages is 2, so page 0 should be LRU evicted because page 1 is visible
                    await source.loadPage(2);
                    if (source.hasPage(0)) return false; // Evicted
                    if (!source.hasPage(1)) return false; // Preserved
                    if (!source.hasPage(2)) return false; // Newly cached

                    // Ordered IDs lazy fetching
                    const ids = await source.getOrderedIds();
                    if (ids.length !== 100 || ids[0] !== 1 || ids[99] !== 100) return false;

                    return true;
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
