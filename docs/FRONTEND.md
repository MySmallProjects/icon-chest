# IconChest Front End & Native Web View Specifications

## 1. Native Web View Requirements

IconChest uses **Photino.NET** (version `4.0.16`, Apache-2.0) for desktop execution, which relies on the operating system's native web view engine:

* **Linux:** Uses WebKitGTK and GTK 3 (`libgtk-3-0`).
  * **Ubuntu 22.04 LTS / Linux Mint 21 (Jammy):**
    * Required package: `libwebkit2gtk-4.0-37` (WebKitGTK 2.36 - 2.44) and `libgtk-3-0`.
    * Install via:
      ```bash
      sudo apt update
      sudo apt install libwebkit2gtk-4.0-37 libgtk-3-0
      ```
  * **Ubuntu 24.04 LTS / Linux Mint 22 (Noble):**
    * Ubuntu 24.04 transitioned to `libwebkit2gtk-4.1-0` (libsoup3). Photino.NET 4.0.16 native Linux assets (`Photino.Native.so`) are dynamically linked against `libwebkit2gtk-4.0.so.37`.
    * To run with native window on Ubuntu 24.04, install the 4.0 runtime or pull the package from the Ubuntu 22.04 (jammy) security archive:
      ```bash
      sudo apt install libwebkit2gtk-4.0-37
      ```
    * **Browser Fallback:** If the WebKitGTK 4.0 runtime library is missing or cannot be installed, IconChest provides a built-in browser fallback:
      ```bash
      iconchest-desktop --browser
      ```
      This boots the API child process on an ephemeral port and opens the system default web browser automatically.

* **Windows:** Uses Microsoft Edge WebView2 Evergreen Runtime.
  * Windows 11 and recent builds of Windows 10 include the WebView2 Runtime pre-installed.
  * Detection: IconChest checks for the presence of the WebView2 Evergreen registry key:
    `SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}`
    under `HKEY_LOCAL_MACHINE` and `HKEY_CURRENT_USER` (handling 64-bit WOW6432Node paths). If the `pv` version value is missing, empty, or `0.0.0.0`, the runtime is reported missing.
  * Download: [Microsoft WebView2 Evergreen Bootstrapper](https://go.microsoft.com/fwlink/p/?LinkId=2124703).
  * If missing, the app displays a clear guidance message and allows running with `--browser`.

---

## 2. Minimum Web Engine Baseline & Feature Compatibility

Based on WebKitGTK 2.36+ (Safari 15.4 engine baseline, ~early 2022) and modern WebView2 (Chromium 100+), the following web platform capabilities are verified safe for production use in IconChest:

| Web Feature | Supported | Notes |
| :--- | :--- | :--- |
| **ES6 Modules** (`import`, `export`) | Yes | Standard in all target engines. |
| **Dynamic `import()`** | Yes | Supported since WebKitGTK 2.24+ / Chrome 63+. |
| **CSS Custom Properties** (Variables) | Yes | Used extensively for Light/Dark themes and design tokens. |
| **CSS Grid & Flexbox** | Yes | Used for 3-column app shell and responsive layouts. |
| **Native `<dialog>` Element** | Yes | Supported in WebKitGTK 2.36+ (Safari 15.4) and Chromium 37+. Provides modal accessibility and focus trapping. |
| **`color-scheme` CSS Property** | Yes | Supported in WebKitGTK 2.36+ and Chromium 81+. Syncs scrollbars and native form elements with Light/Dark themes. |
| **Pointer Events API** | Yes | `pointerdown`, `pointermove`, `pointerup`, `setPointerCapture`, `releasePointerCapture`. Used for pane resizing. |
| **Fetch API & AbortController** | Yes | Full request cancellation and timeout support. |
| **Server-Sent Events (`EventSource`)**| Yes | Used for live status updates (`/api/events`). |
| **Clipboard API** | Yes | `navigator.clipboard.writeText` with fallback to `document.execCommand('copy')`. |
| **Web Storage (`localStorage`)** | Yes | Used for immediate theme boot-up and local pane state. |
| **Drag & Drop Events** | Yes | Handled globally to prevent dragging external files from causing navigation. |

### Features Intentionally Avoided
* CSS `@starting-style` and CSS Transitions on display changes (WebKitGTK 2.44+).
* Popover API (`popover` attribute) (WebKitGTK 2.42+).
* CSS masonry layout or CSS subgrid edge cases.
* Custom elements / shadow DOM (not needed; plain DOM helpers provide superior simplicity and security).

---

## 3. Security & Content Security Policy (CSP)

The front end strictly adheres to the server-enforced Content Security Policy:
* `default-src 'self'`: Only same-origin resources.
* `script-src 'self'`: No inline `<script>` tags without src, no `eval()`, no dynamic string execution (`new Function()`).
* `style-src 'self'`: External `.css` stylesheets only. No inline `<style>` tags or inline `style="..."` attributes on elements (style mutations must occur via CSS classes or the CSSOM).
* `img-src 'self' data:`: Local images, thumbnails, and inline SVG data URIs.
* `connect-src 'self'`: API endpoints and `/api/events` EventSource.

---

## 4. Cache Control & Asset Revalidation

All static front-end assets (`index.html`, `js/**`, `css/**`) are served with:
```http
Cache-Control: no-cache
```
accompanied by an `ETag`. The browser always validates whether the assets have changed before serving from local cache, guaranteeing that application upgrades immediately take effect without stale cached modules.

---

## 5. Browse and View Architecture (Phase 7)

### 5.1 Pure Arithmetic & Logic Modules
All algorithms that do not require DOM access are isolated into testable, data-agnostic ES modules:
* `grid-geometry.js`: Computes columns, rows, cell bounding boxes, visible index ranges with top/bottom buffer rows, scroll anchoring when changing thumbnail sizes, and marquee axis-aligned bounding box (AABB) hit testing.
* `selection-model.js`: Implements multi-selection state machine (click, Ctrl-toggle, Shift-range, Ctrl+Shift-range, select all, clear, and valid ID retention).
* `keyboard-nav.js`: Calculates 2D grid matrix navigation indices for arrow keys, Home, End, PageUp, PageDown, and short terminal rows.
* `thumb-loader.js`: Selects thumbnail size (`64`, `128`, `256`) based on cell size and device pixel ratio (`window.devicePixelRatio`), and schedules 202 retry attempts with exponential backoff (1s, 2s, 4s, 8s).
* `image-source.js`: Paged image loading (200 items/page), LRU cache eviction (max 20 pages, protecting visible pages), in-flight de-duplication, aborting stale fetch requests, soft refresh, and lazy ordered ID caching.
* `tree-model.js`: Hierarchical node flattening, WAI-ARIA arrow key navigation, parent expansion, and type-ahead character matching.
* `shortcuts.js`: Single-source registry of keyboard shortcuts for grid navigation, full-window preview, and tree management.

### 5.2 Virtual Grid Implementation (`virtual-grid.js`)
* **Bounded DOM Pool:** Maintains a fixed pool of recycled DOM cell elements (`~80-150` elements depending on window height), positioning them via hardware-accelerated CSS `transform: translate3d(x, y, 0)`.
* **Zero Layout Trashing:** Never calls `offsetHeight` or `getBoundingClientRect` during the scroll hot path; scroll coordinates are mapped directly to row indices via arithmetic.
* **Marquee Selection:** Pointer down on unoccupied grid space initiates rubber-band selection with edge auto-scrolling and live arithmetic hit testing.
* **Background Modes:** Provides checkerboard, light, and dark transparency backgrounds for icons with alpha channels.

### 5.3 Modal Preview & Details Drawer
* `preview.js`: Full-viewport modal overlay (`role="dialog"`, `aria-modal="true"`) setting `inert` on the application root. Supports navigation arrows, preload of adjacent images, and an info HUD toggled via the `I` key.
* `details-panel.js`: Collapsible 260–480px side drawer displaying image dimensions, file size, hash, EXIF metadata, tag chips, and read-only star ratings.

---

## 6. Details and Editing Architecture (Phase 8)

### 6.1 Native Form Input Compatibility in WebKitGTK 2.36+
* **`<input type="color">`**: In WebKitGTK 2.36+ without the `xdg-desktop-portal` or GTK color chooser integration, native color dialogs fail or behave unpredictably across distributions.
  * **Architectural Decision:** An accessible custom color chooser dialog is used: 16 curated swatches meeting WCAG 2.1 $\ge 4.5:1$ contrast against their text, a validated hex code input with live non-blocking contrast ratio warnings against both light (`#ffffff`) and dark (`#1f2937`) backgrounds, and an immediate preview badge.
* **`<input type="date">`**: In WebKitGTK 2.36–2.40 on Linux, `<input type="date">` renders as an unstyled text field without a drop-down calendar widget.
  * **Architectural Decision:** An accessible text input with strict `YYYY-MM-DD` validation and quick-fill buttons ("Use EXIF date", "Use created date", "Clear") provides identical, rock-solid cross-platform ergonomics across all target engines.

### 6.2 Pure Front-End Modules
* `lib/contrast.js`: WCAG 2.1 relative luminance calculation, contrast ratio evaluation ($L_1+0.05 / L_2+0.05$), hex normalization/parsing, and 16 accessible color pairs ($\ge 4.5:1$).
* `lib/tag-combobox-model.js`: Prefix-first and substring ranking with Unicode-aware case folding, inline tag creation evaluation (valid 1–50 character trimmed string, no control characters, exact-match selection vs new tag creation).
* `lib/tags-store.js`: Centralized in-memory tag store with subscriber pattern, SSE `library.changed` auto-refresh, and optimistic mutations with rollback.
* `lib/edit-model.js`: Multi-image edit state aggregator, dirty field tracking, `PATCH /api/images` payload generation (emitting only dirty fields, empty string for title reset, explicit `null` for cleared description/date, tri-state tag diffing into `addTagIds` and `removeTagIds`), calendar date and range validation.

### 6.3 UI Components & Interactions
* `components/context-menu.js`: WAI-ARIA `role="menu"` and `role="menuitem"` popup supporting pointer right-click, `Shift+F10`, `Menu` key, type-ahead, arrow navigation, Escape dismissal, and focus return.
* `components/tag-chip.js`: Accessible tag badges with tri-state cycle (`1` all ✓, `0.5` some +, `0` removed ×), context menu (Rename `F2`, Recolour, Delete `Delete`).
* `lib/dialog.js`: Three-button unsaved changes guard (Save, Discard, Cancel; Cancel strictly preserves focus and selection state), Rename dialog, Recolour dialog with swatches and live contrast warning, Delete confirmation dialog respecting `confirmBeforeDelete`.
* `components/details-panel.js`: Full editable details drawer with sticky header dirty indicator, single and bulk multi-edit (up to 10,000 images), rating star picker, local native file launcher (`POST /api/native/open-file`), duplicates list (`GET /api/images/{id}/duplicates`), and `Ctrl+S` / `Cmd+S` keyboard save shortcut.


