# IconChest Phase 4 Prompt: The API

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 to 3 code. Those phases must be complete and their tests passing.

## Goal

Build the HTTP layer over Core: session authentication, static hosting of the front end, and every endpoint the UI will need, with OpenAPI docs. The API is thin. Business logic lives in Core, and this phase adds the Core services the endpoints need (tags, image queries and edits, trees, duplicates, missing files, scan jobs). The query language is Phase 5, so images are filtered with structured parameters only. After this phase you can drive the whole system from Swagger or the CLI.

**Deviation from the earlier plan:** database backup, restore, DB relocation and tag import and export move to Phase 10, together with their UI. Everything else from the earlier Phase 4 list is here.

## Before you write any code

1. Read the existing solution and follow its conventions.
2. Verify the current best option for OpenAPI on .NET 10 with a Swagger-style UI that is served from the same origin, works with cookie auth, and has a permissive licence (for example `Microsoft.AspNetCore.OpenApi` plus a UI package). Explain your choice.
3. Reply with a short plan and assumptions, then build.

## Scope

### 1. Hosting, startup and the child-process contract

The desktop shell (Phase 6) will run this project as a child process, so define the contract now and document it in the README.

- **Configuration inputs:** command-line `--port N` (0 = random free port; default from `api.portPreference`), `--parent-pid N` (the API exits if that process disappears, as a backstop to the shell killing it), `--swagger` (enable Swagger UI; also enabled when `ASPNETCORE_ENVIRONMENT=Development`; off otherwise), and the environment variable `ICONCHEST_API_SECRET` (the session secret).
- **Binding:** `127.0.0.1` only, unless `api.allowLan` is true **and** a LAN password is set, in which case bind all interfaces. If `allowLan` is true with no password, bind locally only and log a Warning.
- **Readiness line:** once listening, print exactly one line to stdout: `ICONCHEST_READY http://127.0.0.1:<port>` (use the actual bound port) so the shell can parse it. Nothing else on stdout may start with `ICONCHEST_`.
- **Standalone secret:** if `ICONCHEST_API_SECRET` is not set, generate a random secret (32 bytes, base64url) and print it to the **console only**, never to a log file. If the variable is set, never print it.
- **Components:** the API logs with component name `api`, uses `AppPaths` and the settings service from Core, opens and migrates the database at startup, starts the `ThumbnailWorker` as a hosted service (resuming the queue), and if `scanOnStartup` is true starts background scans of all enabled folders after startup. Graceful shutdown stops the worker, cancels scans cleanly, flushes thumbnail access times and flushes logs.
- `GET /health` (anonymous) returns `{"status":"ok"}`.

### 2. Authentication and security

- **Session:** `POST /api/session` accepts `{ "secret": "..." }` or `{ "password": "..." }` and on success sets cookie `iconchest_session` (random 32-byte token, `HttpOnly`, `SameSite=Strict`, `Path=/`, no `Expires` so it is a session cookie; no `Secure` flag because plain HTTP is a known, accepted limitation, to be noted in the README). `GET /api/session` returns `{ authenticated, mode }`. `DELETE /api/session` logs out. Sessions live in memory with a sliding 24-hour expiry.
- **Secret login** is accepted only from loopback connections. **Password login** works from anywhere when a password is set. The password hash uses PBKDF2-SHA256 with at least 600,000 iterations, a random salt and a versioned format string (Core class `PasswordHasher`, with tests). Compare secrets and hashes in constant time.
- **Throttling:** after 5 failed logins in a minute, answer further attempts with 429 and a `Retry-After` header for a growing delay. Never reveal whether the secret or password was the problem.
- **Every endpoint requires a valid session** except `GET /health`, `POST` and `GET /api/session`, the static front-end files, and the Swagger UI pages (their calls still need a session).
- **Host header allow-list** (DNS-rebinding defence): accept only `127.0.0.1`, `localhost`, `[::1]` and, when LAN is on, the machine's own addresses and host name. Otherwise 400.
- **Origin check:** for non-GET requests, if an `Origin` header is present it must match the request's host, otherwise 403. No CORS at all.
- **Headers on every response:** `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer`, `X-Frame-Options: DENY`, and a Content-Security-Policy of `default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; script-src 'self'; frame-ancestors 'none'`. Relax the CSP only for the Swagger paths if needed.
- **Never log** the secret, passwords, cookie values or login request bodies. Request logging is at Debug and records method, route template, status and duration only, not query strings on session endpoints.
- `isLocal` for a request means the connection is from a loopback address. Native-only features (Phase 6 and 10) will check it.

### 3. Static hosting

Serve `src/IconChest.Api/wwwroot` at `/`, with a fallback to `index.html` for non-`/api` paths that do not match a file. Use correct content types, ETags, no-cache on `index.html`, and refuse dotfiles. Add a **throwaway placeholder** front end: `index.html` plus one small ES module that checks `GET /api/session`, shows a password login form when unauthenticated, and when logged in shows the output of `/api/system/info` and a link to Swagger. It will be replaced in Phase 6.

### 4. Errors and conventions

- JSON in camelCase, enums as strings, dates as UTC ISO 8601.
- RFC 7807 problem details for every error, with `traceId`. Validation errors are 400 with an `errors` map, unknown ids are 404, conflicts (duplicate tag name, scan already running, folder already in the library) are 409, throttling is 429, and unhandled exceptions are 500 with a generic message (details only in the log).
- Request body limit 1 MB. Cap list sizes (for example 10,000 ids per bulk edit, page `limit` max 500).
- DTOs live in the Api project. Do not expose Core entities directly as the contract.

### 5. Endpoints

**System and settings**

| Method and path | Behaviour |
| --- | --- |
| `GET /api/system/info` | Version, build date, executable path, log file path, log folder, platform, API port, DB path, schema version, hash algorithm, `isLocal` |
| `POST /api/system/shutdown` | Graceful shutdown. Loopback only. |
| `GET /api/settings` | All settings except the password hash (include `api.hasLanPassword`) |
| `PATCH /api/settings` | JSON merge patch, validated by Core. Returns the new settings plus `restartRequired` (a list of keys such as `api.allowLan`, `api.portPreference`, `dbPath`). The log level takes effect immediately. Worker count changes restart the thumbnail worker. |
| `PUT /api/settings/lan-password` | Sets or changes the LAN password. If one exists, the current password is required. Minimum 8 characters. |

**Library, scanning and events**

| Method and path | Behaviour |
| --- | --- |
| `GET /api/folders` | Folders with image counts and last scan time |
| `POST /api/folders` | `{ path }`. Core validation errors map to 400 or 409. |
| `PATCH /api/folders/{id}` | `{ enabled }` |
| `DELETE /api/folders/{id}` | Removes records and triggers thumbnail orphan cleanup. Never touches disk files. |
| `POST /api/folders/{id}/scan` and `POST /api/scans` (`{ folderIds? }`) | Start background scan jobs, return 202 with job ids. A folder already scanning gives 409. |
| `GET /api/scans`, `GET /api/scans/{jobId}`, `POST /api/scans/{jobId}/cancel` | List recent jobs (keep the last 20), progress and result, and cancel |
| `GET /api/events` | Server-Sent Events stream (cookie auth). Event types: `scan.progress` (throttled to about 5 per second per job), `scan.completed`, `thumbnails.status` (about every 2 seconds while work is pending), `library.changed` (after any edit, scan or removal, so views can refresh). Include a heartbeat comment every 15 seconds. Clean up on client disconnect. |

Scan jobs live in a Core `ScanJobManager` (so the CLI and API share it): it runs scans on background tasks, tracks progress and results, and enforces one scan per folder.

**Trees**

| Method and path | Behaviour |
| --- | --- |
| `GET /api/tree/folders` | Library folders as top-level nodes with their real sub-directory structure from `directory_key`. Each node has `path`, `name`, `count` (files directly in it) and `totalCount` (including subfolders). |
| `GET /api/tree/years?field=effective\|created\|modified\|taken\|added` | Years, each with months and counts, plus an `unknown` node. Default `effective`. |
| `GET /api/tags` | Tags (alphabetical, case-insensitive) with `id`, `name`, `fgColor`, `bgColor` and image `count` |

Tree counts exclude hidden and missing images.

**Images**

| Method and path | Behaviour |
| --- | --- |
| `GET /api/images` | List with structured filters, sorting and paging (below). Returns `{ total, offset, limit, items }`. |
| `GET /api/images/ids` | Same filters and sort, returns only ids (cap 200,000), for Select all and Shift-range selection across unloaded pages |
| `GET /api/images/{id}` | Full details: path, directory, hash and algorithm, all dates, description, tags, EXIF, status, transparency |
| `PATCH /api/images` | Bulk edit (below) |
| `GET /api/images/{id}/thumbnail?size=64\|128\|256` | See thumbnail contract below |
| `GET /api/images/{id}/file` | The original file |

**Filter parameters** (all optional, combined with AND): `folderId`; `directory` (a directory key) with `includeSubfolders` (default false); `year`, `month` and `dateField`; `dateFrom` and `dateTo` (`yyyy-mm-dd`, on `dateField`); `tag` (repeatable, plus `tagMode=any|all`, default any); `type` (repeatable); `minWidth`, `maxWidth`, `minHeight`, `maxHeight`; `minSize`, `maxSize` (bytes); `ratingMin`; `favourite`; `text` (case-insensitive contains across file name, title and description); `hidden`, `missing` and `duplicate` each as `exclude|include|only` (defaults: hidden `exclude`, missing `exclude`, duplicate `include`). **Sort:** `sort` is one of filename, title, effectiveDate, created, modified, taken, added, size, width, height, type, rating, favourite, folder; `dir` is `asc|desc`; id is the hidden tiebreaker. The parameter `q` (query language) is reserved: if present return 501 now (implemented in Phase 5), and if both `q` and any other filter are sent return 400, because the Simple and Advanced modes are mutually exclusive. Build SQL only from whitelisted fields, with parameters. Design the Core `ImageSearchRequest` and SQL builder so Phase 5 can plug in a compiled `WHERE` fragment without rewriting paging and sorting.

Image summaries contain: id, fileName, title, width, height, format, sizeBytes, rating, isFavourite, isHidden, isMissing, isDuplicate, effectiveDateUtc, directory, and `tagIds`. Fetch tag ids efficiently (one query for the page, not one per row).

**Bulk edit** (`PATCH /api/images`): body `{ ids: [...], changes: { title?, description?, rating?, isFavourite?, isHidden?, takenOverrideUtc?, addTagIds?, removeTagIds? } }`. Only properties present in the JSON are applied, and an explicit `null` clears (`takenOverrideUtc: null` removes the override; `description: null` clears it). An empty-string title resets to the default derived from the file name. Rating must be 0 to 5. Dimensions, format, size, hash and path are not editable. All ids must exist (otherwise 404 and nothing changes). Apply everything in one transaction. Changing `isHidden` recomputes duplicate flags for the affected hashes. Emit `library.changed`.

**Thumbnail contract** (for Phase 7's `<img>` tags): `Ready` returns 200 `image/webp` with an `ETag` and `Cache-Control: private, max-age=86400`, and honours `If-None-Match` with 304. `Pending` returns 202 with an empty body, `Cache-Control: no-store`, `Retry-After: 2` and header `X-Thumbnail-State: pending`. `Failed` returns 404 with header `X-Thumbnail-State: failed` and an `X-Thumbnail-Error` code. Unknown image is 404, invalid size is 400. Requesting a pending thumbnail raises its queue priority, through the Phase 3 provider.

**Original file contract:** look up by image id only (never accept a path). 404 if the image or file is gone or flagged missing. Stream with range support, ETag and Last-Modified, the content type from the recorded format, `Content-Disposition: inline`, and `X-Content-Type-Options: nosniff`. Hidden images are still served (the user may be viewing the hidden list). Serve SVG with `Content-Security-Policy: default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox` so scripts cannot run if the file is opened directly.

**Tags**

| Method and path | Behaviour |
| --- | --- |
| `POST /api/tags` | `{ name, fgColor?, bgColor? }`. Names are trimmed, 1 to 50 characters, no control characters, unique case-insensitively (409 otherwise). Colours are `#RRGGBB`, with sensible defaults. |
| `GET /api/tags/{id}` | Includes the number of images using it, so the UI can warn before deleting |
| `PATCH /api/tags/{id}` | Rename and recolour |
| `DELETE /api/tags/{id}` | Removes the tag from every image |

**Duplicates, missing and thumbnails**

| Method and path | Behaviour |
| --- | --- |
| `GET /api/duplicates` | Groups `{ hash, sizeBytes, items }`, paged by group (`limit`, `offset`), excluding hidden and missing, items within a group sorted by path |
| `GET /api/missing` | Paged list of missing images (path, last seen) and a total |
| `POST /api/missing/remove` | Optional `{ folderId }`. Deletes the missing records, triggers orphan cleanup, returns the count. |
| `POST /api/missing/rescan` | Starts scans for the folders that contain missing files |
| `GET /api/thumbnails/status` | Queue status from Phase 3 |
| `POST /api/thumbnails/retry-failed`, `POST /api/thumbnails/verify`, `POST /api/thumbnails/clear` | `clear` requires `{ confirm: true }` |

### 6. New Core services

Add, with their own unit tests where they hold logic: `TagService`, `ImageSearchService` (filter, sort, page, ids, whitelist SQL builder), `ImageEditService` (bulk edit with presence semantics), `TreeService`, `DuplicateService`, `MissingService`, `ScanJobManager`, `PasswordHasher`. Add database indexes you find necessary through a new migration (never edit earlier ones) and justify each.

### 7. OpenAPI

Generate an OpenAPI document that describes every endpoint, its parameters, request and response types, and a cookie-based security scheme. Serve the Swagger-style UI at `/swagger` when enabled. Add short summaries so the docs are usable.

### 8. Dev CLI additions

| Command | Behaviour |
| --- | --- |
| `api login [--url U] [--secret S]` | Logs in (secret from the argument or `ICONCHEST_API_SECRET`) and stores the cookie in a user-only temp file |
| `api get\|post\|patch\|put\|delete <path> [--body json\|@file]` | Calls the API with the stored cookie and pretty-prints the response and status |
| `api events` | Streams `/api/events` to the console |

The existing Core-direct commands keep working.

## Out of scope

The query language, the real front end, the Photino shell, backup and restore, DB relocation, tag import and export, native endpoints (open in viewer, folder picker, open log folder), HTTPS, multiple users.

## Required tests

Use `WebApplicationFactory` with temp folders and the `ICONCHEST_CONFIG_DIR` and `ICONCHEST_DATA_DIR` overrides, a real SQLite file, and generated images. Pass on Linux and Windows.

- **Auth:** enumerate the endpoint route table and assert that every `/api` endpoint except the allowed ones returns 401 without a session; login success and failure; cookie attributes (`HttpOnly`, `SameSite=Strict`, path, no expiry); logout invalidates; sliding expiry using the injected clock; secret login refused from a non-loopback remote address (simulate with a test middleware); password login; throttling and `Retry-After`; PBKDF2 hasher round-trip, format versioning and wrong-password cases.
- **Security:** Host allow-list (accept and reject cases, with LAN on and off); Origin mismatch on non-GET; security headers on API and static responses; no CORS headers; after a login, the log files contain neither the secret nor the cookie value.
- **Hosting:** static files served, SPA fallback, dotfiles refused, `index.html` no-cache, Swagger on only when enabled, the readiness line format, binding rules for `allowLan` with and without a password, shutdown endpoint (loopback only).
- **Settings:** get hides the hash; patch validation errors; `restartRequired`; log level changes at runtime; LAN password set and change rules.
- **Folders and scans:** CRUD and validation (nested folder 409); scan lifecycle (202, progress, completion, result counts); second scan of the same folder 409; cancel; scan on startup; removal triggers thumbnail cleanup and leaves disk files.
- **Events:** an SSE client receives scan progress and completion, a heartbeat, and is cleaned up on disconnect.
- **Images:** every filter alone and combined; `tagMode` any and all; date-field filters; sorting on every field in both directions with stable ties; paging and `total`; ids endpoint ordering matches the list; defaults hide hidden and missing; `only` and `include` modes; `text` search; `q` gives 501 and `q` with other filters gives 400; unknown parameters are rejected or ignored consistently (state which); caps enforced; no N+1 tag fetching (assert the query count).
- **Edit:** presence versus null semantics for each field; clearing the taken override; adding and removing tags; multi-image edit; hidden toggling updates duplicate flags; invalid rating and unknown id leave data unchanged; the title reset rule; read-only fields cannot be changed; `library.changed` fires.
- **Tags:** CRUD, case-insensitive uniqueness, colour validation, name rules, usage count, delete removes links and leaves images.
- **Trees:** folder tree structure and counts for nested directories and several top-level folders; year and month grouping for every date field; the `unknown` node; hidden and missing excluded.
- **Thumbnails and files:** ready, pending, failed, not-found and bad-size responses; ETag and 304; priority raised on a pending request; original file served with correct type and range requests; missing file 404; SVG headers; an id for a hidden image is served; no way to request an arbitrary path.
- **Duplicates and missing:** grouping and paging; excluded when hidden; missing list, remove and rescan.
- **Errors:** problem-details shape with `traceId`; a thrown exception gives a generic 500 with details only in the log.
- **OpenAPI:** the document lists all endpoints and the cookie security scheme.
- **Performance (slow or explicit):** seed 50,000 image rows directly in SQL, then time list pages with typical filters and each sort, the ids endpoint, and the trees. Print timings and note any slow query with its `EXPLAIN QUERY PLAN`.

## Working rules

- Follow the master brief and earlier conventions. Where silent, choose the simplest option and note the assumption.
- Keep endpoint handlers thin. Logic goes in Core.
- Add every new dependency to `THIRD-PARTY-NOTICES.md` after checking its licence.
- If a requirement here conflicts with the brief, follow this prompt and tell me.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux, with no warnings in Core and Api.
- All required tests exist and pass. List any you could not write and why.
- README documents the child-process contract, environment variables, ports, auth model, known limitations, and how to run the API standalone.
- Your final reply includes: new files, endpoint list, assumptions, deviations, decisions you want me to confirm, and the manual checklist below with exact commands.

## How I will test Phase 4

1. Start the API standalone with `--swagger`. Note the printed secret and the `ICONCHEST_READY` line.
2. Open `http://127.0.0.1:<port>/swagger`. Call an endpoint and confirm 401. Call `POST /api/session` with the secret, then retry and confirm 200.
3. Open the root page and confirm the placeholder front end loads and logs in with a password (set one with `PUT /api/settings/lan-password`).
4. Use the CLI: `api login`, then `api post /api/folders --body '{"path":"<test folder>"}'`, `api post /api/folders/1/scan`, and watch `api events` for progress and thumbnail status.
5. In Swagger, list images with different filters and sorts. Try `hidden=only`, `tag` filters and `text`.
6. Create tags, bulk-edit several images (title, rating, tags, hidden) and confirm the changes, including clearing the taken-date override.
7. Open `/api/images/{id}/thumbnail?size=64` in the browser (once logged in) and the `/file` URL for a PNG, an SVG and a JPEG.
8. Check `/api/duplicates`, then hide one of a pair and confirm the group disappears.
9. Delete a file on disk, rescan, and see it in `/api/missing`, then remove it through `/api/missing/remove`.
10. Check that the log file contains no secret. Change the log level through `PATCH /api/settings` and confirm output changes without a restart.
11. Run `dotnet test` and the slow performance test and check the timings.
