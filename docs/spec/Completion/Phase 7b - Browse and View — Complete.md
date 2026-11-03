### **Phase 7: Browse and View — Complete**

All requirements of **Phase 7: Browse and View** have been built, verified, and tested across all test suites.

---

### **What Was Built**

#### **1. API Addition (`year=unknown`)**

- **ImageSearchRequest.cs** & **ImageSearchService.cs**: Added `YearUnknown` support, generating `{dateCol} IS NULL` to match the `TreeService.GetYearTree` "unknown" node count.
- **ImageEndpoints.cs**: Accepts `year=unknown` or numeric years in `/api/images` and `/api/images/ids`.
- **EndpointIntegrationTests.cs**: Added integration test `Images_Search_And_Ids_SupportYearUnknown`.

#### **2. Pure Front-End Modules (`js/lib/`)**

- **grid-geometry.js**: Columns, rows, cell bounds, visible ranges with buffer rows, scroll anchoring when resizing, and marquee AABB arithmetic hit testing without DOM layout access.
- **selection-model.js**: Multi-selection state engine supporting single-click, Ctrl-click (toggle), Shift-click (range), Ctrl+Shift-click (additive range), Select All, Clear, and valid ID retention.
- **keyboard-nav.js**: 2D matrix navigation for Arrow keys, Home, End, PageUp, PageDown, and short terminal rows.
- **thumb-loader.js**: `pickThumbnailSize` (64, 128, 256) scaled by `window.devicePixelRatio`, plus exponential backoff (1s, 2s, 4s, 8s) for thumbnail generation retries.
- **image-source.js**: Paged loading (200 items/page), LRU page caching (max 20 pages, protecting visible pages), concurrency limiting (max 4 concurrent), in-flight de-duplication, stale request aborting via `AbortController`, soft-refreshing, and lazy ordered ID caching.
- **tree-model.js**: WAI-ARIA Tree model supporting hierarchy flattening, parent expansion, keyboard moves, and type-ahead searching.
- **shortcuts.js**: Central keyboard shortcut registry.

#### **3. UI Components (`js/components/`)**

- **virtual-grid.js**: Virtualized grid with bounded DOM pool, CSS `transform: translate3d(x, y, 0)` cell positioning, thumbnail lazy-loading with 202 retry scheduling, rubber-band marquee selection with edge auto-scrolling, hover checkbox/favourite buttons, badges (hidden, retry), and caption formatters.
- **preview.js**: Full-viewport modal dialog (`role="dialog"`, `aria-modal="true"`) setting `inert` on the application root. Supports Prev/Next navigation, image preloading, and an info HUD toggled via the `I` key.
- **details-panel.js**: Resizable 260–480px slide drawer displaying image metadata, dimensions, file size, hash, EXIF table, tag chips, and read-only star ratings.
- **tree.js**: Accessible WAI-ARIA Tree (`role="tree"`, `role="treeitem"`) with roving tabindex, expand/collapse chevrons, and item counts.
- **tag-chip.js**: Semantic tag chips with color badges and count indicators.

#### **4. Browsing Views (`js/views/`)**

- **image-browser.js**: Shared view coordinator connecting toolbar controls (sorting dropdown, sort direction, transparency background toggle, details toggle), virtual grid, footer (selection count, select all, clear, preview, size slider), and details drawer.
- **folders.js**: Folder directory tree via `/api/tree/folders`, "Include subfolders" toggle, and root "All Images" selection.
- **years.js**: Date field dropdown (`effective`, `created`, `modified`, `taken`, `added`), year and month tree with localized month names, and "Unknown" date grouping.
- **tags.js**: Tag chips multi-selector with "Match Any" (OR) and "Match All" (AND) mode switching.
- **favourites.js**: Fast favourites view (`favourite=true`) with Panel A hidden.

---

### **Verification & Test Results**

The full test suite passes with 100% success across all assemblies:

Passed!  - Failed: 0, Passed:  46, Skipped: 0, Total:  46 - IconChest.Api.Tests.dll

Passed!  - Failed: 0, Passed: 369, Skipped: 0, Total: 369 - IconChest.Core.Tests.dll

Passed!  - Failed: 0, Passed:  20, Skipped: 0, Total:  20 - IconChest.E2E.Tests.dll

Passed!  - Failed: 0, Passed:  50, Skipped: 0, Total:  50 - IconChest.Desktop.Tests.dll

--------------------------------------------------------------------------------------

Total: 485 tests passed, 0 failed.

- **100k Items Virtual Grid Test:** BrowseE2ETests.cs seeded 100,000 synthetic image database records and verified that during deep scrolling down to 500,000px, the number of DOM cell elements in the virtual grid remains strictly bounded (`< 200` cells).
- Full documentation has been recorded in FRONTEND.md and Phase 7 - Browse and View - Complete.md.

























How should I testPhase 7 ?

To test **Phase 7 (Browse and View)**, you can run both the automated test suites and interactive manual testing in the UI.

---

### **1. Automated Test Suites**

Run all 485 tests across the solution:

```bash

XDG_RUNTIME_DIR=/tmp dotnet test IconChest.sln
```
#### **Run Specific Phase 7 Test Suites**

- **Pure Front-End Module Tests** (geometry math, selection engine, matrix keyboard navigation, thumbnail size picking, WAI-ARIA tree model):
    
    ``` bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.E2E.Tests/ --filter "FullyQualifiedName~FrontEndUnitTests"
    ```
    
- **100,000 Item Virtual Grid Stress Test** (seeds 100k synthetic images and verifies DOM element recycling stays strictly bounded `< 200` cells during deep scrolling):
    
    ```    bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.E2E.Tests/ --filter "FullyQualifiedName~Browse_VirtualGrid_With100kItems_BoundsDomNodeCount"
    ```
    
- **End-to-End View & Modal Tests** (view switching, zoom slider, background modes, preview modal, details drawer):
    
    ```    bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.E2E.Tests/ --filter "FullyQualifiedName~BrowseE2ETests"
    ```
    
- **API `year=unknown` Integration Tests**:
    
    ```    bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.Api.Tests/ --filter "FullyQualifiedName~Images_Search_And_Ids_SupportYearUnknown"
    
    ```

---

### **2. Interactive Manual Testing in the Browser**

Launch IconChest in browser mode:

``` bash

dotnet run --project src/IconChest.Desktop -- --browser
```

_(Or omit `--browser` if you have `libwebkit2gtk-4.0-37` installed on your system)._

#### **Features to Verify in the UI**

1. **Populate Sample Library:**
    
    - Click **Settings** (bottom gear icon in the left icon bar) →\rightarrow→ **Library**.
    - Add a directory containing images/icons and click **Scan Now**.
    - Watch the live scan status update in the bottom status bar.
2. **Folders Tree & Subfolders (Panel A →\rightarrow→ Panel B):**
    
    - Click the **Folders** icon in the icon bar.
    - Panel A displays the hierarchical folder tree with image counts.
    - Click any directory node to filter images in Panel B.
    - Toggle **"Include subfolders"** to switch between direct images and recursive directory trees.
    - Click **"All Images"** at the top of Panel A to clear the filter.
3. **Virtual Grid & Size Controls:**
    
    - Drag the **Zoom Slider** in the bottom footer (64px to 320px). Notice how columns dynamically adjust and the scroll position maintains your anchor point.
    - Click the **Transparency Background Button** in the top toolbar to cycle between **Checkerboard**, **Light**, and **Dark** backgrounds (ideal for transparent icons).
    - Use the **Sort Dropdown** (Date Effective, Created, Modified, Filename, Size, Dimensions) and the **Asc/Desc Direction Toggle**.
4. **Desktop Selection & Rubber-band Marquee:**
    
    - **Click:** Selects a single image.
    - **Ctrl + Click** (or Cmd + Click): Toggles individual items in/out of the selection.
    - **Shift + Click:** Selects contiguous ranges.
    - **Rubber-band Drag:** Click and drag on empty space in the grid to draw a selection rectangle. Drag near the top or bottom edge to test edge auto-scrolling.
    - **Keyboard:** Use **Arrow Keys** to move focus, **Space** to toggle selection, **Ctrl+A** to select all, and **Escape** to clear.
5. **Full-Screen Preview Modal:**
    
    - Double-click an image (or press **Enter** / click the **"Preview"** button in the footer).
    - The full-screen overlay opens with focus trapped.
    - Press **Left / Right Arrow Keys** to navigate adjacent images.
    - Press **`I`** to toggle the metadata HUD info box.
    - Press **`Escape`** (or click the Close `✕` button) to close the preview.
6. **Details Drawer (Panel C):**
    
    - Select an image to automatically open the right-side details drawer.
    - Inspect the thumbnail preview, dimensions, file size, SHA-256 hash, EXIF table, tag chips, and star ratings.
    - Click the **Copy Path** button to copy the image file path to the clipboard.
    - Click the `✕` close button (or the **Info** button in the top toolbar) to dismiss the drawer.
7. **Years & Months View:**
    
    - Click the **Years** icon in the icon bar.
    - Select the date grouping field from the dropdown (Effective, Created, Modified, Taken, Added).
    - Expand year nodes to see localized month names.
    - If you have images with missing dates, select the **"Unknown"** node.
8. **Tags & Favourites Views:**
    
    - Click the **Tags** icon: select multiple tag chips and toggle **"Match Any"** (OR) vs **"Match All"** (AND).
    - Click the heart icon on any image card to mark it as a favourite.
    - Click the **Favourites** icon in the icon bar: Panel A automatically hides and only favourited images are displayed.