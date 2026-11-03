# IconChest Phase 9 Prompt: Filtering, Search and Help

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 8 code. Those phases must be complete and their tests passing.

## Goal

Add the filter bar to the image browser views, with its two mutually exclusive modes: **Simple** (dropdowns and a text box) and **Advanced** (the query language from Phase 5, with live validation and clear errors). Build the **Help page** (keyboard shortcuts and the query reference, generated from the same sources as the code), and the "Convert to query" button. After this phase the app's search and filtering are complete.

## Before you write any code

1. Read the existing solution: the Phase 7 image browser and its reserved filter-bar slot, `shortcuts.js`, the Phase 4 image filter parameters, the Phase 5 query engine, `/api/query/validate`, `/api/query/help`, `/api/query/from-filter` and `docs/QUERY-LANGUAGE.md`, plus `docs/FRONTEND.md`.
2. Re-read brief sections 11 and 12.3.
3. Reply with a short plan and assumptions, then build.

## Scope

### 1. API changes (small, fully tested)

**Scope versus filter.** The trees send *scope* parameters (`folderId`, `directory`, `includeSubfolders`, `year`, `month`, `dateField`, `tag`, `tagMode`, and `favourite` from the Favourites view). The filter bar sends *filter* parameters. Right now `q` plus any other parameter is 400, which would stop the Advanced mode working inside a folder or year. Change the rule: **`q` may be combined with scope parameters, but not with filter parameters** (400 naming the offending parameters). Scope and filter always combine with AND. The ids endpoint follows the same rules.

The tag, folder and date-range dropdowns would collide with scope parameters, so add distinct filter parameters:

- `filterTag` (repeatable) and `filterTagMode` (`any` or `all`), and `untagged` (boolean).
- `filterFolderId`: a library folder id, meaning that folder and everything under it.
- `rangeField` (`effective`, `created`, `modified`, `taken`, `added`; default `effective`). **`dateFrom` and `dateTo` now apply to `rangeField`**, and `dateField` is used only together with `year` and `month`. Update the Phase 4 tests that relied on the old behaviour.
- `ratingMax` (so "Unrated" is `ratingMax=0`).

The existing parameters keep their names: `type`, `minWidth`, `maxWidth`, `minHeight`, `maxHeight`, `minSize`, `maxSize`, `ratingMin`, `favourite`, `text`, `hidden`, `missing`, `duplicate`, `dateFrom`, `dateTo`.

**`GET /api/filters/options`** returns what the dropdowns need: formats present with counts, library folders (id, name, path, count), and the minimum and maximum effective dates (for the custom date range). Counts exclude hidden and missing images.

**Update `SimpleFilterToQuery`** (Phase 5) and `POST /api/query/from-filter` to understand every filter parameter above, with these mappings: `filterFolderId` becomes `folder under "<path>"`; `untagged` becomes `tag is empty`; `filterTag` any becomes `tag in (...)` and all becomes `tag == "a" and tag == "b"`; `rangeField` maps to `date`, `created`, `modified`, `taken` or `added`; `ratingMax=0` becomes `rating == 0` and otherwise `rating <= n`; hidden `only` becomes `hidden == true`, `include` becomes `(hidden == true or hidden == false)`, `exclude` adds nothing; duplicate `only` and `exclude` become `duplicate == true` and `duplicate == false`; `text` becomes bare terms ANDed (quote them); sizes and dimensions map to the matching comparisons. The converter must report any filter it cannot express, and the **equivalence rule still holds**: the converted query returns exactly the same ids as the structured filter.

### 2. Front-end architecture

Keep the earlier rules (plain ES modules, no framework, no HTML built from data, pure logic in small testable modules). New and changed modules:

- `lib/filter-model.js` (pure): the Simple filter state, conversion to request parameters, the date presets, dirty detection (pending versus applied), active-filter chips and removal, reset, and which controls are hidden in which view.
- `lib/query-box-model.js` (pure): validation state machine (debounce, ignoring stale responses), mapping an error's UTF-16 `position` and `length` to a selection range, the recent-queries list (deduplicated, newest first, capped at 20).
- `lib/help-render.js` (pure data to DOM descriptions): turns the help model and the shortcuts registry into the page content, with all text inserted as text, never as HTML.
- `components/filter-bar.js` (Simple and Advanced tabs), `components/query-box.js`, `components/active-filters.js`, `views/help.js`.
- The image browser gains a `FilterController` that owns the applied filter for its view, combines it with the view's scope, resets pages through the image source, clears the selection (honouring the Phase 8 unsaved-changes guard), and keeps sort and size.

### 3. The filter bar

It sits in the slot reserved in the browser's top bar, shown in the Folders, Years, Tags and Favourites views. Two tabs: **Simple** and **Advanced** (`role="tablist"`). **Switching tabs clears the other mode**: the applied filter is removed, the previous tab's controls are reset, and the results refresh unfiltered. Show a toast with **Undo** that restores the previous tab and its state (the model keeps one snapshot for a few seconds). The only way to carry a filter across is the **Convert to query** button.

**Simple tab.** Dropdowns and a text box. Every dropdown starts with **All**. Controls apply only when the user presses **Filter** (or Enter in the text box), never as they change. While the controls differ from what is applied, the Filter button is highlighted and a small "Press Filter to apply" hint shows.

- **Search text box** ("Name, title or description"): `text` parameter.
- **Date:** a preset dropdown (All time, Today, Last 7 days, Last 30 days, This year, Last year, Custom), plus a "using" dropdown for the field (Effective date default, Created, Modified, Date taken, Date added). Custom shows From and To fields (`yyyy-mm-dd`, validated, bounded by the options endpoint). Presets compute calendar dates in the local time zone from an injectable clock.
- **Tag:** All, Untagged, then each tag (name chip colours not required in a native select, so a custom list box is optional), with a Match any or all switch when two or more are chosen.
- **Type:** the formats present, with counts.
- **Folder:** All, then each library folder.
- **Dimensions:** All, At most 32, 64, 128, 256 or 512 px (both sides), At least 128, 256, 512 or 1024 px (both sides).
- **File size:** All, Up to 10 KB, 100 KB, 1 MB, At least 1 MB, At least 10 MB.
- **Rating:** All, Unrated, At least 1 to 5 stars.
- **Favourite:** All, Favourites only, Not favourites. **Hidden in the Favourites view.**
- **Show hidden:** Exclude (default), Include, Only hidden.
- **Duplicates:** All, Only duplicates, Exclude duplicates.
- Buttons: **Filter**, **Clear** (resets every control **and** removes the applied filter), and **Convert to query**.
- **Active filters** show as removable chips under the bar (for example "Type: PNG", "Rating: 4+"). Removing a chip resets that control and re-applies. Also show the current scope (for example "In Icons / social") as a chip with a clear button that behaves like Show all.

**Convert to query:** calls `POST /api/query/from-filter` with the **pending** Simple controls, switches to the Advanced tab with the text filled in and executed, and resets the Simple controls. If the server reports an unsupported filter, stay on Simple and show which controls could not be converted.

**Advanced tab.**

- A query box (a textarea that grows to about 6 lines; **Enter runs**, Shift+Enter inserts a newline), a **Run** button, a **Clear** button, an **info icon** that opens the Help page at the query section (`#help/query`), a **Recent** dropdown (last 20 successful queries, remembered per device), and an **Examples** dropdown with about 8 common examples taken from the help model, which fill the box.
- **Live validation:** after about 250 ms of idle typing call `POST /api/query/validate`. Cancel or ignore stale responses so a slow earlier answer never overwrites a newer one. Show the first error under the box in plain English with its position ("Column 12") and the suggestion if any, set `aria-invalid` and `aria-describedby`, and make the message clickable to select the offending span in the box (positions are UTF-16 indices, so they work with emoji and accented characters). A red underline drawn by a mirrored highlight layer is a **nice-to-have only if** it never breaks caret placement, selection, IME input or scroll sync; otherwise skip it.
- **Run** sends `q` with the view's scope. Server-side `queryErrors` (a 400) are shown exactly like validation errors. An empty box means no filter. Running applies the filter, clears the selection and resets scroll.
- The query box never executes anything on its own while typing, only validates.

**Both tabs:** applying a filter shows the result count in the top bar ("128 of 4,310" when scope plus filter narrows the library), keeps the sort and size, and shows an empty-results state with the active chips and a **Clear filter** button. Per-view filter state (tab, controls, applied filter, query text) is kept while the app runs and the views stay mounted; across restarts remember only the last tab and the recent queries. **Shortcuts:** `/` or `Ctrl+F` (`Cmd+F` on macOS) focuses the active tab's search or query box (ignored while typing, like other single-key shortcuts). Add them to `shortcuts.js`.

Accessibility: the tab pattern, labelled groups, every control reachable by keyboard, `aria-live` for result counts and errors, visible focus, and no information carried by colour alone.

### 4. The Help page

Replace the Help placeholder. A page in Panel B's full width with a left sub-navigation and deep links: `#help/shortcuts`, `#help/query`, `#help/filters`, `#help/tips`.

- **Keyboard shortcuts:** generated from the `shortcuts.js` registry, grouped by context (Grid, Selection, Preview, Details panel, Trees, Tags, Global), showing `Ctrl` or `Cmd` according to the platform. **Every shortcut in the registry appears, and the page contains nothing that is not in the registry.**
- **Search and query language:** rendered from `GET /api/query/help`: an introduction, the field table (types, aliases, allowed operators, description), operators and their meaning, value formats (text and wildcards, numbers and sizes, dates and partial dates, booleans), the null rules, implicit hidden and missing filters including the `or` quirk, limits, the error codes with an example each, and the examples. **Each example has a "Try it" button** that opens the last-used browser view (or Folders), switches it to the Advanced tab, fills the example, and runs it. All prose comes from the help model so the page cannot drift from the parser. Add a short "how to read an error" section.
- **Filters:** a short guide to the Simple filters, how Filter and Clear work, what Convert to query does, and how scope, filters and queries combine.
- **Tips:** selection conventions, previews, tags, hiding images, and the fact that files on disk are never modified.

## Out of scope

Autocomplete and as-you-type tooltips (the Examples dropdown and Help page are the aid), saved searches or smart collections, relative dates in the query language, the Duplicates and Missing pages and their filtering (Phase 10), natural-language or LLM search, EXIF field search, persisting applied filters across restarts, changing the query language itself.

## Required tests

**Pure logic** (import modules in the page as in earlier phases; large data-driven tables):

- **Filter model:** every control maps to the exact request parameters; defaults send nothing; dirty detection (changing and reverting a control); chip generation and removal; reset; control visibility per view (favourite hidden in the Favourites view); the date presets with an injected clock and zone (today, week and month boundaries, year start and end, leap day, a DST change, last year); custom range validation (impossible dates, From after To); size and dimension band maps.
- **Query box model:** the debounce; cancellation and **ordering of stale responses** (a slow old response arriving after a newer one is ignored); error position to selection range for ASCII, accented text, emoji and surrogate pairs; recent list deduplication, cap and persistence; Enter and Shift+Enter handling.
- **Help rendering:** the model and registry render completely; **hostile strings in the model and registry (HTML, script, quotes) render as inert text**; platform key labels for Windows, Linux and macOS.
- **Tab switching:** the state machine for clearing, the Undo snapshot, and the Convert flow including unsupported filters.

**API tests:**

- The partition rule: `q` with each scope parameter succeeds, `q` with each filter parameter is 400 naming the parameter, the ids endpoint behaves the same, and scope with filters combine by AND.
- Every new parameter on its own and combined: `filterTag` (any and all), `untagged`, `filterFolderId` (branch semantics, nested folders), `rangeField` with `dateFrom` and `dateTo` for each field including inclusive day boundaries and a DST zone, `ratingMax`, plus `tag` (scope) and `filterTag` together.
- `GET /api/filters/options`: contents, counts excluding hidden and missing, date bounds, authentication required.
- **Converter equivalence:** at least 60 combinations of filter parameters (every parameter alone, pairs, and a few large combinations, including each hidden and duplicate mode and unsupported cases) against the seeded dataset: the converted query returns exactly the ids of the structured filter.
- Existing Phase 4 and 5 tests still pass after the parameter changes.

**End-to-end** (Playwright for .NET, Chromium and WebKit; the seeded 100,000-image database plus the small real library):

- **Simple filters:** every dropdown starts with All and lists the right options; nothing applies until Filter or Enter; the dirty highlight and hint; Clear resets controls and results; each filter's result count and first items match the API; combined filters; chips and chip removal; Show hidden in each mode; scope chips and the clear button.
- **Scope plus filter:** inside a folder, a year, a tag selection and the Favourites view (where the favourite dropdown is hidden); sort and size preserved; selection cleared; the unsaved-edits guard from Phase 8 triggers and **Cancel keeps the old filter and selection**.
- **Advanced:** the example queries run and match expected counts; Enter versus Shift+Enter; invalid queries show the right message and column, clicking it selects the exact span (including with accented text); suggestions display; server errors display the same way; **a burst of fast typing sends a bounded number of validate requests**; stale responses are ignored (delay one response); the Recent and Examples dropdowns; the empty-results state.
- **Tabs and Convert:** switching clears the other mode and the Undo restores it; Convert produces a query that returns the same ids as the Simple filter did (compare before and after); unsupported conversion message.
- **Shortcuts:** `/` and `Ctrl+F` focus the right box; single-key shortcuts such as `I` are ignored while typing in the query box.
- **Help:** all four sections render; every shortcut in the registry is present and none extra; the field and error tables match `/api/query/help`; **Try it** runs each of the examples and lands in the Advanced tab with the right results; the info icon deep-links to `#help/query`; hostile content injected through a mocked help response shows as text; no console errors.
- **State:** per-view filter state survives switching views and Settings; the last tab and recent queries survive a restart.
- No console errors or unexpected failed requests in any test.

**Performance** (print, loose assertions): applying typical Simple filters and queries on 100,000 images (time to first rows), validate round-trip latency, and the number of validate requests during 200 characters of fast typing.

## Working rules

- Follow the master brief and earlier conventions. Where silent, choose the simplest option and say so. If this prompt conflicts with the brief, follow this prompt and tell me.
- Do not change the query language itself. If you find a defect in it, report it and add a regression test instead of silently changing semantics.
- Add any dependency to `THIRD-PARTY-NOTICES.md` after checking its licence. Prefer none.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux, and the E2E tests pass in both browsers when run.
- All required tests exist and pass, or you list exactly which could not be written and why.
- `docs/QUERY-LANGUAGE.md` and the README are updated for the parameter changes, and `shortcuts.js` is complete.
- Your final reply includes: new files, assumptions, deviations, decisions to confirm, known issues, and the manual checklist with exact commands.

## How I will test Phase 9

1. Open a folder with a few thousand images. In Simple, choose PNG and a rating, change nothing else, and confirm nothing happens until Filter. Press Filter, read the chips, remove one, then press Clear.
2. Try each dropdown on its own and then several together. Include a search word. Check Show hidden in all three modes.
3. Open Years, pick a year, and apply a type filter. Confirm both apply, and the scope chip shows. Do the same in Tags and Favourites.
4. Switch to Advanced. Try the Examples dropdown, then type your own: `tag == "dog"`, `type == png or type == jpg`, `date < 2026-10-30`, `title like "elepha*"`, and a bare word.
5. Type broken queries (`tag ==`, `tpye == png`, `date == 2026-02-30`, `dog cat`). Read the error, click it, and check the selected span. Type fast and see that the app stays responsive.
6. Run a query, switch to Simple and back, and press Undo in the toast. Use Recent.
7. In Simple, set a few filters and press Convert to query. Confirm the same images appear.
8. Edit an image's title without saving, then run a filter, and check the Save, Discard, Cancel dialog.
9. Open Help. Check the shortcuts against what you actually press, read the query reference, and press Try it on three examples.
10. Press `/` and `Ctrl+F`. Type `I` in the query box and confirm the details panel does not toggle.
11. Restart the app and confirm the last tab and recent queries are remembered but the applied filter is not.
12. Run `dotnet test`, then the E2E tests in both browsers, and read the performance numbers.
