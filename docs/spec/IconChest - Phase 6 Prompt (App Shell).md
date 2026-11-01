# IconChest Phase 6 Prompt: The App Shell

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 5 code. Those phases must be complete and their tests passing.

## Goal

Turn IconChest into a real desktop application. Build the Photino desktop shell that launches and supervises the API, and the front-end skeleton it displays: the three-column layout, resizable and collapsible panes, the icon bar, light and dark themes, live status, and a Settings page with working Library, Appearance, Behaviour and App info sections. After this phase you can launch the app, add a folder, scan it and watch progress. Browsing images is Phase 7.

## Before you write any code

1. Read the existing solution. Re-read brief sections 3, 12 and 13, and the Phase 4 README (child-process contract, auth).
2. Verify the current Photino.NET version, its native requirements, and the **exact** Linux package name it needs for Linux Mint 21 and 22 (Ubuntu 22.04 and 24.04 bases). On Windows, check how to detect a missing WebView2 runtime. Check the minimum WebKitGTK version you will really get, and use it to decide which web features are safe. Record the findings in `docs/FRONTEND.md`.
3. Reply with a short plan and assumptions, then build.

## Part A: the desktop shell (`IconChest.Desktop`)

### A1. Structure and testability

Keep the Photino code to one thin class. Put everything else in plain, testable classes: `SingleInstanceGuard`, `ApiProcessManager`, `ReadinessLineParser`, `SecretGenerator`, `WindowStateService` and `NavigationPolicy`. Add a test project `IconChest.Desktop.Tests` and a tiny console helper project `IconChest.TestApiStub` that imitates the API's process behaviour for tests (prints the readiness line, delays it, never prints it, exits early, ignores shutdown).

### A2. Startup sequence

1. Acquire the single-instance lock (A5). If another instance holds it, signal that instance to focus its window and exit with code 0.
2. Load settings through Core's `SettingsService` (the shell may read and write only `settings.json`, never the database).
3. Generate a per-launch secret (32 bytes from a cryptographic RNG, base64url) with `SecretGenerator`.
4. Start the API as a child process: `--port 0 --parent-pid <shell pid>`, with the secret in the environment variable `ICONCHEST_API_SECRET`, and `ICONCHEST_*` path overrides inherited. Locate the API in this order: environment variable `ICONCHEST_API_PATH`, then `api/` beside the shell (A9). Capture stdout and stderr. Log child stderr at Debug under the source `api-child`. Never log the environment or the secret.
5. Read stdout until the `ICONCHEST_READY <url>` line (30-second timeout), then confirm `GET /health`.
6. Create the window (A4) and load the URL. Hand the secret to the page (A3).
7. On any failure in steps 4 to 6, show an error page rendered locally with the reason and the log file location, log at Error, kill the child, and exit with a non-zero code when the window closes. The shell must never hang with no window.

### A3. Login handoff (no secret in a URL)

The session cookie must end up in the web view. Use Photino's JavaScript message bridge: the page asks the shell for the secret, the shell answers only if the message came from the API's own origin, and the page posts it to `POST /api/session`. The secret is never put in a URL, never written to disk, and never shown. As a **fallback** for the `--browser` mode (A8), where no bridge exists, open the default browser at `<url>/#handoff=<secret>`; the front end reads it, removes it with `history.replaceState` straight away, and logs in. Document that this puts the per-launch secret in that browser's history and why that is acceptable (loopback only, dies at exit).

### A4. Window

- Title "IconChest", the app icon (create a simple placeholder icon in `.png` and `.ico`), minimum size 800x500.
- Restore size, position and maximised state from `settings.json` (`window` and `rememberWindowState`). If the saved position is off every screen (a monitor was unplugged), centre on the primary screen. Clamp to the minimum size.
- Save the window state when it changes (debounced to about 500 ms) and on close, by updating only the `window` key through the settings service (no clobbering other keys, safe with concurrent writes from the API).
- DevTools only when started with `--devtools` or in a Debug build.

### A5. Single instance

Per-user scope. Windows: a named mutex whose name includes the user. Linux: an exclusive file lock in the runtime directory (`$XDG_RUNTIME_DIR`, with a fallback to the data folder). The first instance listens on a small local channel (named pipe or Unix domain socket in the same directory) for a "focus" message, and when it arrives it restores and focuses the window. A stale lock from a crashed process must not block a new launch. Test it: two guards with the same name, release then re-acquire, a crashed holder, concurrent acquisition, and the focus signal.

### A6. Lifecycle

On window close: call `POST /api/system/shutdown` (using the secret to log in with a short-lived `HttpClient`), wait up to 5 seconds for the child to exit, then kill the process tree. On Windows, also put the child in a Job Object with kill-on-close, so a shell crash or kill takes the child with it. On Linux the API's `--parent-pid` watchdog is the backstop, so confirm it exits within a few seconds of the shell being killed with `kill -9` (test with the stub and add a manual step). Handle SIGTERM and Ctrl+C in the shell by running the same shutdown path.

### A7. Navigation and native behaviour

`NavigationPolicy`: allow navigation only within the API origin. A link to any other `http` or `https` address is cancelled and opened in the system browser (`xdg-open`, or the shell-execute equivalent on Windows). Any other scheme is blocked. Honour bridge messages only from the API origin. Provide one native bridge command now: **`pickFolder`**, which opens Photino's native folder dialog and returns the chosen path (or null). The front end shows a Browse button only when the bridge exists. Dropping a file onto the window must not navigate away (also prevented in the front end).

### A8. Fallbacks and errors

- `--browser`: skip the web view, start the API, open the default browser at the handoff URL, and keep running until Ctrl+C. This is the documented fallback if Photino fails on a platform.
- If the Linux web view library or the Windows WebView2 runtime is missing, print and show a clear message with the exact package or download to install (from your verified research), and suggest `--browser`.

### A9. Build layout

After `dotnet build src/IconChest.Desktop`, the output must contain the API in an `api/` sub-folder (use a build target that copies it, with the `ProjectReference` set not to reference the assembly), so the shell can start it with no environment variables. Proper packaging is Phase 11. Explain your approach in the README.

## Part B: the front end (`src/IconChest.Api/wwwroot`)

### B1. Rules

- Plain HTML, CSS and ES modules. No framework, no bundler, no build step, no CDN. Everything is served from the API. Remember the Phase 4 CSP: no inline scripts, no inline event handlers, no remote resources. The few styles set from script must go through the CSSOM or classes.
- Layout of the folder: `index.html`, `css/` (`tokens.css`, `base.css`, `layout.css`, `components.css`, `themes.css`), `js/` (`boot.js` as a small blocking classic script for the theme, `app.js` as the module entry, `lib/` with `api.js`, `events.js`, `store.js`, `dom.js`, `dialog.js`, `toast.js`, `resizer.js`, `views/` with one module per view, `settings/` with one module per settings section).
- A tiny `dom.js` helper (create elements, set attributes and events, no innerHTML with data) keeps views readable and safe. **Never build HTML from server data with string concatenation.**
- Use only web features that work in the oldest WebKitGTK you identified and in current WebView2. List what you rely on in `docs/FRONTEND.md`, with the minimum versions.
- Make the static assets revalidate on every load (`Cache-Control: no-cache` with ETag for `.js` and `.css` as well as `index.html`) so an upgrade can never leave stale modules in the web view. Make that small API change.
- Accessibility baseline: landmarks, visible focus, keyboard-operable everything, ARIA roles for the icon bar, the resizer and dialogs, `aria-live` for toasts, `prefers-reduced-motion` respected, text contrast at least WCAG AA in both themes.

### B2. Boot and auth

On load: `GET /api/session`. If not authenticated, in this order: use the shell bridge if present; else the `#handoff` fragment; else show a **login page** (password field, error messages, shows the retry delay when throttled with 429). After login, fetch settings and system info and start the app. The `api.js` wrapper: JSON in and out, `ApiError` built from problem details (status, title, detail, `errors` map, `traceId`), `AbortController` support, no accidental caching, and one global rule: on 401 mid-session, re-run the login flow (silently through the bridge in the desktop app, or show the login overlay in the browser) without losing UI state. `events.js` wraps `EventSource` for `/api/events` with automatic reconnect and backoff, typed subscriptions, and a `resync` notification after reconnect.

### B3. Layout

Two regions in a full-window grid: a thin **icon bar** (about 48 px) on the far left, then the working area, with a slim **status bar** (about 24 px) along the bottom. The working area holds **Panel A** (resizable and collapsible), a drag handle, and **Panel B** (flexible). A right-hand details panel arrives in Phase 8, so leave a clear slot for it in the structure.

- **Panel A** has a default width of 280 px, a minimum of 180 and a maximum of 600 (also capped to 50% of the window). Resize by dragging the handle (pointer events with capture, so fast drags and leaving the window work), by keyboard (arrow keys on the focused handle, Shift for bigger steps, Home and End for the limits), and double-click resets to the default. Persist the width and the collapsed state per device. The handle is a `role="separator"` with `aria-valuenow`, `aria-valuemin` and `aria-valuemax`.
- Clicking the icon of the **active** view toggles Panel A (hide and show). Hiding it gives Panel B the full width.
- Views that have a tree (Folders, Years, Tags) show Panel A. Views that do not (Favourites, Duplicates, Missing, Help, Settings) hide it and give Panel B the full width. Panel A's own state (width, collapsed) is remembered and comes back when you return to a tree view.
- **Every view stays mounted** in the DOM once opened and is hidden when inactive, so scroll position, form state and (later) selection and filters are preserved when switching views and when entering and leaving Settings.
- Responsive: below about 900 px width the layout must still be usable (Panel A overlays rather than squeezes Panel B).

### B4. The icon bar

Data-driven from a registry so later phases only add entries: `{ id, title, icon, group: top|bottom, hasPanelA, loadView }`. Entries now: top group **Folders, Years, Tags, Favourites, Duplicates, Missing**; bottom group **Help, Settings**. Hand-drawn inline SVG icons (or a permissively licensed icon set with its notice added), `title` and `aria-label` on each, an obvious active state, and arrow-key navigation inside the bar. Only **Settings** is functional now. The other views show a clean placeholder in Panel B (and in Panel A for the tree views) saying what arrives in which phase. Routing uses the URL hash (`#folders`, `#settings`, `#settings/library`) so the browser back button works in website mode, and the last view is remembered. On startup open the `defaultView` setting.

### B5. Themes

Light and Dark through CSS custom properties defined in `tokens.css` and `themes.css`, switched by `data-theme` on `<html>` and `color-scheme` set to match. `boot.js` (a small external blocking script in `<head>`) applies the last known theme from `localStorage` before first paint to avoid a flash, then the app reconciles with the server setting. Changing the theme in Settings applies instantly and saves. Design a restrained, professional look: neutral palette, one accent colour, consistent spacing scale, subtle borders, rounded corners, clear typography using the system font stack, and a good focus ring. Provide design tokens for a later checkerboard and tag colours.

### B6. Shared components

- **Status bar:** left side shows scan activity (for example "Scanning Icons: 1,204 files"), right side shows thumbnail progress ("Thumbnails: 318 of 1,520") and hides when idle. Fed by the Phase 4 events, with a `resync` fetch after reconnect. A small connection indicator appears when the event stream is down.
- **Toasts:** non-blocking, stacked, auto-dismiss (errors stay longer), `aria-live`, with a dismiss button.
- **Dialog:** an accessible confirm and message dialog (prefer the native `<dialog>` element if your verified WebKitGTK supports it, otherwise a fallback), focus trap, Escape to cancel, returns a promise. Used for destructive confirms.
- **Copy button** helper using the clipboard API with a fallback, and a short "Copied" confirmation.
- **Prevent file drops** from navigating: handle `dragover` and `drop` on the document.

### B7. The Settings page

A page in Panel B's full width with a left sub-navigation of sections: **Library, Appearance, Behaviour, Performance, Cleanup, Logging, API, Data, App info** (the brief's section 13). Each section is its own module and has a URL (`#settings/library`). Sections not built yet (Performance, Cleanup, Logging, API, Data) show a short "Coming in Phase 10" panel. Settings controls **save on change** (debounced for text inputs) with `PATCH /api/settings`, show inline problem-details errors beside the control, revert the control on failure, and show a reusable "restart required" banner when the response lists `restartRequired`.

**Library section**

- A list of library folders: path, image count, last scan time, an "include in scans" checkbox, a **Scan** button, a **Remove** button. A **Scan all** button above the list.
- **Add folder:** a text box plus **Add**; and a **Browse...** button only when the native bridge exists (it fills the box via `pickFolder`). Show server validation errors clearly (does not exist, nested folder, already added).
- **Scan:** starts the job, then shows live progress in that folder's row from `scan.progress` events (stage, counts, current file name truncated in the middle, a progress bar when a total is known), a **Cancel** button, and on completion a summary line (new, changed, moved, missing, duplicates, errors) that stays until dismissed or the next scan. Handle a 409 (already scanning) gracefully. Resume showing progress if the page is opened or reloaded while a scan is running (read `GET /api/scans`).
- **Remove:** confirm dialog stating that the folder's records and thumbnails are removed from IconChest and that **no files on disk are touched**.
- Fields for **include patterns**, **exclude names** (with a **Reset to defaults** link each) and **scan on startup**.

**Appearance section:** theme (Light or Dark), grid caption (None, Filename, Title), checkerboard mode (Checker, Light, Dark), preview info overlay default. They save now and take effect in later phases (theme takes effect immediately).

**Behaviour section:** default view on startup (the registry's views), remember window state, confirm before delete.

**App info section:** version, build date, executable location, current log file path, log folder, platform, API port, database path and schema version, hash algorithm (read-only). A copy button on each path. A **Third-party licences** button opens a dialog showing the notices text from the new endpoint below.

## Part C: API additions

- `GET /api/system/licenses` returns the text of `docs/THIRD-PARTY-NOTICES.md` (embed it at build time). Update the notices for every new dependency in this phase (Photino.NET, Playwright, and any icon set).
- The `no-cache` rule for static `.js` and `.css` (B1).
- Anything else you find the front end genuinely needs: list it and keep it minimal.

## Out of scope

Browsing, trees with data, the grid, selection, preview, the details panel, filters and search UI, the Duplicates, Missing and Help pages, remaining Settings sections, packaging, installers, auto-start, tray icon, native "open in viewer" and "open log folder" actions.

## Required tests

All must pass on Linux and Windows unless marked.

**Shell unit tests (`IconChest.Desktop.Tests`)**

- `SingleInstanceGuard`: second acquire fails, release then re-acquire works, a crashed holder does not block, concurrent attempts yield exactly one winner, the focus signal is delivered.
- `ReadinessLineParser`: valid line, extra output around it, malformed lines, an `ICONCHEST_` prefix collision, very long output.
- `SecretGenerator`: length, encoding, uniqueness across many calls.
- `ApiProcessManager` with the stub: starts and reads the port; readiness timeout; early exit with the error reported; shutdown through the endpoint (fake) then forced kill after the timeout; process tree killed; stderr captured; the secret never appears in logs or in the arguments; parent-death behaviour of the stub watchdog.
- `WindowStateService`: restore, off-screen position recovery, minimum size, maximised round trip, debounce, and a concurrent write with another settings update loses nothing.
- `NavigationPolicy`: same origin allowed, other `http` and `https` sent to the browser, other schemes blocked, bridge messages from other origins ignored.

**API tests:** the licences endpoint (authenticated only), the cache headers on static assets, and the `--parent-pid` watchdog (the API exits after its parent dies, if not already covered).

**Front-end tests.** Add `tests/IconChest.E2E.Tests` using Playwright for .NET, running against a standalone API started on a random port with temp folders and the path overrides. Run them in Chromium and WebKit. Tests skip with a clear message if the browsers are not installed (document `playwright install`), and carry a trait so the default `dotnet test` run can exclude them. Pure JavaScript logic (`store.js`, `resizer.js` clamp maths, `api.js` error parsing, the settings diff) is tested by importing the modules inside the page (`page.EvaluateAsync` with dynamic `import()`), so no Node toolchain is needed.

- Boot and auth: login with the secret through the API context, then the app loads; the password login page, wrong password message, throttling message; 401 mid-session returns to login without losing state.
- Layout: all eight icons present in order with accessible names; Panel A visible for Folders, Years and Tags and hidden for the others; Panel B widens in Settings and Panel A returns with its width intact; clicking the active icon toggles Panel A; drag, keyboard and double-click resizing with clamping; width and collapsed state persist across reload; views stay mounted (type in a settings field, switch away and back, the value is still there); narrow-window behaviour.
- Theme: switching changes `data-theme` and computed colours at once, persists across reload, and `boot.js` applies the stored theme before the app module runs (assert order).
- Settings pages: each implemented section saves and reloads correctly; an invalid value shows the inline error and reverts; the restart-required banner appears for a setting that needs it; section deep links work.
- Library: add a valid folder; nonexistent, nested and duplicate adds show the right messages; scan a generated folder of about 300 tiny images and see live progress then the summary; cancel a scan; reload during a scan and see it resume; remove with the confirm dialog (cancel keeps it, confirm removes it, files still on disk); include and exclude edits and reset to defaults.
- Status bar and events: thumbnail progress appears and clears; the connection indicator shows when the stream drops.
- App info: every value matches `/api/system/info`; the licences dialog opens and is readable; copy buttons work.
- Keyboard and accessibility: icon bar arrow-key navigation, dialog focus trap and Escape, visible focus, no console errors during all of the above (fail the test if one occurs).
- Dropping a file onto the page does not navigate.

**Manual desktop checks** are listed below and must be run on both Windows and Linux before this phase is accepted.

## Working rules

- Follow the master brief and earlier conventions. Where silent, choose the simplest option and say so. If this prompt conflicts with the brief, follow this prompt and tell me.
- The shell holds no business logic and never touches the database. The front end never reads files or the database.
- Keep modules small and single-purpose, with short comments on non-obvious decisions only.
- Add every dependency to `THIRD-PARTY-NOTICES.md` after checking its licence.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux (E2E excluded by default, passing when run), with no warnings in Core and Api.
- All required tests exist and pass, or you list exactly which could not be written and why.
- `docs/FRONTEND.md` records the verified browser-feature assumptions, and the README documents running the shell, the browser fallback, the Linux and Windows prerequisites and the handoff design.
- Your final reply includes: new files, assumptions, deviations, decisions to confirm, known platform issues, and the manual checklist with exact commands.

## How I will test Phase 6

1. Install the prerequisites you documented. `dotnet build`, then run the shell with `dotnet run --project src/IconChest.Desktop`. Confirm a window opens without a login prompt and shows the app.
2. Resize, move and maximise the window, close it, reopen it, and confirm the state is remembered. Move it off-screen in the settings file and confirm it recovers.
3. Launch a second copy and confirm the first window is focused and the second exits.
4. While running, check the process list: one shell and one API. Kill the shell with `kill -9` (Windows: end the task) and confirm the API disappears within a few seconds. Repeat with a normal close.
5. Drag Panel A's handle, use the keyboard on it, double-click it, and click the active icon. Open Settings and confirm Panel A disappears and Panel B widens, then return.
6. Switch theme in Settings and watch it change instantly. Restart and confirm no flash of the wrong theme.
7. In Settings, Library: use Browse to add a folder, scan it, watch live progress and the status bar, cancel one scan, run another, then remove a folder and confirm the files on disk remain.
8. Open App info and check the version, build date, paths, and the licences dialog. Build twice and confirm the version changes.
9. Click an external link (if one exists in the licences text) and confirm it opens in the system browser, not the app. Drop an image file onto the window and confirm nothing happens.
10. Run `dotnet run --project src/IconChest.Desktop -- --browser` and confirm it works in your default browser.
11. Temporarily remove the web view library (or test on a clean VM) and confirm you get a clear message.
12. With `ss` or `netstat`, confirm the API listens on 127.0.0.1 only.
13. Run `dotnet test`, then the E2E tests in both browsers.
