# IconChest Phase 10 Prompt: Management Pages and Data Settings

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 9 code. Those phases must be complete and their tests passing.

## Goal

Finish the product's management features: the **Duplicates** page, the **Missing** page, the remaining Settings sections (**Performance, Cleanup, Logging, API, Data**), database **move, backup and restore**, and **tag export and import**. Every destructive or risky operation is validated first, confirmed, logged and reversible where possible. Nothing here ever modifies, moves or deletes a user's image files.

## Before you write any code

1. Read the existing solution: the Phase 2 scanner and missing handling, the Phase 3 cache and cleanup, the Phase 4 endpoints (`/api/duplicates`, `/api/missing`, `/api/thumbnails/*`, settings, `ScanJobManager`), the Phase 6 shell and bridge, the Phase 7 grid and `grid-geometry.js`, the Phase 8 details panel, and `docs/FRONTEND.md`.
2. Re-read brief sections 8, 13 and 14.
3. Verify whether a file input and a download link work acceptably inside Photino on both platforms, and record the result in `docs/FRONTEND.md`.
4. Reply with a short plan and assumptions, then build.

## Scope

### 1. Core: the database gate and maintenance service

Moving, backing up and restoring a live SQLite database safely needs one coordinated mechanism. Build it first and test it hardest.

- **`IDbGate`**: every database access goes through a shared lock. Maintenance takes an **exclusive** lock, waiting (with a timeout) for in-flight work to finish. While maintenance runs, new API requests that touch the database get **503 with `Retry-After`** and a `maintenance` problem type, and the front end shows a calm "Updating the library, one moment" overlay and retries.
- **`DataMaintenanceService`** with `Backup`, `InspectBackup`, `Restore` and `Move`. Before any swap: refuse (409) if a scan is running, pause the thumbnail workers, run a WAL checkpoint, close every connection and clear the SQLite connection pools. After: reopen, migrate if needed, resume workers, emit `data.reloaded` and `library.changed`. On any failure after the swap began, **roll back automatically** to the previous file and report the error.
- **Backup** uses SQLite's online backup API, so it is consistent while the app is running and writes are happening. It never writes inside a library folder (reject such a path), never overwrites an existing file unless asked, and writes to a temporary name then renames.
- **Restore validation** (`InspectBackup`) opens the file read-only and checks: it is a SQLite database, `PRAGMA integrity_check` is `ok`, the required tables exist, and the schema version is **not newer** than the app supports (an older one is migrated after the swap). It reports the schema version, record counts (images, folders, tags), the file's date, and the **library folder paths with whether each exists on this machine**.
- **Restore** keeps the current database as `<name>.pre-restore-<yyyyMMddHHmmss>` (never destroyed), copies the backup into place, migrates, and returns a summary: backup file, schema versions (including any migration), counts, and warnings (for example library folders that do not exist here). The summary is written to the log at Information. `settings.json` is **not** part of a backup. Thumbnails self-heal afterwards through the Phase 3 mechanism.
- **Move** copies the database (after a checkpoint) to a chosen folder, verifies it (integrity check and record counts equal the source), updates `dbPath` in `settings.json`, reopens, and renames the old file to `<name>.old-<timestamp>` (or deletes it only if the user explicitly asked). Refuse if the target exists, is not writable, or is inside a library folder.
- **Settings validation (extends Phase 1):** `dbPath`, `thumbnailCachePath` and `backupFolder` (new, default `<data>/backups`) must not be inside any library folder. `thumbnailCacheMaxMb` 64 to 1,048,576, `scanWorkers` and `thumbnailWorkers` 0 to 32, `thumbnailQuality` 1 to 100, `api.portPreference` 0 or 1024 to 65535, and `api.allowLan` may only become true when a LAN password is set.

### 2. API additions (all authenticated; the ones marked local accept loopback requests only)

| Endpoint | Behaviour |
| --- | --- |
| `GET /api/duplicates/summary` | `{ groupCount, imageCount, reclaimableBytes, groupSizes: [...] }` for the chosen sort, so the front end can lay out every group without loading them (cap 200,000 groups) |
| `GET /api/duplicates` | Phase 4 endpoint, now with `sort` (`reclaimable` default descending, `copies`, `size`, `name`), `dir`, and paging by group index |
| `GET /api/duplicates/ids` | Image ids in display order (cap 200,000), for Select all, ranges and preview |
| `GET /api/missing` | Phase 4 list, now with `sort` (`name`, `path`, `since`, `size`) and `dir` |
| `POST /api/missing/remove` | Accepts `{ ids }`, `{ folderId }` or neither (all). Returns the count and triggers thumbnail orphan cleanup. |
| `POST /api/thumbnails/cleanup-orphans` | Runs the Phase 3 cleanup, returns counts |
| `GET /api/data/info` | DB path, size, schema version, WAL size, counts, backup folder, and the list of backups in it (name, size, date) |
| `POST /api/data/backup` | `{ path? }`. No path means a timestamped file in the backup folder. A custom path is **local** only. |
| `GET /api/data/backups/{name}/download` | Streams a backup from the backup folder by name only (no traversal), for web mode |
| `POST /api/data/restore/inspect` | `{ name }` or `{ path }` (path is **local** only). Returns the validation report. |
| `POST /api/data/restore` | Same selectors plus `{ confirm: true }`. Returns the summary. |
| `POST /api/data/move` | `{ folder, deleteOld? }`. **Local** only. |
| `GET /api/tags/export` | The tag JSON document |
| `POST /api/tags/import` | A tag JSON document (body limit 1 MB). Returns the result summary. |
| `POST /api/native/open-folder` | `{ target }` where target is one of `logs`, `backups`, `thumbnails`, `data` (a whitelist, never a path). **Local** only. Uses `INativeLauncher`. |

`GET /api/system/info` also returns `lanUrls` (the machine's non-loopback addresses with the port, only when LAN access is enabled) and a `canRestart` flag. Add the SSE event `data.reloaded`.

### 3. Desktop shell additions

Extend the Phase 6 bridge (same origin check, strict payload validation, 5 MB message cap): **`pickFile`** (native open dialog, optional extension filter), **`pickSaveFile`** (native save dialog with a suggested name), **`saveFile`** (shows a save dialog, writes the given text to the chosen path, returns the path or null), and **`restartApi`** (stops the API child gracefully, starts a new one with a fresh readiness line, re-logs in, and reloads the page at the new URL). `ApiProcessManager` gets a tested `RestartAsync`. Outside the shell (browser or standalone) these commands do not exist and the UI falls back as described below.

### 4. The Duplicates page

A view in the icon bar (Panel A hidden). The user can only **look** at duplicates for now: no delete or keep actions.

- **Grouped virtual grid:** each duplicate set is a block with a slim header ("3 copies, 24 KB each, 48 KB reclaimable"), followed by its images in rows. A group always starts on a new row, so **copies stay together**. Add `lib/grouped-geometry.js` (pure): row offsets from the group sizes using prefix sums, visible range by binary search, index to position and back, header heights, recalculation when the column count changes. Reuse the Phase 7 cell, thumbnail loader, selection model and keyboard module where possible, extending keyboard navigation so Up and Down move between rows across group boundaries.
- **Data:** read `GET /api/duplicates/summary` for the layout, then fetch group pages on demand (about 100 groups) with the same cache, prefetch and abort rules as the image source.
- **Cells** show the thumbnail with the **folder** (shortened in the middle, full path in the tooltip) as the caption, because the copies differ by location. The heart works. Hidden and missing images never appear, and a set with fewer than two visible members disappears after the next refresh.
- Clicking selects and opens the details panel (with its Copies list), with all the selection rules from Phase 7. Preview moves through the selection, or through every duplicate in display order.
- Top bar: the totals ("1,204 sets, 3,310 files, 412 MB reclaimable"), a sort dropdown (Reclaimable, Copies, Size, Name) with a direction toggle, the background toggle, and the size slider. An empty state says "No duplicates found".

### 5. The Missing page

A view in the icon bar (Panel A hidden). A virtualised table (reuse the grid geometry with one column): thumbnail (small, with a placeholder when none exists), file name, folder and full path, library folder, size, and **missing since**, sortable by column. A checkbox column with Select all and the usual Shift-click ranges.

- **Remove from library:** with a selection it removes those rows, otherwise all missing records. The confirm dialog states the count and says plainly that the records and thumbnails are removed from IconChest and **no files on disk are touched**.
- **Rescan:** starts scans of the library folders that contain missing files (`POST /api/missing/rescan`) with progress in the status bar. Files that reappeared drop out of the list when it finishes.
- An empty state ("Nothing is missing"). A **count badge** on the Missing icon in the icon bar, updated from `library.changed`.

### 6. Settings sections

Replace the Phase 6 placeholders. Every section saves on change, shows inline problem-details errors, and shows the restart banner when needed, as before.

- **Performance:** thumbnail cache location (text, plus Browse when the bridge exists; note that old thumbnails are not moved and are regenerated on demand), maximum size (accepts `500 MB` or `2 GB`), current cache usage, **Clear thumbnail cache** (confirm), **Verify cache** and **Retry failed** with result summaries, **Clean up orphans**, an **Open cache folder** button (local only), the hash algorithm shown read-only (`xxh128`), and the worker settings: scan threads and thumbnail workers (Auto or a number), thumbnail quality.
- **Cleanup:** the missing count linking to the Missing page, **Delete all missing records** (the same confirm as the page), the **auto-remove missing after scan** checkbox with a clear explanation, the hidden-image count with a **View hidden images** button (opens Folders with Show all and the Simple filter "Only hidden" applied), and orphan thumbnail cleanup.
- **Logging:** the level dropdown (Off, Error, Warning, Information, Debug, Trace), effective immediately; the current log file and folder with copy buttons; **Open log folder** (local only); the retention note (30 days, one file per day per component).
- **API:** the port (read-only), a fixed-port field (0 means random), **Allow LAN access** (blocked with an explanation until a password exists), **Set or change password** (current password required if one exists, at least 8 characters), the reachable URLs when LAN access is on, a plain warning that traffic is not encrypted, and a **Restart API** button that uses the `restartApi` bridge command, or shows manual restart instructions when `canRestart` is false.
- **Data:** the database path, size, schema version and counts. **Change location** (folder text box with Browse, an "also delete the old file" checkbox that is off by default) with a progress state and the verification result. **Backup now** (default folder), **Backup to...** (bridge `pickSaveFile`, local only), and a list of existing backups with date and size, each with **Restore**, **Download** (shown when not local) and **Open backup folder**. **Restore from file...** (bridge `pickFile`, or a path text box when no bridge). **Export tags** and **Import tags** (below).
- **Restore flow** is a short wizard: choose a backup, a validation step that shows the report (schema version, record counts, date, which library folders exist here), a strong confirmation naming the file and stating that the current library will be replaced and kept as a `.pre-restore` copy, a progress state, and a result dialog showing the summary and warnings. Errors are plain English and leave the library untouched. After success the whole UI reloads its state (settings, tags, trees, grids, selection cleared).

### 7. Tag export and import

- **Format:** `{ "format": "iconchest-tags", "version": 1, "exportedUtc": "...", "tags": [ { "name": "...", "fgColor": "#RRGGBB", "bgColor": "#RRGGBB" } ] }`. Definitions and colours only, **not** which images have which tags.
- **Export:** the front end fetches `GET /api/tags/export` and saves it through the bridge `saveFile`, or with a download link when there is no bridge.
- **Import:** the user picks a JSON file (a file input read in the browser), and the front end posts it to `POST /api/tags/import`. Rules: **merge by name, case-insensitive; an existing tag wins and keeps its colours**; a duplicate name inside the file keeps the first; entries with an invalid name (see the tag name rules) or invalid colour are reported as invalid and skipped; at most 10,000 tags; a `version` newer than 1 is rejected with a clear message; malformed JSON gives a friendly 400.
- **Result summary:** counts for added, skipped (already exists), duplicates in file, and invalid (with each reason, limited to the first 50 in the log). Show it in a dialog with expandable lists, write it to the log at Information, and refresh the tags store.

## Out of scope

Actions on duplicates (keep, delete, merge), scheduled or automatic backups, backup compression or encryption, backup retention rules, exporting or importing `settings.json`, importing which images have which tags, HTTPS, an update checker, packaging (Phase 11).

## Required tests

**Pure logic** (import modules in the page as in earlier phases; large data-driven tables):

- **Grouped geometry:** group offsets, header heights, visible range and buffer for 0, 1, 2, 7, 20,000 groups, with groups smaller than, equal to and larger than the column count, short last rows per group, column changes, index to position and back, the total height.
- **Grouped keyboard navigation:** Up and Down across group boundaries (column kept or clamped), Left and Right across boundaries, Home and End, page keys, one-column layouts, and selection ranges in display order.
- **Missing table model:** sorting, selection with ranges, select all, the remove payload (ids versus all).
- **Settings forms:** size parsing (`500 MB`, `2 GB`, bad input), port and worker ranges, quality, and the LAN checkbox rule.
- **Wizard and result models:** restore wizard state transitions, tag result formatting, backup list formatting, client-side tag file pre-checks.

**Core and API tests** (real SQLite, temp folders, path overrides):

- **Duplicates:** summary values (group sizes, image count, reclaimable bytes), every sort and direction, paging by group, ids in display order, hidden and missing excluded, and results updating after a copy, a move, a hide and an unhide.
- **Missing:** sort parameters, remove by ids, by folder and all, thumbnail cleanup triggered, rescan restoring files.
- **Settings validation:** every new rule, including paths inside library folders and the LAN password rule.
- **Tag export and import:** round trip; merge by name ignoring case, including accented names; the existing tag wins on colours; duplicates in the file; every invalid-entry kind; limits and size cap; malformed JSON and a newer version; 10,000-tag performance; the summary counts; the log file contains the summary and no secrets; authentication required.
- **Backup:** consistent and valid while another thread writes continuously (integrity check ok, counts within the expected range); path rules (inside library rejected, overwrite protection, local-only custom path); default naming; download endpoint accepts only listed names (try traversal attempts); works with an active WAL.
- **Inspect and restore:** valid backup; truncated file; a non-SQLite file; a newer schema (refused); an older schema (migrated); missing tables; library folders reported as existing or missing; restore refused while a scan runs; the `.pre-restore-timestamp` copy exists and opens; **automatic rollback when a failure is injected** after the swap; restoring onto the live file path refused; workers resume; `data.reloaded` is emitted; `settings.json` untouched; thumbnails self-heal; **no file handles are left open** (on Windows CI the old file can be renamed and deleted afterwards).
- **Move:** to a new folder with verification; the settings update; old file renamed or deleted per the flag; refusal cases (target exists, not writable, inside a library folder); rollback on injected failure; the WAL and shm files are handled.
- **`IDbGate`:** shared versus exclusive behaviour, waiters, timeouts, 503 with `Retry-After` for requests during maintenance and success afterwards, and a stress test mixing reads, writes and a backup.
- **Open folder:** only whitelisted targets, local only, the fake launcher receives the right folder.
- **LAN and system info:** `lanUrls` content, enabling LAN refused without a password.
- **Shell (`IconChest.Desktop.Tests`):** `RestartAsync` with the stub (new port, old process gone, failure cases), bridge payload validation, origin checks, size cap.

**End-to-end** (Playwright for .NET, Chromium and WebKit; seeded and real libraries as in earlier phases; simulate the bridge with an injected `window.external` stub that records messages):

- **Duplicates page:** seed sets of 2, 3 and 10 across folders; groups stay together with headers and totals; clicking a cell opens the details panel with its Copies; keyboard navigation across groups; the preview walks all duplicates; there are no delete or keep controls; hearts work; hiding one of a pair removes the group; sorting; a seeded 20,000-group database keeps the DOM bounded and scrolls smoothly.
- **Missing page:** delete files on disk, rescan, see them listed with the right data; sorting; remove selected, remove all with the confirm text and count; **files on disk remain**; rescan after restoring a file removes it from the list; the icon badge count; the empty state.
- **Settings sections:** each field saves and persists; invalid values show inline errors; Clear cache, Verify and Retry failed show results; Cleanup counts and the auto-remove toggle (verify the scan behaviour); View hidden images opens the right filtered view; the Logging level takes effect (check the log file) and Open log folder sends the right request and is hidden when not local; LAN toggle blocked until a password exists, then allowed with the restart banner, the URLs list, and Restart API using the bridge stub or the manual instructions.
- **Data section:** Backup now creates a listed file; Download appears only when not local; the whole restore wizard (validation report, cancel, confirm, success summary, UI reloaded and showing the backup's content, `.pre-restore` file present); corrupt and newer-schema files show friendly errors and change nothing; Change location with verification; the "delete old file" option; the maintenance overlay appears during a slowed operation and clears afterwards.
- **Tags:** export produces valid JSON (capture the download or the bridge message); import through the file chooser shows the summary dialog and the merge rules hold (check through the API); bad files show clear messages.
- No console errors or unexpected failed requests in any test.

**Performance** (print, loose assertions): backup and restore of a 100,000-image database; the duplicates summary and first paint with 20,000 groups; the missing list with 50,000 rows; tag import of 10,000 tags.

## Working rules

- Follow the master brief and earlier conventions. Where silent, choose the simplest option and say so. If this prompt conflicts with the brief, follow this prompt and tell me.
- Nothing in this phase may write inside a library folder, and no endpoint may accept an arbitrary path from a non-local request.
- Every destructive action has a confirm step, a log entry and a clear message. Prefer keep-a-copy over delete.
- Add any dependency to `THIRD-PARTY-NOTICES.md` after checking its licence. Prefer none.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux, and the E2E tests pass in both browsers when run. The maintenance tests also pass on Windows.
- All required tests exist and pass, or you list exactly which could not be written and why.
- The README documents backups, restore, moving the database, LAN access and its limits. `docs/FRONTEND.md` is updated.
- Your final reply includes: new files, assumptions, deviations, decisions to confirm, known issues, and the manual checklist with exact commands.

## How I will test Phase 10

1. Scan a library, then copy several files into other folders and scan again. Open Duplicates, check the grouping, headers and totals, click through to details, use the arrow keys across groups, preview them, and try each sort.
2. Delete some files on disk and rescan. Open Missing, sort it, remove a few selected, then rescan and watch a restored file vanish from the list. Remove the rest and confirm the files on disk were never touched.
3. In Settings, Performance: change the cache size, clear the cache and watch thumbnails regenerate, then run Verify. Try invalid values.
4. In Cleanup: toggle auto-remove, delete a file, rescan, and confirm the record is removed automatically. Use View hidden images.
5. In Logging: set Debug, do something, and look at the log file. Set Off and confirm nothing is written. Press Open log folder.
6. In API: try to enable LAN without a password, set a password, enable LAN, press Restart API, and then open the listed URL from a phone or another computer and log in.
7. In Data: Backup now, then add some tags and scan more files. Restore the backup and read the validation report and the summary. Confirm your library went back, and a `.pre-restore` file exists. Try a corrupt file and a file from a future version.
8. Move the database to another folder (another drive if you can). Confirm the app keeps working and the old file is renamed. Try a read-only target.
9. Start a backup while a scan runs, and a restore while a scan runs, and read the messages.
10. Export tags, import them back (all skipped), edit the JSON to add new tags, invalid colours and different-case names, and import again. Read the summary and the log.
11. Repeat the dialog, restart and download checks on both Windows and Linux.
12. Run `dotnet test`, the E2E tests in both browsers, and read the performance numbers.
