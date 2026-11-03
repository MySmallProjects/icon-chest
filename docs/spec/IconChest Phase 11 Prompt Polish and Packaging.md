# IconChest Phase 11 Prompt: Polish and Packaging

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 10 code. Those phases must be complete and their tests passing. This is the last phase: it adds no features. It makes IconChest installable, trustworthy, fast and finished.

## Goal

Produce release-quality builds for **Windows and Linux**, with packages a user can install on a clean machine; finalise licences and documentation; audit and tune performance, accessibility, security and data safety; and leave a repeatable release process. The result is a release candidate for version 1.0.

## Before you write any code

1. Read the whole solution, `README.md`, `docs/`, the test projects and the Phase 6 shell notes (WebKitGTK package name, WebView2 detection).
2. Check which packaging tools you can really run on a Linux build machine (Linux Mint, .NET 10 SDK). In particular verify whether a Windows installer can be produced there (for example with WiX as a cross-platform .NET tool, or Inno Setup under Wine) before promising one. Report what you find.
3. Reply with a short plan, the list of deliverables you will and will not meet, and your assumptions. Then build.

## Scope

### 1. Release builds and publishing

- Scripts under `build/` (shell scripts for Linux, PowerShell for Windows where needed) that produce, from a clean checkout, everything under `artifacts/` with SHA-256 checksum files: `publish` for `win-x64` and `linux-x64` (arm64 only if it is trivial and verified), the packages in section 2, and a developer archive of the CLI tool (not part of the user packages).
- **Self-contained**, no .NET installation needed. Evaluate and measure these options, and choose with reasons in `docs/PERFORMANCE.md`: ReadyToRun for faster startup; publishing the shell and the API so they **share one runtime** instead of duplicating it (the Phase 6 `api/` sub-folder may change if that saves substantial size, but the shell's lookup rules and the `ICONCHEST_API_PATH` override must keep working and stay tested); trimming (only if the full test suite, including reflection-heavy parts such as Dapper, Serilog, SkiaSharp and JSON, passes against the trimmed build, otherwise skip it); no single-file bundling if it breaks native libraries.
- **Globalization:** on Linux a self-contained app still needs the system ICU library unless `InvariantGlobalization` is on, and invariant mode may change Unicode normalisation and case-folding that the query language relies on. Do not enable it unless the Phase 5 Unicode tests pass unchanged in invariant mode. Otherwise declare the ICU dependency in the Linux package and handle its absence with a clear message.
- Release configuration must be clean: Swagger off, DevTools off, no test or development endpoints, no debug symbols shipped (keep them as a separate artifact), and the version and build date correct in `version.json`, the assemblies, App info and the package names. Set `Major.Minor` to **1.0** (tell me if I should use something else). The build number and revision keep auto-incrementing.
- The packaged app must run from a **read-only install directory**. Verify that nothing writes beside the executables (config, data, logs and cache all live in the per-user folders).
- Optional but encouraged: a GitHub Actions workflow that builds, tests and packages on `ubuntu-latest` and `windows-latest` and uploads the artifacts.

### 2. Packages

- **Linux (primary):** a `.deb` for Ubuntu 22.04 and 24.04 and Linux Mint 21 and 22, built with `dpkg-deb` and plain scripts. Install to `/opt/iconchest`, a launcher at `/usr/bin/iconchest`, a freedesktop `.desktop` entry (name, comment, categories Graphics and Viewer, icon, `StartupWMClass`), icons at several PNG sizes plus SVG under the hicolor theme, and a post-install step that refreshes the icon and desktop caches. Declare dependencies (the verified WebKitGTK package name from Phase 6, ICU if needed, and other native libraries SkiaSharp needs, for example fontconfig and freetype where they are not bundled). Removal must leave the user's library data alone, and the docs must say where that data is and how to delete it. Also a portable `.tar.gz` that runs in place.
- **Windows:** a portable `.zip` that runs in place (with `IconChest.exe` as the entry point), and, if section 0 shows it is feasible, an installer (per-user install with no administrator rights, Start menu shortcut, uninstaller that keeps user data). The installer or the app's start-up must detect a missing **WebView2 runtime** and show a clear message with the download link. Say plainly in the docs that the builds are **not code-signed**, what SmartScreen will show, and how to proceed.
- Validate packages with scripts: `desktop-file-validate`, `lintian` if available (report warnings, fix errors), expected file lists, executable bits, no test or dev files, a `licenses/` folder present, `version.json` matching the package version.

### 3. Licences

- Audit **every shipped dependency**, including transitive NuGet packages and **native libraries** (SkiaSharp's native Skia build and what it bundles, Svg.Skia and its helpers, MetadataExtractor and XmpCore, the Photino native library, SQLite and the SQLitePCLRaw bundle, Serilog and sinks, Dapper, Microsoft packages, the .NET runtime) and any front-end assets (icons, fonts). Use a tool or `project.assets.json` plus manual verification; do not rely on memory.
- Produce a final `docs/THIRD-PARTY-NOTICES.md` and a shipped `licenses/` folder with the **full licence texts** and any required NOTICE or copyright lines (for example Apache 2.0 NOTICE content). The App info licences dialog from Phase 6 must show the final text.
- Fail the build if a shipped dependency has no entry (an automated check comparing the resolved package list with the notices file). Flag any copyleft, restrictive or commercial licence to me before shipping.
- Do not invent a licence for IconChest itself. Add a clearly marked `LICENSE.txt` placeholder ("Copyright (c) \<year> \<owner>. All rights reserved. Replace before distribution.") and ask me which licence to use.
- Playwright, axe-core and other test-only tools are not shipped, but list them in the developer documentation.

### 4. Performance tuning

Measure first, then fix. Write `docs/PERFORMANCE.md` with a reproducible method, baseline numbers, changes and final numbers, and do not accept regressions. Use the seeded 100,000-image database, and a real library of about 20,000 small icons for scan and thumbnail work. Report your reference machine.

Targets (report honestly and explain if unreachable): the window usable within 4 s of launch cold and 2 s warm; first rows of a 100,000-image grid in under 300 ms; scrolling 95th-percentile frame time under 33 ms; idle CPU under 1% with workers asleep; API idle memory under 200 MB with the 100,000-image library; a 20,000-file scan under 60 s on an SSD; thumbnails at 150 per second or better with four workers on small icons.

Areas: `EXPLAIN QUERY PLAN` for every list, tree, duplicate, missing and summary query with indexes added through a new migration where needed; SQLite pragmas (`synchronous`, `cache_size`, `mmap_size`, `temp_store`, WAL checkpoint behaviour, `PRAGMA optimize` on shutdown, `ANALYZE` after large scans); connection reuse; response compression for JSON but never for images; the HTTP caching headers; front-end asset size and load; avoiding idle timers and noisy SSE traffic; and a **soak test** (a scripted session of scrolling, selecting, previewing, editing and filtering for 5 minutes by default and 30 minutes on request) that shows stable memory in the API and the web view.

### 5. Platform checks

- Build a **compatibility matrix** (`docs/PLATFORMS.md`): Windows 10 and 11 with WebView2, Linux Mint 21 and 22 and Ubuntu 22.04 and 24.04 with their WebKitGTK versions, X11 and Wayland, HiDPI and fractional scaling. Record the results of the manual checks I will run, with a column for known issues and workarounds.
- Handle the known WebKitGTK problems you find (blank window with some GPU drivers, compositing and Wayland quirks) with documented environment variables or a `--safe-mode` flag that applies the workarounds, plus a troubleshooting section in the README.
- Add a lightweight local **loading screen** shown by the shell within about 300 ms (a spinner and the app name, rendered locally) until the app is ready, and a clear local error page if start-up fails.

### 6. Polish pass

No new features. Review and fix:

- **Consistency:** terms (library, folder, tag, hidden, missing), button labels, capitalisation, plurals, number and date formats (`yyyy-mm-dd`), empty, loading and error states in every view and dialog, plain-English messages with no jargon or stack traces.
- **First run:** an empty library shows a short welcome card that explains the app, says that files are never modified, and offers **Add your first folder**. Remove every leftover placeholder, TODO or "coming in phase N" text, and confirm all icon bar views and settings sections are real.
- **Branding:** a finished app icon (SVG source, PNG sizes, ICO), the browser-mode favicon, consistent window and About text, and an **About** dialog (version, build date, licences, project notes).
- **Accessibility audit:** run axe-core through Playwright against every view, dialog and settings section in both themes, in both browsers. Fix all serious and critical issues and report the rest. Check contrast, focus order and visibility, keyboard-only use of every feature, 200% zoom, reduced motion, and that every control has an accessible name.
- **Error handling:** global handlers for unhandled exceptions in the shell and API (log, then show a friendly message), a `window.onerror` and unhandled-rejection handler in the front end that shows a toast and reports to a new authenticated `POST /api/system/client-log` endpoint (8 KB cap, rate-limited, written at Warning with no personal data), and a check that no log line contains a secret or cookie. Check log volume: scanning 20,000 files at Information should not produce thousands of lines.
- **Shortcuts:** confirm `shortcuts.js`, the Help page and the real behaviour agree.

### 7. Security review

Re-verify the Phase 4 and Phase 10 rules against the final build and write `docs/SECURITY.md` (threat model for a single-user local app, what is protected, accepted risks such as plain HTTP on a LAN). Specifically: the route-enumeration test that every endpoint requires a session; Host and Origin checks; cookie flags; security headers and CSP on every response including errors and static files; request size limits and rate limits; path handling (no endpoint accepts a path from a non-local request, no path traversal in downloads); SVG served safely; restore and import validation; dependency vulnerabilities (`dotnet list package --vulnerable --include-transitive` as a build step that fails on high severity); and a secrets review of logs, crash output and packages.

### 8. Data safety and upgrade

- **Original files are never touched.** Add a final test that runs a broad scripted session of every feature that works on a library folder and proves, by hashing and comparing timestamps before and after, that nothing changed. Run the same session once with the **library folder made read-only** (Linux permissions, Windows ACLs) and prove everything still works.
- **Crash resilience:** a script that kills the app with `kill -9` during a scan, thumbnail generation, an edit save, a backup and a restore, then restarts and verifies the database opens cleanly, queues resume, and no partial files remain.
- **Upgrade:** keep a fixture database for every schema version produced so far (generate any missing ones from the migration scripts) and test that each migrates forward and passes the integrity check with its data intact; test `settings.json` migration from every earlier version; test that a newer database is refused with a clear message and left unmodified; test installing a new package over an old one with data preserved.

### 9. Documentation

`README.md` (what it is, install on each platform, first run, troubleshooting), `docs/USER-GUIDE.md` (short, task-based, with the selection, preview, tags, filters, query language, duplicates, missing, backup and restore basics), `docs/DEVELOPER.md` (architecture overview with diagrams, build, test, conventions, how to add a view, a setting and a migration), `docs/ARCHITECTURE.md`, `docs/RELEASING.md` (the exact release steps), `docs/KNOWN-LIMITATIONS.md`, `docs/SECURITY.md`, `docs/PLATFORMS.md`, `docs/PERFORMANCE.md`, `docs/SMOKE-TEST.md`, `CHANGELOG.md` (start at 1.0), and the existing `docs/QUERY-LANGUAGE.md`, `docs/FRONTEND.md` and `THIRD-PARTY-NOTICES.md`, all consistent with the final behaviour.

`docs/SMOKE-TEST.md` is a checklist of about 50 steps with a results table that I can run on a clean machine: install, first launch, single instance, add folder, scan, thumbnails, browse each tree, grid and slider, selection methods, preview, details and multi-edit, tags, hiding, favourites, Simple and Advanced filters, Help, duplicates, missing and cleanup, every settings section, backup, restore and move, tag export and import, LAN access from another device, upgrade over an older version, uninstall, and data left behind.

## Out of scope

New features, auto-update, code signing, store distribution, macOS, telemetry or network crash reporting, localisation, tray icon and auto-start, AppImage and Flatpak, a web-hosted deployment (it still works but is not packaged).

## Required tests and automated checks

Everything below must be runnable from a script, and the results recorded in the final reply.

- **Full regression:** the complete unit, API, shell and Playwright suites in both browsers, the slow performance tests, all passing, on the final release build where applicable.
- **Package tests:** contents, permissions, `desktop-file-validate`, `lintian` report, version consistency, size budget (record package and unpacked sizes; flag any large growth), `licenses/` present, no dev or test files, no debug symbols, a notices-versus-dependencies check, and the vulnerability check.
- **Headless smoke test script** (`scripts/smoke-headless.sh` and a PowerShell equivalent) that unpacks the real package, starts the packaged API, and drives a full sequence over HTTP and the CLI-style calls (login, add a generated folder, scan, thumbnails, list, a query, edit, tag, duplicates, missing, backup, restore, tag export and import, shutdown), checking each result. It must work on a CI machine with no display.
- **GUI smoke test on Linux** (`scripts/smoke-gui-xvfb.sh`, best effort): run the packaged shell under `xvfb-run`, confirm the window process stays up for 15 seconds, the API answers, a graceful `SIGTERM` stops both processes, and no child process is left.
- **Install tests:** install and remove the `.deb` in a clean Ubuntu container or chroot (dependencies resolve, files land where documented, removal leaves user data), and run from a read-only install location.
- **Data safety, crash, upgrade and first-run tests** from section 8, plus a **clean-profile first run** (no config or data folders exist) and a run with unusual environments (a user name or home path with spaces and non-ASCII characters, a long path, a read-only home that forces a clear error).
- **Accessibility and soak tests** from sections 4 and 6.
- **Security tests** from section 7.

## Manual checks (I will run these)

Listed in `docs/SMOKE-TEST.md` and below. They cover what automation cannot: real installs on clean Windows and Linux machines, GPU and Wayland behaviour, HiDPI, the native dialogs, double-clicking an installed icon, and LAN access from a phone.

## Working rules

- Follow the master brief and earlier conventions. If a requirement here cannot be met, say exactly why and what you did instead. Do not silently drop a deliverable.
- Do not add features. Fix defects you find, add a regression test for each, and list them in the final reply.
- Every script is idempotent, documented, and works from a clean checkout. No manual steps hidden outside `docs/RELEASING.md`.
- Never commit secrets or machine-specific paths.

## Definition of done

- `artifacts/` contains the Linux `.deb` and `.tar.gz` and the Windows `.zip` (and installer if feasible), each with checksums, built by one command per platform.
- All automated checks pass and their output is summarised, with any exception explained.
- The documentation set is complete and consistent, and `docs/PERFORMANCE.md` and `docs/PLATFORMS.md` contain real measured data.
- Your final reply includes: the artifact list with sizes, how to build them, results of every automated check, performance numbers against the targets, defects found and fixed, known limitations, open questions for me (licence choice, version number), and the manual checklist.

## How I will test Phase 11

1. Run the Linux build script on a clean checkout. Confirm the artifacts and checksums, and that the version number matches everywhere.
2. Install the `.deb` on a clean Linux Mint VM that has none of the dev tools. Launch IconChest from the menu, and check the icon, the loading screen, and that no .NET install was needed.
3. Unzip the Windows build on a clean Windows 10 or 11 machine. Launch it, then try it on one without the WebView2 runtime if you can, and read the message.
4. Work through `docs/SMOKE-TEST.md` on both platforms, filling in the results table.
5. Open a large library (your real icon collection) and judge speed against `docs/PERFORMANCE.md`.
6. Try Wayland and X11, a HiDPI display, and the `--safe-mode` flag.
7. Enable LAN access, then connect from a phone browser and log in.
8. Install over the previous version and confirm the library and settings are still there. Remove the package and confirm the data folders remain.
9. Make your library folder read-only and use the whole app.
10. Kill the app during a scan and restart it.
11. Open the licences dialog and spot-check three entries against their real licence files.
12. Read the README and the user guide as a newcomer would, and tell me what is unclear.
