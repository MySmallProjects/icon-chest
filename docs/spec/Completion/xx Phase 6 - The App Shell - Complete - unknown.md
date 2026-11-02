
I'm not sure where this text came from?

# Phase 6: The App Shell — Implementation Complete

## Overview
Phase 6 turns IconChest into a desktop application by implementing:
1. The **Photino.NET Desktop Shell** (`IconChest.Desktop`) hosting the API child process (`IconChest.Api`), managing single-instance locks, window state persistence, navigation policy, and secure session handoff.
2. The **Front-End Skeleton** (`src/IconChest.Api/wwwroot`) using modern vanilla ES modules, HTML5, and CSS3 without frameworks or bundlers under strict Content Security Policy.
3. Accessible components: native `<dialog>` modals with focus trapping, stackable `aria-live` toasts, responsive resizable/collapsible 3-column layout, dark/light theme switching without flash of unstyled content (FOUC).
4. Settings page with working Library, Appearance, Behaviour, and App Info sections (and "Coming in Phase 10" placeholders for remaining sections).
5. Comprehensive test coverage:
   - `IconChest.TestApiStub`: test stub simulating all child process modes.
   - `IconChest.Desktop.Tests`: 50 unit tests for shell logic.
   - `IconChest.E2E.Tests`: Playwright in-browser unit tests and full end-to-end suite.

---

## Deliverables & Key Changes

### 1. Documentation
- `docs/FRONTEND.md`: Records WebKitGTK runtime requirements across Linux distros (`libwebkit2gtk-4.0-37` on Ubuntu 22.04 / Mint 21; `libwebkit2gtk-4.1-0` on Ubuntu 24.04 / Mint 22), WebView2 registry checks on Windows, baseline WebKitGTK 2.36+ feature compatibility, and browser fallback mode.
- `docs/THIRD-PARTY-NOTICES.md`: Updated with Photino.NET (4.0.16, Apache-2.0) and Microsoft.Playwright (1.49.0, Apache-2.0).
- `README.md`: Updated with desktop run instructions, browser fallback flag (`--browser`), and package prerequisites.

### 2. Desktop Shell (`IconChest.Desktop`)
- Thin host pattern: `DesktopApp.cs` wraps Photino, while pure business logic is decoupled into testable components:
  - `SingleInstanceGuard`: Linux exclusive lockfile (`~/.iconchest/iconchest.lock`) + Unix Domain Socket; Windows named mutex (`Local\IconChest_SingleInstance_Mutex`) + Named Pipe. Signals first instance to bring window to foreground.
  - `ApiProcessManager`: Launches API child process (`--port 0 --parent-pid <pid>`), passes session secret via `ICONCHEST_API_SECRET`, parses readiness line (`ICONCHEST_READY <url>`), performs health check, and ensures clean shutdown (`POST /api/system/shutdown` + 5s kill tree fallback, plus Windows Job Object).
  - `ReadinessLineParser`: Parses `ICONCHEST_READY <url>` stdout line.
  - `SecretGenerator`: 32 crypto-random URL-safe base64 bytes.
  - `WindowStateService`: Persists bounds to `settings.json`, clamps to min 800x500, handles off-screen monitor disconnect recovery, and debounces writes (500ms).
  - `NavigationPolicy`: Intercepts non-origin URLs and opens them in default external browser (`xdg-open` / Windows ShellExecute).
  - `--browser` fallback mode: Launches API child process and opens system browser directly.

### 3. Front-End Skeleton (`src/IconChest.Api/wwwroot`)
- **CSS Architecture**:
  - `tokens.css`: Core design tokens (spacing, typography, transitions, z-index).
  - `themes.css`: Light and Dark theme palette variables.
  - `base.css`: CSS reset, font declarations, high-contrast and reduced-motion media query handling.
  - `layout.css`: 3-column layout (48px icon bar, resizable & collapsible Panel A, flexible Panel B, 28px bottom status bar).
  - `components.css`: Buttons, inputs, forms, tree rows, modals, toasts, badge chips.
- **JavaScript Core & Libs**:
  - `boot.js`: Executed synchronously in `<head>` to read `localStorage.getItem('iconchest_theme')` and apply the theme class to `<html>` prior to first paint.
  - `lib/dom.js`: Helper for building DOM trees safely using `document.createElement`, attributes, event listeners, and text nodes (avoiding `innerHTML` injection).
  - `lib/api.js`: Fetch wrapper handling desktop bridge secret handoff, `#handoff` fragment login, and silent 401 re-authentication.
  - `lib/events.js`: Server-Sent Events client for `/api/events` with reconnection backoff and `resync` trigger.
  - `lib/store.js`: Reactive observable store with slice subscriptions.
  - `lib/dialog.js`: Accessible native `<dialog>` confirm and alert modals with focus trapping and restore.
  - `lib/toast.js`: Stacked `aria-live="polite"` toast notifications with auto-dismiss.
  - `lib/resizer.js`: Pointer capture, keyboard arrow step resizing, double-click reset, 180px-600px clamping.
- **Views**:
  - Main views: `folders.js`, `years.js`, `tags.js`, `favourites.js`, `duplicates.js`, `missing.js`, `help.js`, `settings.js`.
  - Settings panels: `library.js` (add folder via bridge/dialog, trigger scans, view live scan progress, toggle include/exclude, remove), `appearance.js` (theme selection, preview), `behaviour.js` (window state toggle, delete confirmation toggle), `appinfo.js` (versions, paths, copy buttons, third-party licenses modal), and placeholders for upcoming Phase 10 sections.
  - `app.js`: Root application controller, data-driven 8-icon bar, hash router (`#folders`, `#settings`, etc.), DOM view preservation.

### 4. Tests
- Total Solution Tests Passing: **473 tests** across 4 projects:
  - `IconChest.Core.Tests`: 369 tests
  - `IconChest.Api.Tests`: 45 tests
  - `IconChest.Desktop.Tests`: 50 tests
  - `IconChest.E2E.Tests`: 9 tests (E2E & in-browser module tests)

