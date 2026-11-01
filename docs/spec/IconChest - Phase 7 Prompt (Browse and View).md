# IconChest Phase 7 Prompt: Browse and View

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 6 code. Those phases must be complete and their tests passing.

## Goal

Make the library browsable. Build the Folders, Years and Tags trees in Panel A, the Favourites view, and a fast virtualised thumbnail grid in Panel B with a size slider, sort controls, a transparency background toggle, favourite hearts, desktop-style selection (mouse, keyboard and rubber-band), a full-window preview overlay, and a read-only details panel. Editing details, tag management and the filter and search UI come in later phases. After this phase the app feels like a real image browser.

## Before you write any code

1. Read the existing solution, especially the Phase 6 front end (`dom.js`, `store.js`, `api.js`, `events.js`, `resizer.js`, the view registry, `docs/FRONTEND.md`) and the Phase 4 image and tree endpoints. Follow their conventions and the browser-feature limits already recorded.
2. Re-read brief sections 12.2 to 12.7.
3. Reply with a short plan and assumptions, then build.

## Scope

### 1. API addition

The Phase 4 filters cannot select the "Unknown" year node. Add `year=unknown` to `GET /api/images` and `/api/images/ids`, meaning images whose chosen `dateField` is null (and therefore the tree's `unknown` node count). Add tests. Report any other small API gap you find, and keep changes minimal.

### 2. Front-end architecture

Keep the Phase 6 rules (plain ES modules, no framework, no build step, no inline scripts, no HTML built from data). Put **all logic that can be pure in small data-agnostic modules** so it can be tested exhaustively without a UI:

- `lib/grid-geometry.js`: columns, rows, cell rectangles, visible index range with buffer, index to position and position to index, scroll anchoring when the size or width changes, marquee intersection by arithmetic (never by reading the DOM).
- `lib/selection-model.js`: selected ids, anchor, focus, click, Ctrl-click, Shift-click, Ctrl+Shift-click, select all, clear, range selection, retention across refreshes.
- `lib/keyboard-nav.js`: the index that each navigation key leads to, including the short last row, Home, End and page keys.
- `lib/image-source.js`: paged loading with a sparse page cache, prefetch window, in-flight de-duplication, abort of stale requests, LRU eviction, soft refresh, and a lazily fetched ordered id list.
- `lib/thumb-loader.js`: choosing the thumbnail size (`PickFor`, as in Phase 3, from cell size times device pixel ratio) and the retry and backoff schedule.
- `lib/tree-model.js`: flattening visible nodes, expansion state, WAI-ARIA keyboard moves, type-ahead.
- `lib/shortcuts.js`: a registry of every keyboard shortcut (keys, description, context). The Phase 10 Help page will render it, so keep it the single source for shortcut text.
- Components: `components/tree.js`, `components/virtual-grid.js`, `components/preview.js`, `components/details-panel.js`, `components/tag-chip.js`, and views `views/folders.js`, `views/years.js`, `views/tags.js`, `views/favourites.js` built on a shared `views/image-browser.js`.

Persist per-device UI state in `localStorage` under one versioned namespace, always inside try/catch: cell size, sort per view, expanded nodes per tree, the selected tree node per view, include-subfolders, the years date field, tag match mode, details panel state and width.

### 3. Panel A trees

**A reusable accessible `Tree`** following the WAI-ARIA tree pattern: `role="tree"`, `treeitem`, `aria-expanded`, `aria-selected`, roving tabindex; Up and Down move, Right expands or moves to the first child, Left collapses or moves to the parent, Home and End, type-ahead by name, Enter and Space select, a chevron click toggles without selecting. Virtualise only if you can show a measured need (tens of thousands of nodes are not expected). Expansion and selection survive refreshes.

Each tree panel has a small header with the view title and a **Show all** link that clears the selection (not a tree node). With nothing selected the view shows every image. Trees refetch on `library.changed` (debounced) and keep expansion and selection. If the selected node vanishes (for example its folder was removed), fall back to Show all.

**Folders:** data from `/api/tree/folders`. Each library folder is a top-level node, with the real sub-directory structure under it. Nodes show the name and the count of files directly in that folder, with the full path as a tooltip. Clicking a node shows **only the files in that folder** and **expands it** to reveal its sub-folders. A toolbar switch **Include subfolders** (off by default, remembered) widens the view to the whole branch.

**Years:** a dropdown above the tree chooses the date field: Effective date (default), Date created, Date modified, Date taken, Date added. The tree is Year, then Month (month names from `Intl`, in the user's locale), with counts, plus an **Unknown** node when present. Clicking a year shows the whole year, a month shows that month, Unknown uses `year=unknown`. Changing the field reloads the tree and keeps the selection if it still exists. Dates shown anywhere in the app use `yyyy-mm-dd`.

**Tags:** `/api/tags`, flat and alphabetical (case-insensitive), each rendered as a chip in its own foreground and background colour with its count. Click selects one tag. Ctrl or Cmd click adds or removes more. When two or more are selected a small **Match any / all** switch appears (remembered), mapped to `tag` and `tagMode`. With no tags in the library show a friendly empty state. Renaming, deleting and colour editing are Phase 8.

### 4. The browser view (Panel B)

One shared `image-browser` view drives Folders, Years, Tags and Favourites. It takes a small "source" object (title, query parameters, sort defaults) and owns the grid, toolbar, footer, preview and details panel.

- **Top bar:** a title (for folders a breadcrumb such as `Icons / social`; for years `2026 / October`; for tags the tag names), the total count, the **sort** controls (field dropdown with every sort field from the brief, plus an ascending or descending toggle button), the **background** toggle (Checker, Light, Dark), and a labelled empty slot for the Phase 9 filter bar. Defaults: Folders and Tags sort by filename ascending, Years and Favourites by effective date descending, remembered per view.
- **Footer:** on the left, "N selected of M" with **Select all** and **Clear selection** and **Preview** buttons; on the right, the **size slider** (labelled, keyboard operable, remembers its value).
- **States:** a loading skeleton, an empty state with a clear message (an empty library offers a button that goes to Settings, Library), an error state with Retry, and a "nothing here" state for an empty folder or tag. Announce counts and selection changes through an `aria-live` region.
- Changing the folder, year or tag clears the selection and resets scroll. Changing sort or size keeps the selection (by id) and keeps the same image in view.

### 5. The virtual grid

- **Virtualisation:** a single scroll container with a spacer of the full height. Render only the visible rows plus a **buffer of 3 rows each side**, recycling DOM nodes from a pool and positioning them with transforms. Use `requestAnimationFrame` for scroll handling, a `ResizeObserver` for width changes, and never read layout in the scroll path. The DOM node count must stay bounded no matter how many images exist.
- **Cells:** a square image area of the slider's size (range about 64 to 320 px in steps of 8, default 128, gap about 8 px), with an optional caption below it (None, Filename or Title from the `gridCaption` setting, one line with an ellipsis and a tooltip). Cell content: the image (never upscaled beyond its natural size, centred, `object-fit: contain`), a **selection indicator** at top-left (a check on hover, focus or when selected, which toggles selection when clicked), and a **heart** at top-right (shown on hover, on focus, or whenever favourited). Show a subtle badge for hidden images when they are included in a view, and for images whose thumbnail failed.
- **Backgrounds:** the image area uses the chosen mode: a checkerboard (CSS gradients, tokens for both themes), a light fill, or a dark fill. The toolbar toggle writes the `checkerboardMode` setting so it stays in sync with Settings. `gridCaption` also takes effect live.
- **Data:** paged from `GET /api/images` with a page size of 200, a total from the first response, and prefetch of the pages around the viewport. Show skeleton cells for rows not loaded yet. Keep at most about 20 pages cached (evict least recently used, never the visible ones). Run at most 4 list requests at once, abort requests for pages that scrolled away, and handle the total changing. **Soft refresh** on `library.changed` (debounced about 300 ms): refetch the total and the visible pages, keep scroll position and the selection by id, and drop selected ids that no longer exist. The ordered id list for range selection, Select all and the preview is fetched lazily from `/api/images/ids` the first time it is needed, cached, and invalidated by any refresh or change of query or sort.
- **Thumbnails:** `<img>` sources are `/api/images/{id}/thumbnail?size=N`, with `N` from `PickFor(cell size times devicePixelRatio)`. Start loading only after scrolling slows (about 100 ms without a large jump), and cancel loads for recycled cells. A pending thumbnail (202) fails to load in an `<img>`, so on error call `fetch` for the same URL, read `X-Thumbnail-State`: `pending` retries with backoff (1, 2, 4, 8 seconds, up to 6 tries, only while the cell is still showing that image) and also retries visible pending cells when a `thumbnails.status` event shows progress; `failed` shows a clear placeholder icon and sets the cell's failed badge. Decode asynchronously and fade images in unless reduced motion is on.
- **Slider and zoom:** changing the size keeps the first visible image anchored. Ctrl+mouse-wheel over the grid changes the size in steps (prevent the browser zoom).
- **Heart:** click toggles favourite through `PATCH /api/images` for that one id. Update the UI at once, revert and show a toast if the request fails. The control is a real button with `aria-pressed` and a label.

### 6. Selection and keyboard

Selection follows normal desktop rules. Implement them in `selection-model.js` and test every case.

- **Mouse:** click selects only that image (and sets the anchor). Ctrl or Cmd click toggles one (moves the anchor). Shift click selects the range from the anchor to the clicked image in the current order, replacing the selection. Ctrl+Shift click adds that range. Clicking empty space clears the selection. Double-click opens the preview. Ranges work across pages that are not loaded, using the ordered id list.
- **Rubber-band:** press and drag on empty background to draw a marquee that selects every image it touches (Ctrl or Cmd adds to the existing selection), with automatic scrolling near the top and bottom edges. Compute hits by geometry, not by DOM queries, and do not start a drag on a cell or control.
- **Keyboard** (the grid is a `role="listbox"` with `aria-multiselectable` and `aria-activedescendant`, with the focus ring on the active cell):

| Key | Action |
| --- | --- |
| Arrow keys | Move focus one cell (Left and Right by 1, Up and Down by one row, clamped to the short last row) and select it |
| Shift + arrows | Extend the selection from the anchor |
| Ctrl or Cmd + arrows | Move focus without changing the selection |
| Space | Toggle the focused image |
| Home, End | First and last cell of the row. With Ctrl or Cmd, the first and last image. |
| Page Up, Page Down | Move by one screen of rows |
| Ctrl or Cmd + A | Select all |
| Escape | Clear the selection (when no overlay is open) |
| Enter | Open the preview (the selection from its first image, or the focused image if nothing is selected) |
| I | With a selection, show or hide the details panel |

The focused cell is always scrolled into view. **Single-key shortcuts are ignored while a text field, select, textarea or content-editable element has focus, while a dialog is open, and while the preview is open** (the preview has its own keys). Register all of them in `shortcuts.js`.

### 7. Preview overlay

Opened by the Preview button, Enter, or double-click.

- Covers the **entire app window** (a fixed full-viewport layer above everything, not the operating-system full screen), a dark backdrop, the image fitted to the window with `contain`. It shows the **original file** from `/api/images/{id}/file` (SVG renders natively and stays sharp). No zoom or pan. The transparency background follows the toggle.
- **Left and Right** keys (and visible Previous and Next buttons) move through the **selection if more than one image is selected, otherwise through the whole current view** (using the ordered ids). Stop at the ends, disabling the button there. Home and End jump to the first and last. Preload the next and previous images.
- A **close button** at the top right and **Escape** close it. Closing returns focus to the grid with the last previewed image focused and scrolled into view. If nothing was selected and Enter opened it, that image becomes the selection.
- **`I` toggles an info overlay** at the bottom right (title, file name, dimensions, file size in human units, format, effective date, and "3 of 12"). Its starting state comes from the `previewInfoOverlayDefault` setting. Toggling inside the preview does not change the setting.
- Show a spinner if the image takes longer than about 150 ms, and an inline "Can't load this image" message (with the navigation still working) if the file is gone. The on-screen controls fade out after about 2 seconds without mouse movement and return on movement or focus.
- Accessibility: `role="dialog"`, `aria-modal`, a label, a focus trap, and the rest of the app made inert (use `inert` if your verified WebKitGTK supports it, otherwise `aria-hidden` plus the focus trap). Respect reduced motion.

### 8. Details panel (read-only for now)

A right-hand panel in the slot reserved in Phase 6: width 320 px by default, resizable between 260 and 480 with the shared resizer, with a slide animation. **Rules:** it slides in when the selection becomes non-empty, unless the user has closed it with its close button, in which case it stays closed until they press `I` or its toolbar button. It slides out when the selection is empty. For one image show a 256 thumbnail on the chosen background, title, file name, full path (click to copy), dimensions, format, size, the four dates, a short hash, tag chips, the rating as read-only stars, favourite and hidden state, and the EXIF fields if present, from `GET /api/images/{id}` (cached, with a loading state). For several images show a count, the combined size, and a short breakdown by format. A note says editing arrives in Phase 8. Phase 8 will replace the content, so keep the container and open and close logic separate from the content.

### 9. Favourites view and live behaviour

The Favourites view uses the same browser view with `favourite=true`. It hides Panel A (as set in Phase 6). A heart click that un-favourites removes the image on the next soft refresh. All views stay mounted, so switching views and visiting Settings preserves scroll, selection, the selected tree node, and the expanded nodes. On startup the app restores the last view and each view's remembered state. The Phase 6 status bar keeps working.

## Out of scope

Editing anything but the favourite heart, tag creation, renaming, deleting or recolouring, the filter bar and query box, the Duplicates, Missing and Help pages, context menus, drag and drop out of the app, open in the system viewer, zoom and pan, slideshow, video, multi-window.

## Required tests

**Pure logic** (import the modules inside the page with `page.EvaluateAsync` and dynamic `import()`, as in Phase 6, so no Node toolchain is needed). Use large data-driven tables.

- **Geometry:** column count for many widths, sizes and gaps; row count; rectangle for each index; visible range and buffer at the top, middle and end of 0, 1, 7, 199, 200, 201, 100,000 and 250,000 items; short last row; index at a point (including gaps and out of range); anchoring when the size changes; marquee hits for many rectangles; total height for the largest case.
- **Selection model:** every mouse rule above including anchor behaviour after Ctrl-click, Shift-click in both directions, Ctrl+Shift ranges, Select all, Clear, retention across a refresh with some ids gone, selection kept when the order changes.
- **Keyboard navigation:** every key at the first, last, middle and short last row, with one column, with one item, with empty data, and the page keys.
- **Image source:** which pages a window needs; prefetch; de-duplicated in-flight requests; aborting stale ones; the concurrency cap of 4; LRU eviction that never drops visible pages; total changes; soft refresh; id list caching and invalidation (use a fake fetch).
- **Thumbnails:** the size choice table for cell sizes and pixel ratios 1, 1.25, 1.5, 2, 3; the backoff schedule; no retry after recycling.
- **Tree model:** visible-node flattening, expand and collapse, every key's result at each position, type-ahead, selection fallback when the node disappears.
- **Source parameters:** the query string each view produces (folder with and without subfolders, year, month, unknown, each date field, tags any and all, favourites, sort fields).

**API tests:** `year=unknown` on the list and ids endpoints for each date field.

**End-to-end** (Playwright for .NET, Chromium and WebKit, as in Phase 6). Seed a database directly in SQL with **100,000 synthetic images** (varied names, sizes, dates including nulls and several years, tags, nested folders, some favourites, some hidden) and start the API against it. Use a separate small **real library** of about 60 generated images (transparent PNGs, JPEG, SVG, GIF, an oversized photo) that is scanned for real, so thumbnails exist.

- **Grid:** renders at once with bounded DOM nodes (assert an upper limit) even for 100,000 items; scrolling to the end is quick and shows the correct last items (`data-index` and `aria-posinset` agree with the API); window resize changes the columns; the slider and Ctrl+wheel change the size and keep the anchor; rapid scrolling does not flood the server (count list and thumbnail requests, assert caps); thumbnails are not requested for rows scrolled past; no memory growth across many scrolls (cap the cached pages).
- **Sort:** every sort field and direction, comparing the first and last visible items to the API result.
- **Thumbnails:** images load (`naturalWidth` greater than 0); the requested `size` follows the slider; a pending thumbnail is retried and appears once ready (delay the worker to force it); a failed thumbnail shows the placeholder and badge; transparent PNGs show over each background mode (assert computed styles and a pixel check on the checkerboard); caption modes change live.
- **Selection with the mouse:** click, Ctrl-click, Shift-click inside and across unloaded pages (assert the exact id set from the API), Ctrl+Shift, click on empty space, Select all, Clear, double-click, indicator click, rubber-band with and without Ctrl, and rubber-band with auto-scroll.
- **Selection with the keyboard:** every key in the table, focus ring and `aria-activedescendant`, always scrolled into view, and single-key shortcuts ignored while typing in a search or settings input, with a dialog open, and with the preview open.
- **Hearts:** optimistic toggle, revert with a toast when the request is made to fail, and removal from the Favourites view after refresh.
- **Trees:** folder structure and counts against the API for several top-level folders and nesting; click shows only that folder's files and expands it; the Include subfolders switch changes totals; keyboard navigation of each tree; years for each date field including Unknown, with counts matching the grid totals; tags alphabetical, single and multi select, any versus all; Show all; refresh keeps expansion and selection; a removed folder falls back to Show all.
- **Preview:** opens at the first selected image, covers the whole viewport, Left and Right through the selection (when more than one is selected) and through the view otherwise, end stops, Home and End, close button and Escape, focus return, the `I` overlay with its setting default, correct info text, PNG, JPEG, SVG and GIF all render, a deleted file shows the message and navigation still works, neighbours are preloaded, controls auto-hide, and the background app is inert.
- **Details panel:** `I` toggle, auto-open and the stay-closed rule, auto-close on empty selection, contents for one and several images, resizing and persistence.
- **State:** switch Folders, Settings, Folders and confirm scroll, selection, tree node and expansion are intact; reload restores sort, size, node and view; a scan adding images updates the grid through `library.changed` without losing scroll or selection.
- No console errors or failed requests (other than deliberate ones) in any test.

**Performance** (print the numbers, loose assertions): time from view open to first rendered row on 100,000 items; during a scripted scroll of the whole list record `requestAnimationFrame` frame times and report the 95th percentile and count of frames over 50 ms; DOM node count; heap use if the browser exposes it.

## Working rules

- Follow the master brief and earlier conventions. Where silent, choose the simplest option and say so. If this prompt conflicts with the brief, follow this prompt and tell me.
- Keep modules small, with short comments on non-obvious decisions only. Never build HTML by concatenating data. Never read the DOM for layout in hot paths.
- Add any dependency to `THIRD-PARTY-NOTICES.md` after checking its licence. Prefer none.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux, with E2E passing when run in both browsers.
- All required tests exist and pass, or you list exactly which could not be written and why.
- `docs/FRONTEND.md` updated with the new browser-feature assumptions, the architecture of the grid and the persistence keys. `shortcuts.js` lists every shortcut.
- Your final reply includes: new files, assumptions, deviations, decisions to confirm, performance numbers, known issues, and the manual checklist with exact commands.

## How I will test Phase 7

1. Scan a folder with a few thousand images (copy a large icon pack if you have one) and let thumbnails finish. Open the app and browse Folders: click folders, watch sub-folders expand, toggle Include subfolders.
2. Scroll quickly through the whole library. It should stay smooth, with placeholders at worst. Drag the slider from smallest to largest and confirm the picture you were looking at stays in view.
3. Switch the background between checker, light and dark with a white icon on transparent. Change the caption in Settings and see it update.
4. Try every selection method: click, Ctrl-click, Shift-click, rubber-band, Ctrl+A, arrow keys, Shift+arrows, Space, Home, End, Page keys, Escape. Confirm the footer counts.
5. Press `I` with a selection and watch the details panel. Close it with its X and confirm it stays closed until you press `I` again.
6. Press Enter or double-click an image. Check that the preview covers the whole window, then use Left and Right with one image selected (whole view) and with five selected (just those). Press `I` for the info overlay, then Escape.
7. Heart a few images, open Favourites, un-heart one, and confirm it disappears.
8. Open Years and try each date field. Open Tags and try Ctrl-click and Match any versus all.
9. Go to Settings and back, and restart the app, to confirm your place is remembered.
10. Run another scan while browsing and confirm the grid updates without jumping.
11. Test on Windows and Linux, a high-DPI display if you have one, and with a trackpad and a mouse wheel.
12. Run `dotnet test`, then the E2E tests in both browsers, and read the performance numbers.
