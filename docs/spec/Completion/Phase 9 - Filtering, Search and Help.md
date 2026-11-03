## **Phase 9 Implementation: Filtering, Search and Help**

IconChest Phase 9 is complete. All backend APIs, pure logic models, UI components, help views, and tests have been implemented and verified with all 510 tests passing across the solution.

---

### **Key Additions & Architecture**

#### **1. Scope vs. Filter API Architecture**

- **Partition Rule**: Implemented in `ImageEndpoints.cs`. `q` may be freely combined with scope parameters (`folderId`, `directory`, `includeSubfolders`, `year`, `month`, `dateField`, `tag`, `tagMode`, `favourite`, `limit`, `offset`, `sort`, `dir`) with `AND` semantics, but combining `q` with any structured filter parameters returns `400 Bad Request` naming the offending parameters.
- **Filter Parameters**: Added `filterTag` (repeatable), `filterTagMode` (`any` or `all`), `untagged` (`bool`), `filterFolderId` (`long`), `rangeField` (`effective`, `created`, `modified`, `taken`, `added`), and `ratingMax` (`0` for Unrated) to `ImageSearchRequest` and `ImageSearchService`.
- **`GET /api/filters/options`**: Implemented in `FilterEndpoints.cs`, returning formats with image counts, library folders with image counts, and min/max effective dates (excluding hidden and missing).
- **`SimpleFilterToQuery` & `POST /api/query/from-filter`**: Updated to translate all structured filter combinations to equivalent query strings. Over 60 combinations verified to return 100% byte-identical and ID-identical results in `SimpleFilterToQueryTests.cs`.

#### **2. Pure Front-End Models**

- `lib/filter-model.js`:
    - Pure Simple filter state model, conversion to API query parameters (`filterStateToParams`).
    - Date presets with injectable clock (`today`, `yesterday`, `last7days`, `last30days`, `thisMonth`, `lastMonth`, `thisYear`, `lastYear`).
    - Dirty detection (`isFilterDirty`) powering the dirty dot indicator on the Filter button.
    - Active filter chips computation and individual chip removal (`getActiveFilterChips`).
    - View-specific control visibility (`getVisibleControlsForView`).
- `lib/query-box-model.js`:
    - Live query validation state machine with 250ms debouncing.
    - Rejection of stale validation responses via sequence tracking.
    - UTF-16 character range mapping for syntax error spans (`getErrorSelectionRange`).
    - Recent queries list capped at 20 items, deduplicated and persisted in `localStorage`.
- `lib/help-render.js`:
    - Safe DOM builder (using `el()`, `createSvg()`, and `textContent`; strictly no `innerHTML` on server data).
    - Renders Shortcuts from `shortcuts.js` and Query reference, grammar, fields, and examples from `/api/query/help`.
    - "Try it" buttons on examples that preload and execute queries in the image browser.

#### **3. Components & UI Integration**

- `components/filter-bar.js`:
    - WAI-ARIA tabbed bar (`role="tablist"` with `Simple` and `Advanced` tabs).
    - Non-blocking Undo toast when switching modes with active criteria.
    - Simple panel with text input, format select, tag select, untagged toggle, date preset and custom range inputs, rating dropdown, dirty indicator, "Clear", and "Convert to query" button.
- `components/query-box.js`:
    - Syntax textarea, live validation indicators, clickable error pills that highlight error spans, Examples and Recent dropdowns, Run (`Ctrl+Enter`), Clear, and Help deep link.
- `components/active-filters.js`:
    - Active filter chips with removal buttons, active scope badge, and "Clear all" button.
- `views/help.js`:
    - Full-width Panel B view with sub-navigation supporting deep links (`#help/shortcuts`, `#help/query`, `#help/filters`, `#help/tips`).
- `views/image-browser.js`:
    - Embedded `FilterBar` between top toolbar and grid.
    - Keyboard shortcuts: `/` and `Ctrl+F` / `Cmd+F` to focus filter bar, `Enter` to apply simple filter, `Escape` to clear or dismiss focus back to grid.

---

### **Verification & Test Results**

IconChest.Core.Tests:     Passed (369 / 369 passed)

IconChest.Api.Tests:      Passed ( 54 /  54 passed)

IconChest.Desktop.Tests:  Passed ( 50 /  50 passed)

IconChest.E2E.Tests:      Passed ( 37 /  37 passed)

---------------------------------------------------

Total Solution Tests:     Passed (510 / 510 passed, 0 failed, 0 skipped)










---






How should i test Phase 9?





Here is how you can test Phase 9, both via the **automated test suite** and **interactively in the app**.

---

### **1. Run Automated Tests**

To run the automated tests from your terminal (always using `XDG_RUNTIME_DIR=/tmp` on Linux):

#### **Run All Phase 9 E2E Tests (Playwright / Chromium)**

```bash

XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.E2E.Tests/ --filter "FullyQualifiedName~Phase9E2ETests"
```

#### **Run Front-End Unit Tests (Filter Model, QueryBox, Help Render)**

```bash

XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.E2E.Tests/ --filter "FullyQualifiedName~JavaScriptModules"
```

#### **Run Query & Structured Filter Equivalence Tests (60+ combinations)**

```bash

XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.Core.Tests/ --filter "FullyQualifiedName~SimpleFilterToQueryTests"
```
#### **Run API Integration Tests (Scope Partition Rule, Filter Options, Convert Endpoint)**

```bash

XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.Api.Tests/ --filter "FullyQualifiedName~QueryApiTests"
```

#### **Run the Entire Solution Test Suite (510 tests)**

```bash

XDG_RUNTIME_DIR=/tmp dotnet test IconChest.sln
```
---

### **2. Manual Interactive Testing in the App**

#### **Launch the Desktop App**

```bash

XDG_RUNTIME_DIR=/tmp dotnet run --project src/IconChest.Desktop
```
_(Or run the web API directly with `XDG_RUNTIME_DIR=/tmp dotnet run --project src/IconChest.Api` and open the URL displayed in the console in your browser, logging in with the session secret printed to stdout)._

---

### **3. What to Test in the UI**

#### **A. Simple Filter Mode**

1. **Focus Shortcut**: Press `/` or `Ctrl+F` from any image grid view. Focus jumps straight into the filter text box.
2. **Text Filter**: Type a word (or multiple words, e.g. `arrow icon`) and press `Enter` or click **Filter**.
    - Notice the yellow dirty dot on the Filter button when changes are pending.
    - An active filter chip appears below the filter bar (e.g. `Text: "arrow icon"`).
3. **Format & Rating**: Select a format (`SVG`, `PNG`, etc.) or rating (`4+`, `Unrated`). Click **Filter**.
4. **Remove Chips**: Click the `×` on any active filter chip to remove just that criteria. The grid immediately updates.
5. **Clear**: Click the **Clear** button or "Clear all" to reset all filters.

#### **B. "Convert to Query"**

1. In **Simple** mode, set a text search and a format or rating.
2. Click the **Convert to query** button.
3. The filter bar will automatically switch to the **Advanced** tab and populate the query editor with the equivalent syntax (e.g., `type == svg and rating >= 4`).

#### **C. Advanced Query Mode**

1. Click the **Advanced** tab.
2. **Live Validation**:
    - Type an invalid query like `badfield == 123`.
    - A red error pill appears with the error message and position; the **Run** button is disabled.
    - Click the error pill—it automatically highlights the offending syntax in the textarea.
    - Fix the query to `type == png or rating >= 4`. A green checkmark appears indicating **Valid query**, and the **Run** button is enabled.
3. **Run Query**: Press `Ctrl+Enter` or click **Run**.
4. **Examples & Recent Dropdowns**:
    - Pick an item from the **Examples...** dropdown to load a sample query.
    - The **Recent queries...** dropdown stores your last 20 queries across restarts.

#### **D. Help Page & "Try It" Buttons**

1. Click the **Help** button (question mark icon) at the bottom of the left icon bar (or in the Query Box toolbar).
2. The full-width Help view opens with sub-navigation (`Shortcuts`, `Query Reference`, `Filters Guide`, `Tips`).
3. Click any sub-nav link (e.g., `#help/query` or `#help/shortcuts`) to verify deep-linking.
4. Under the **Query Examples** section, click any **Try it** button.
    - The app navigates back to your image library, preloads the query into the Advanced Query box, and runs it immediately.

