# IconChest: Master Project Brief

Draft 1 for review. Paste this brief at the start of every AI session, followed by one phase prompt (delivered separately).

## 1. How to use this brief

This brief describes the whole IconChest application: architecture, constraints, conventions and behaviour. It is the source of truth. Each phase prompt says which part to build now. Rules:

- Build only the current phase. Do not build ahead, but do not make choices that block later phases.
- Where this brief is silent, choose the simplest option that fits the architecture, state the assumption in the reply, and continue.
- Every phase ends with passing unit tests, a clean build on Linux and Windows, and a short "how to test this phase" note.
- Never log or print secrets (session secret, passwords, cookies).
- Verify package names and current versions before using them. Do not rely on memory for NuGet or Linux package names.

## 2. Product summary

IconChest is a single-user, cross-platform (Windows and Linux) desktop application for organising a library of images, mostly icons up to 512x512 (many with transparency), plus some photos. The user adds folders to the library. The app scans them, records metadata in SQLite, generates thumbnails in the background, and lets the user browse, tag, rate, favourite, filter and search. **The app never modifies, moves or deletes the user's image files or folders.** All edits live in the database.

Supported source formats: png, jpg/jpeg, gif, webp, svg, bmp, ico. Others (tiff, avif) only if trivial. RAW and PSD are out of scope.

## 3. Architecture

Three parts, strictly separated:

1. **IconChest.Core** (class library): all logic. Scanning, hashing, metadata, thumbnails, queue, query language, database access, settings, import/export, backup. No UI and no HTTP.
2. **IconChest.Api** (ASP.NET Core Web API): thin HTTP layer over Core. It also serves the front-end static files (HTML/JS/CSS) from the same origin, so the desktop app and a future website use the same site.
3. **IconChest.Desktop** (.NET + Photino.NET): a thin shell. It starts the API as a **child process** on a free localhost port, waits until healthy, then opens a Photino window pointing at `http://127.0.0.1:{port}`. It holds no business logic and never touches the database.

The front end is plain HTML, CSS and JavaScript (ES modules, no framework, no bundler required). It talks to the API only through `fetch`.

Plus **IconChest.Cli**, a small developer console tool (see section 6.6).

### 3.1 Desktop shell responsibilities

- Single-instance protection (second launch focuses the first window and exits).
- Generate a random per-launch secret (at least 32 bytes, from a cryptographic RNG) and pass it to the API child via environment variable or stdin, never the command line.
- Pick a free port, start the API bound to 127.0.0.1, poll a health endpoint, call `POST /api/session` with the secret, then open the window. The session cookie lives in the web view.
- Ensure the child process is killed when the shell exits, including after a crash (job object on Windows, process-group or parent-death handling on Linux).
- Remember window size, position and maximised state in settings.json (section 6.3).
- If Photino fails on a platform, the documented fallback is to open the default browser at the same URL. Do not change the architecture for this.
- Native-only features (folder picker, open in system viewer, open log folder, reveal file) are implemented as API endpoints that act on the machine running the API, and the front end hides them when the API reports it is not local.

### 3.2 Website mode (future, design for it now)

The API can run standalone. Single user, LAN or localhost. Authentication is the same session cookie behind a login page with a password. Native-only features are hidden. HTTPS is not required now; note the plain-HTTP token exposure as a known, accepted limitation in the docs.

## 4. Technology choices

| Concern | Choice |
| --- | --- |
| Runtime | .NET 10 (`net10.0`), C#, nullable enabled, warnings as errors in Core |
| Database | SQLite via Microsoft.Data.Sqlite and Dapper. WAL mode, foreign keys on. Simple versioned SQL migration runner (embedded scripts, `schema_version` table). No EF Core. |
| Images | SkiaSharp (MIT) for decode and thumbnails. Svg.Skia (MIT) for SVG rasterising and SVG dimensions. MetadataExtractor (Apache 2.0) for EXIF. |
| Hash | XxHash128 from System.IO.Hashing (MIT). Store as lowercase hex, with the algorithm name stored alongside. |
| Logging | Serilog behind `Microsoft.Extensions.Logging` with the file sink. Runtime-switchable level. |
| Desktop | Photino.NET |
| API docs | OpenAPI with Swagger UI, served same-origin |
| Tests | xUnit, `WebApplicationFactory` for API integration tests. Playwright end-to-end tests optional, late. |

Linux note: Photino needs the WebKitGTK runtime package. Verify the correct package name for the target distro (Linux Mint / Ubuntu base) and document it. The front end must work in both WebView2 (Windows) and WebKitGTK (Linux), so avoid cutting-edge browser APIs without checking support.

## 5. Solution layout

```
IconChest/
  Directory.Build.props        (version, shared settings)
  IconChest.sln
  src/
    IconChest.Core/
    IconChest.Api/             (wwwroot = front end)
    IconChest.Desktop/
    IconChest.Cli/
  tests/
    IconChest.Core.Tests/
    IconChest.Api.Tests/
  docs/
    THIRD-PARTY-NOTICES.md
    QUERY-LANGUAGE.md
```

Core is organised by feature folder (Scanning, Thumbnails, Query, Data, Settings, Logging, Tags, Backup), with interfaces at boundaries so the file system, clock and database can be faked in tests.

## 6. Cross-cutting conventions

### 6.1 Versioning

`Major.Minor` is edited by hand in `Directory.Build.props`. Build and revision are generated at every build: build = days since 2024-01-01, revision = seconds since midnight (UTC) divided by 2 (assembly version parts cannot exceed 65535). Both values go into the assembly version and informational version. A build step also writes `version.json` (full version, build date as ISO 8601 UTC) next to each executable. No dependence on git. Version and build date are read at runtime from the assembly or `version.json`.

### 6.2 Logging

- Levels: **Off, Error, Warning, Information, Debug, Trace.** Default Information. One setting controls both API and desktop shell.
- One file per day per component: `api-YYYYMMDD.log` and `app-YYYYMMDD.log` (app = desktop shell and CLI). Retain the newest 30 days; delete older files automatically at startup and on rotation.
- Line format: timestamp with offset, level, source context, message, exception. Plain text.
- Never log secrets, cookies or passwords. Never log full file contents.
- The current log file path is exposed via API for the App Info settings section. Log folder is in the app data folder.

### 6.3 Settings file (settings.json)

All application settings live in one JSON file, `settings.json`, in the per-user config folder (Windows: `%APPDATA%/IconChest`, Linux: `$XDG_CONFIG_HOME` or `~/.config/IconChest`). **The database holds only library data** (folders, images, tags, the thumbnail queue) and its schema version. It never stores app preferences. `settings.json` holds the DB path, thumbnail cache path and max size, log level, window size and position, theme and appearance options, include and exclude patterns, scan options, startup view, LAN-access flag, API port preference and the hashed LAN password. The file is read before the database opens, so logging and the DB location are available first. A settings service in Core owns the file: a typed settings class with defaults for missing keys, a `version` field with simple migration, tolerance of unknown keys, thread-safe access, change notifications, and atomic writes (write a temp file, then replace). A corrupt file is renamed to `settings.json.corrupt`, logged at Warning, and replaced with defaults. The API exposes settings endpoints, and the front end never reads the file directly. Default DB and cache locations are in the per-user data folder (`~/.local/share/IconChest` or `%LOCALAPPDATA%/IconChest`).

### 6.4 General rules

- Store all timestamps as UTC ISO 8601 text. Display in local time in the UI. The user-facing date format is `yyyy-mm-dd`.
- Paths: store full absolute paths as the OS reports them, plus a normalised comparison key (case-insensitive on Windows). Use `Path` APIs, never string-split on separators.
- All I/O is async where it touches disk or network. Long jobs (scan, thumbnails) are background services with progress and cancellation tokens.
- API errors use RFC 7807 problem details. Validation errors are 400, missing items 404.
- Every SQL statement is parameterised. Dynamic parts (sort column, query-language fields) come from fixed whitelists only.
- A file that cannot be read (permissions, corrupt) is logged at Warning, recorded with a status flag, and never stops the scan.
- Do not use localStorage for anything the user would expect to survive a move to the website version. UI state that is per-device (pane widths, last view) may use it.

### 6.5 Security

- Secret handling as in section 10. Cookie is `HttpOnly`, `SameSite=Strict`, session-scoped.
- The original-file endpoint serves only files recorded in the library (by image id), never arbitrary paths. Set correct content types and `X-Content-Type-Options: nosniff`. SVGs are served with a restrictive Content-Security-Policy so embedded scripts cannot run.
- Folder-picker and open-in-viewer endpoints exist only when the API is local, and reject requests otherwise.

### 6.6 Dev CLI (`IconChest.Cli`)

A console tool using Core directly and the API over HTTP. Commands (grow per phase): `scan <folder>`, `list`, `hash <file>`, `thumbs run`, `api login`, `api get <path>`, `query validate "<text>"`, `query run "<text>"`, `log-test` (writes one message at each level), `version`. It keeps its API cookie in a temp file. It is a developer aid, not shipped to end users.

## 7. Data model (guide, refine in phase 1)

The database holds only library and API data. App settings live in `settings.json` (section 6.3), not in the database.

- `library_folder`: id, path, enabled, last\_scan\_utc, include\_patterns (nullable override).
- `image`: id, folder\_id, path, path\_key, file\_name, extension, format, width, height, size\_bytes, hash, hash\_algo, created\_utc, modified\_utc, exif\_date\_utc, taken\_override\_utc, added\_utc, title, description, rating (0-5), is\_favourite, is\_hidden, is\_missing, missing\_since\_utc, is\_duplicate, has\_transparency (nullable), status, exif\_json (nullable).
- `tag`: id, name (unique, case-insensitive), fg\_color, bg\_color.
- `image_tag`: image\_id, tag\_id.
- `thumbnail_job`: image\_id, size, state (pending, done, failed), attempts, error, updated\_utc. Unique per (hash, size) of work.
- `schema_version`.

Indexes on hash, folder\_id, path\_key (unique), is\_hidden, is\_missing, and date fields used by sorting. **Effective date** = first non-null of `taken_override_utc`, `exif_date_utc`, `created_utc`, `modified_utc`, stored or computed so it can be sorted and grouped efficiently (Years view includes an "Unknown" node for the rare case where all are null).

## 8. Scanning, hashing and file lifecycle

- Adding a folder to the library always scans recursively. Scans run per folder, in the background, one at a time per folder, with progress (files seen, new, changed, moved, missing, errors) and cancel.
- Include patterns: a setting holding a semicolon-separated list of globs, default `*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.ico`. An exclude list setting (default: `.thumbs;@eaDir;.git;node_modules`) skips matching directory names.
- Per file record: format, width, height, size, created and modified times, hash, EXIF as a JSON custom field where present (JPEG and others MetadataExtractor can read), transparency flag where cheap to determine.
- **Incremental:** if path, size and modified time match the existing record, skip hashing and metadata.
- **Move detection:** a missing path whose hash matches a newly seen file is a move. Update the path and keep id, tags, rating, edits, hidden flag and thumbnails. If the old path still exists, the new file is a duplicate instead.
- **Missing:** a recorded file not found during a scan gets `is_missing = 1` and a timestamp. If it reappears it is restored. Setting: auto-remove missing records after the next scan (off by default). Missing images are excluded from normal views.
- **Duplicates:** same hash and same size, flagged `is_duplicate`, recomputed at the end of each scan. Every file in a duplicate set is flagged. Hidden and missing files are not counted.
- **Hidden:** hidden files are still scanned and hashed (so they do not reappear as new) and stay hidden after a move. Excluded from all views, tree counts, search and the Duplicates page unless the user chooses to show hidden.
- **Removing a library folder:** deletes its image records, tag links and orphaned thumbnails after a confirmation. Never touches files on disk.
- Default title is derived from the filename (extension removed, underscores and dashes to spaces).
- Hash speed: stream the file with a buffer, never load whole files into memory. Scan worker count is a setting (Auto = min(4, cores)).

## 9. Thumbnails

- Sizes: **64, 128 and 256** (longest side), aspect ratio preserved, **never upscaled**.
- Every thumbnail is **WebP with alpha**, whatever the source. SVGs are rasterised. Animated GIFs get a static first frame.
- Cached on disk in the thumbnail cache folder, named by `hash` and size (so moved files and duplicates share thumbnails), in a two-level directory fan-out (first two hex characters).
- A persistent queue in the DB. After a scan finishes, jobs are enqueued for images lacking a thumbnail. Background workers drain the queue and the queue **resumes after restart**. Failed jobs retry a limited number of times, then stay `failed` with an error message and a placeholder is shown.
- The grid picks the smallest size at or above its displayed cell size.
- Settings: cache location, max cache size (oldest-used evicted when exceeded), clear cache button. Thumbnails of deleted images are removed on cleanup.
- API: `GET /api/images/{id}/thumbnail?size=64|128|256` with cache headers. If not ready, return a 202 or a placeholder so the grid can retry.
- The full-size preview uses the original file from the original-file endpoint. SVG renders natively.

## 10. Authentication

- `POST /api/session` accepts a secret (desktop and dev use) or a password (website mode) and sets the session cookie. `DELETE /api/session` logs out. All other endpoints except health, the login page and static assets require a valid session.
- Standalone mode secret: from environment variable or config. If neither is set, the API generates one and prints it to the console only, never to log files.
- Compare secrets in constant time. Rate-limit failed attempts.
- LAN access (bootstrap setting) switches binding from 127.0.0.1 to all interfaces, needs an API restart, and requires a user-set password (stored as a salted hash using a standard password hasher).
- Swagger UI is same-origin so calling `POST /api/session` there authenticates later calls. The CLI has `api login`.

## 11. Query language (the key feature, be precise)

A JQL/Wireshark-style expression language used in the **Advanced** search box. It must have a written specification (`docs/QUERY-LANGUAGE.md`), a hand-written recursive-descent parser producing an AST, a validator, and a compiler to parameterised SQL. The help page is generated from the same field definitions so they never drift.

### 11.1 Grammar (EBNF)

```
query      = [ orExpr ] ;                       (* empty query = match all *)
orExpr     = andExpr { OR andExpr } ;
andExpr    = unary { AND unary } ;
unary      = NOT unary | primary ;
primary    = "(" orExpr ")" | comparison | bareWord ;
comparison = field cmpOp value
           | field IN "(" value { "," value } ")"
           | field IS [ NOT ] EMPTY
           | field LIKE string
           | field CONTAINS string
           | folderField UNDER string ;
cmpOp      = "==" | "=" | "!=" | "<" | "<=" | ">" | ">=" ;
OR         = "or"  | "||" ;
AND        = "and" | "&&" ;
NOT        = "not" | "!" ;
value      = string | number | size | date | bool | identifier ;
string     = '"' chars '"' | "'" chars "'" ;     (* backslash escapes: \\ \" \' \* \? *)
number     = digit { digit } ;
size       = number [ "." digits ] ( "b" | "kb" | "mb" | "gb" ) ;   (* 1024-based *)
date       = digit4 "-" digit2 "-" digit2 ;      (* yyyy-mm-dd, unquoted *)
bool       = "true" | "false" ;
bareWord   = identifier | string ;              (* text search, see 11.4 *)
```

Keywords (`and or not in is empty like contains under true false`) and field names are case-insensitive. Whitespace is insignificant between tokens. **Precedence:** `not` over `and` over `or`. There is no implicit AND: two bare words in a row is a syntax error with a hint to add `and`. Limits: query length 2000 characters, nesting depth 20.

### 11.2 Fields

| Field | Type | Notes |
| --- | --- | --- |
| `type` | text | Format: png, jpg, gif, webp, svg, bmp, ico. `jpeg` is an alias of `jpg`. |
| `width`, `height` | integer | Pixels |
| `size` | size | Bytes, accepts `10kb`, `1.5mb` |
| `filename` | text | File name with extension |
| `title`, `description` | text | Editable fields |
| `folder` | text | Full folder path. `==` exact. `under` includes subfolders. |
| `tag` | multi-text | See 11.4 |
| `rating` | integer 0-5 | 0 means unrated |
| `favourite` | bool | `favorite` accepted as alias |
| `hidden`, `missing`, `duplicate` | bool | See 11.5 |
| `date` | date | Effective date |
| `created`, `modified`, `taken`, `added` | date | `taken` is the override, else EXIF. |
| `transparent` | bool | If known |

### 11.3 Operators and semantics

- Text comparisons are case-insensitive. `==` and `=` are exact match; `!=` its negation. `like` uses wildcards `*` (any run) and `?` (one character); the match is over the whole value, so `title like "elepha*"` is a prefix match. `contains "x"` is `like "*x*"`. Literal `*` or `?` are written `\*` and `\?`.
- Numbers and sizes support all comparison operators. Comparing a number field to a text value is a validation error.
- Dates compare on the calendar date (local time). `date == 2026-10-30` matches that whole day. `<` is before the start of that day, `<=` through the end of it.
- `in (a, b, c)` is true if the field equals any listed value. `not in` is written `not field in (...)`.
- `is empty` is true for null or empty text, and rating 0. `is not empty` is its negation.
- Using an operator on an incompatible field type (for example `like` on `width`) is a validation error naming the field and allowed operators.

### 11.4 Tags and bare words

- `tag == "dog"`: image has that tag. `tag != "dog"`: image does **not** have it. `tag like "do*"`: has any matching tag. `tag in ("dog","cat")`: has either. `tag is empty`: untagged.
- A bare word such as `elephant` or `"red car"` is a text search across filename, title and description (contains, case-insensitive).

### 11.5 Implicit filters

Images with `is_hidden = 1` are excluded unless the query mentions the `hidden` field. Images with `is_missing = 1` are excluded unless the query mentions `missing`. A query such as `hidden == true` shows only hidden images.

### 11.6 Errors

Every error is a structured object: `code`, `message`, `position` (zero-based character index), `length`, and where useful `suggestion` (for example "unknown field 'tpye', did you mean 'type'?"). The parser reports the first error but should recover enough to give one good message. The UI underlines the error position. Include at least: unexpected token, unterminated string, unknown field, invalid operator for field, invalid value, invalid date (including impossible dates), unbalanced parentheses, nesting too deep, query too long.

### 11.7 Examples that must work

```
tag == "dog"
type == png or type == jpg
date < 2026-10-30
title like "elepha*" and not tag == "draft"
(width >= 256 and height >= 256) and size < 100kb
folder under "/home/me/icons/social" and favourite == true
tag is empty and rating >= 4
elephant
hidden == true
```

### 11.8 Compiler requirements

The compiler turns the AST into a SQL `WHERE` fragment plus a parameter dictionary. It never concatenates user text into SQL. Multi-value tag conditions use `EXISTS` sub-queries. The result must be usable together with sorting and paging. Required tests: every operator for every field type, precedence and parentheses, empty and whitespace queries, every error code with its position, SQL injection attempts (quotes, comments, semicolons, `' OR 1=1 --`), very long and deeply nested input, unicode, and round-trip tests that run each documented example against a seeded in-memory database.

## 12. User interface

### 12.1 Layout

Three columns: a thin **icon bar** on the far left, **Panel A** (the view pane, resizable by dragging its edge handle, collapsible by clicking the active icon), and **Panel B** (main area). A details panel slides in from the right of Panel B when images are selected.

Icon bar, top to bottom: **Folders**, **Years**, **Tags**, **Favourites**, **Duplicates**, **Missing**, then at the bottom **Help** and **Settings**. Folders, Years and Tags show a tree in Panel A. Favourites, Duplicates, Missing, Help and Settings hide Panel A and give Panel B the full width. Switching views preserves each view's state (selection, filters, scroll position, expanded nodes). Settings is a page, not a modal. More view icons will be added later, so the icon bar is data-driven.

### 12.2 Trees

- **Folders:** each library folder is a top-level node, with its real sub-folder structure. Clicking a folder shows only its own images and expands its children. A toolbar toggle "Include subfolders" (off by default) widens the view. Counts shown.
- **Years:** a dropdown above the tree chooses the date field: Effective (default), Created, Modified, Date taken, Date added. Tree is Year, then Month. Clicking a year shows the whole year. Includes an "Unknown" node if needed.
- **Tags:** flat, alphabetical. No "All" node by default (the view with nothing selected shows everything). Right-click or inline actions to rename or delete (with a warning that it will be removed from all images).

### 12.3 Filter and sort bar (top of Panel B)

Two tabs: **Simple** and **Advanced**. They are mutually exclusive; switching tabs clears the other.

- **Simple:** dropdowns (every list starts with All): date range, tag, type, folder, dimensions (size bands), rating, favourite, show hidden. A text box searches filename, title and description. **Filter** and **Clear** buttons. A "Convert to query" button copies the current choices into the Advanced tab as query text.
- **Advanced:** the query text box, a Run button (Enter also runs), inline error display with position, and an info icon opening the Help page at the query section.
- **Sort:** field dropdown plus ascending/descending toggle. Fields: filename, title, effective date, created, modified, date taken, date added, size, width, height, type, rating, favourite, folder. Hidden tiebreaker on id.

### 12.4 Grid

- Virtualised (only visible rows plus a small buffer are rendered) so tens of thousands of images scroll smoothly. Data is paged from the API.
- Cell size is set by a slider at the bottom. Optional caption under each image: None, Filename or Title (setting).
- Transparency background behind images: Checkerboard, Light or Dark (toolbar toggle, remembered).
- Heart icon on each cell toggles favourite (filled when favourited).
- Missing thumbnails show a placeholder and refresh automatically when ready.

### 12.5 Selection and keyboard

Behave like a desktop file browser: click selects one; Ctrl/Cmd+click toggles; Shift+click selects a range; Ctrl+A selects all in the current view; arrows move focus, Shift+arrows extend; Space toggles the focused item; Home/End/PageUp/PageDown move focus. Buttons: **Select all**, **Clear selection**, **Preview**. Single-key shortcuts (Space, arrows, `I`, Escape) are ignored while a text input, textarea or select has focus. There is **no** keyboard shortcut for hiding images.

### 12.6 Details panel

Shown when images are selected; `I` toggles it. Editable: title, description, tags (multiple, create new inline), rating (0-5 stars), taken-date override, hidden checkbox. Read-only: dimensions, format, file size, hash, dates, EXIF, full path and name. Clicking the path copies it to the clipboard (with a brief confirmation). A button next to it opens the file in the system viewer (local mode only). With multiple images selected, fields with differing values show "Multiple values" and only fields the user changed are applied on Save. Read-only fields that differ show "Multiple". Saving hidden refreshes the view and clears the selection. Tags have foreground and background colours, editable here, and shown everywhere tags appear.

### 12.7 Preview

Pressing Preview (or Enter on a focused image) covers the **entire app window** with the first selected image at full size, fit to window, using the original file on a transparency background. Left/Right navigate: through the selection if more than one image is selected, otherwise through the current view. Escape or a close button (top right) exits. `I` toggles an info overlay (bottom right: title, filename, dimensions, size, date). The overlay's default visibility is a setting. No zoom or pan.

### 12.8 Other pages

- **Duplicates:** grid of duplicate images, grouped so duplicates sit together with a visual group divider. Clicking an image opens its details. No actions yet beyond viewing.
- **Missing:** list of missing files (path, last seen), with buttons **Remove from library** and **Rescan**.
- **Help:** keyboard shortcut reference and the full query language reference (fields, operators, grammar, examples) generated from the same definitions as the parser. Example queries are clickable and run in Advanced.
- **Favourites:** a grid of all favourited images across the library (full-width, with the same filter and sort bar). It applies an implicit `favourite == true`, so the filters and query box narrow within the favourites. The heart icon on a cell un-favourites it, and the image leaves the view on the next refresh.

## 13. Settings page (sections)

All settings on this page are stored in `settings.json` (section 6.3), not in the database. The list of library folders is the exception: it is library data and stays in the database.

- **Library:** list of folders, each with Scan button, progress and Remove (warns); Add folder (native picker locally, text box otherwise); include patterns; exclude names; scan on startup.
- **Appearance:** theme (Light or Dark), grid caption (None, Filename, Title), default checkerboard mode, preview info overlay default.
- **Behaviour:** default view on startup, remember window state, confirm-before-delete toggles.
- **Performance:** thumbnail cache location and max size, Clear thumbnail cache, hash algorithm (read-only), scan worker threads (Auto or a number).
- **Cleanup:** count of missing files, Delete all missing records (and their thumbnails), setting to auto-remove missing after scan.
- **Logging:** log level, Open log folder (local only).
- **API:** current port (read-only), Allow LAN access checkbox (explains restart and password requirement), set password.
- **Data:** DB location (change and move), Backup now, Restore from backup (overwrite warning), Export tags (JSON), Import tags (JSON).
- **App info:** executable location, version, build date, current log file path, third-party licence notices.

## 14. Import, export and backup

- **Tag export** is a JSON file of tag names and colours only. **Import** merges by name, case-insensitive. The existing local tag wins on colour conflicts.
- Every import (tags, DB restore) shows a result summary to the user and writes it to the log. Tag import reports tags added, skipped (already exist), and invalid entries. Restore reports the backup file, schema version and record counts.
- **Backup** uses SQLite's online backup API, so it is safe while running. **Restore** first validates the file (integrity check, schema version not newer than the app supports), keeps the old database as `<name>.pre-restore`, swaps the files safely, and reloads. Warn the user before overwriting.
- Changing DB location moves the file after copy verification, updates `settings.json`, and reopens.

## 15. Testing rules

- xUnit. Core logic must have thorough unit tests; target every public behaviour in this brief for the current phase. Bugs fixed later get a regression test.
- File-system tests use a temporary folder and generated test images (create small PNG, JPG, GIF, WebP, SVG, BMP and ICO files in code, including transparent ones and a JPEG with EXIF). Never depend on the developer's own files.
- Database tests use real SQLite (in-memory or temp file), not mocks.
- Tests must run on Linux and Windows and must not depend on time zone, locale or file-system case sensitivity. Use an injected clock.
- API tests use `WebApplicationFactory` and cover auth (with and without a session), validation errors and the happy paths.
- Query language gets the most tests (see 11.8).
- At the end of each phase, provide: how to run the tests, and a short manual test checklist.

## 16. Third-party licences

Maintain `docs/THIRD-PARTY-NOTICES.md` listing every dependency with name, version, licence and URL, updated whenever packages change. Expected: SkiaSharp (MIT), Svg.Skia (MIT), MetadataExtractor (Apache 2.0, include the NOTICE text as required), System.IO.Hashing (MIT), Microsoft.Data.Sqlite (MIT), Dapper (Apache 2.0), Serilog and sinks (Apache 2.0), Photino.NET (Apache 2.0), Swashbuckle or equivalent (MIT), xUnit (Apache 2.0). Verify each licence when adding it. Prefer permissive open source; ask before adding any dependency with a restrictive or commercial licence. The App info section links to these notices.

## 17. Phases

1. Foundation (solution, versioning, logging, schema and migrations, settings.json store, test projects)
2. Scanning (folders, metadata, EXIF, hashing, moves, missing, duplicates, hidden) plus dev CLI
3. Thumbnails (queue, workers, WebP, SVG, resume)
4. API (session auth, static hosting, endpoints, OpenAPI)
5. Query language
6. App shell (Photino host, layout, settings page with Library and App info)
7. Browse and view (trees, grid, sort, selection, preview)
8. Details and editing (including tags)
9. Filtering and search UI, help page
10. Management pages (Duplicates, Missing, cleanup, import and export, backup and restore, remaining settings)
11. Polish and packaging (self-contained publishes for Windows and Linux, licence notices, performance)

Each phase prompt will restate its scope, deliverables, tests and a "how to test" note.

## 18. Assumptions to confirm or override

- Favourites is its own icon-bar view and applies the equivalent of the `favourite == true` filter.
- Auto-remove of missing records is off by default.
- Include-subfolders toggle state is remembered per session, not saved to the DB.
- Window state and pane widths are per-device, so they are not synced to a website instance.
