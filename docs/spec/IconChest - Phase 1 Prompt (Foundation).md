# IconChest Phase 1 Prompt: Foundation

Paste the (revised) Master Project Brief first, then this prompt, into a fresh AI coding session.

## Goal

Build the foundation of IconChest: the solution structure, versioning, logging, the `settings.json` settings service, the SQLite schema with migrations, a minimal dev CLI, and the test projects. There is no UI, scanning, thumbnailing or real API in this phase. Everything later builds on this, so correctness and tests matter more than features.

## Before you write any code

1. Run `dotnet --list-sdks`. If no .NET 10 SDK is installed, stop and give me the exact install steps for Linux Mint / Ubuntu and for Windows. Do not fall back to .NET 8.
2. Verify the current stable versions and licences of every NuGet package you plan to use. Do not rely on memory.
3. Reply with a short plan listing your assumptions, then build it. Do not ask questions unless something blocks you.

## Scope: what to build

### 1. Solution and projects

- Solution `IconChest.sln` with the layout from section 5 of the brief, all targeting `net10.0`.
- Projects: `IconChest.Core` (class library, nullable on, warnings as errors), `IconChest.Api` (ASP.NET Core, only a `GET /health` endpoint returning 200 for now), `IconChest.Desktop` (builds and runs as a placeholder that prints its version and exits; Photino comes in phase 6), `IconChest.Cli` (console), `IconChest.Core.Tests` and `IconChest.Api.Tests` (xUnit; the API test project needs one `WebApplicationFactory` smoke test for `/health`).
- Add `.gitignore`, `.editorconfig`, a `README.md` (prerequisites, build, test and run instructions) and `docs/THIRD-PARTY-NOTICES.md` listing every package used in this phase with version, licence and URL.
- Optional: a GitHub Actions workflow that builds and tests on `ubuntu-latest` and `windows-latest`. Skip it if you think it is premature, and say so.

### 2. Versioning (brief section 6.1)

- `Directory.Build.props` holds hand-edited `VersionMajor` and `VersionMinor`. Build number = days since 2024-01-01 (UTC). Revision = seconds since UTC midnight divided by 2. Both must fit in 0-65535.
- Compute them at build time with MSBuild property functions. Set assembly version, file version and informational version for every project.
- A build step writes `version.json` (`version`, `buildDateUtc` as ISO 8601) next to each built executable.
- Core exposes a `VersionInfo` service giving full version, build date and executable location, read from the assembly with `version.json` as a fallback.
- Confirm that building twice some minutes apart produces different versions, including when only one project changed. If MSBuild's incremental build skips regeneration, fix it and explain how.

### 3. Logging (brief section 6.2)

- Core provides a logging setup on Serilog behind `Microsoft.Extensions.Logging`, with a component name (`api` or `app`) that decides the file name: `api-YYYYMMDD.log`, `app-YYYYMMDD.log`.
- Levels: Off, Error, Warning, Information, Debug, Trace (map Trace to Serilog Verbose; Off must produce no output at all). Default Information. The level can change at runtime through a `LoggingLevelSwitch` without restarting.
- One file per day, plain text, line format: timestamp with offset, level, source context, message, exception.
- Keep at most 30 days of files per component. Delete older files at startup and after rotation, decided by the date in the file name, using an injected clock. Never throw if a file is locked or cannot be deleted; log a warning instead.
- Expose the current log file path and the log folder path.
- The log folder defaults to a `logs` folder inside the per-user data folder.
- Do not log secrets. Provide a short note in the README on this rule.

### 4. Paths and the settings service (brief section 6.3)

- An `AppPaths` service resolves the config folder, data folder and default DB, cache and log locations for Windows and Linux. It must be overridable by environment variables `ICONCHEST_CONFIG_DIR` and `ICONCHEST_DATA_DIR`, so tests and dev runs never touch real user folders.
- A `SettingsService` owns `settings.json` as described in section 6.3: typed `AppSettings` class, `version` field with a migration hook, defaults for missing keys, unknown keys ignored but preserved on save, thread-safe get and update, a change event, and atomic writes (write a temp file in the same folder, then replace). A corrupt or unreadable file is renamed to `settings.json.corrupt`, logged at Warning, and replaced with defaults.
- Use camelCase JSON, enums as strings, indented output so it can be read and hand-edited.
- Validate on load and on update: clamp or reject out-of-range values (for example negative sizes, unknown theme names) with clear errors.
- Defaults:

| Key | Default |
| --- | --- |
| `version` | 1 |
| `dbPath` | null (use default data folder) |
| `thumbnailCachePath` | null (use default) |
| `thumbnailCacheMaxMb` | 2048 |
| `logLevel` | Information |
| `theme` | Light (Light or Dark) |
| `gridCaption` | None (None, Filename or Title) |
| `checkerboardMode` | Checker (Checker, Light or Dark) |
| `previewInfoOverlayDefault` | true |
| `defaultView` | Folders |
| `rememberWindowState` | true |
| `window` | width 1280, height 800, x and y null, maximised false |
| `includePatterns` | `*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.ico` |
| `excludeNames` | `.thumbs;@eaDir;.git;node_modules` |
| `scanOnStartup` | false |
| `scanWorkers` | 0 (0 means Auto) |
| `autoRemoveMissing` | false |
| `confirmBeforeDelete` | true |
| `api.portPreference` | 0 (0 means random free port) |
| `api.allowLan` | false |
| `api.lanPasswordHash` | null |

### 5. Database and migrations (brief section 7)

- Microsoft.Data.Sqlite and Dapper. A connection factory takes the DB path from the settings service (or an explicit path for tests). On every open set `PRAGMA foreign_keys = ON`, and enable WAL once on a file database.
- A migration runner applies embedded, numbered `.sql` scripts (`0001_initial.sql`, and so on) in order, each in a transaction, recording each in `schema_version`. It is safe to run repeatedly. If the database has a newer schema version than the app knows, refuse to open it with a clear typed exception and do not modify the file.
- `0001_initial.sql` creates `library_folder`, `image`, `tag`, `image_tag`, `thumbnail_job` and `schema_version` per section 7 of the brief. There is **no settings table**.
- Constraints and indexes: unique `path_key`; `tag.name` unique and case-insensitive (`COLLATE NOCASE`); `image_tag` primary key on both ids; cascading deletes so removing a `library_folder` removes its images and tag links, and removing an image or tag removes its links; indexes on `hash`, `folder_id`, `path_key`, `is_hidden`, `is_missing` and the date columns used for sorting and grouping.
- Effective date (brief section 7): the first non-null of `taken_override_utc`, `exif_date_utc`, `created_utc`, `modified_utc`. Implement it as a SQLite generated column or a view, whichever lets it be indexed and sorted efficiently, and explain your choice. Test the fallback chain.
- Core exposes a small `IconChestDatabase` entry point (open, migrate, report schema version). No repositories or business logic yet.

### 6. Dev CLI (brief section 6.6, phase 1 commands only)

- `version` prints version, build date and executable path.
- `log-test [--level X]` writes one message at each level using the real logging setup, prints the log file path, and shows what was filtered.
- `settings show`, `settings get <key>`, `settings set <key> <value>` for simple keys.
- `db init` creates or migrates the database and prints its path and schema version. `db info` prints the path and schema version.
- Keep argument parsing simple (hand-rolled or System.CommandLine if its licence and stability suit). Print clear errors and use non-zero exit codes on failure. Use `AppPaths` so `ICONCHEST_CONFIG_DIR` and `ICONCHEST_DATA_DIR` work.

## Out of scope

Scanning, hashing, image metadata, thumbnails, query language, authentication and sessions, Photino, any front end, backup and restore, import and export, repositories or domain services. Do not stub these beyond what is listed.

## Required tests (xUnit, Linux and Windows safe)

- **Versioning:** build and revision values are within 0-65535; the version in the built assembly matches `version.json`; the formula helper (if you add one for testing) matches the MSBuild result for a sample date.
- **Logging:** each level setting passes and blocks the right messages; Off writes nothing; runtime level change takes effect immediately; file name contains the component and date; retention deletes files older than 30 days but never recent ones or other components' files; a locked file does not crash the cleanup.
- **Paths:** environment-variable overrides work; defaults differ correctly for the two platforms (use an abstraction so both can be tested on one OS).
- **Settings:** defaults on first run; round-trip save and load; unknown keys preserved; corrupt file is renamed and replaced with defaults; invalid values are rejected or clamped; concurrent updates do not corrupt the file; change event fires; atomic write leaves no temp file behind.
- **Database:** fresh migration succeeds; running it twice changes nothing; newer-schema DB is refused and left unmodified; foreign keys are enforced; unique and case-insensitive constraints behave; cascades work; WAL is on for a file DB; effective date falls back in the right order; a failed migration rolls back.
- **API smoke:** `/health` returns 200.
- Tests use temp folders and the env-var overrides, never real user folders, and never depend on time zone or locale.

## Working rules

- Follow the master brief. Where it is silent, pick the simplest option and note the assumption.
- No dependency without a verified permissive licence. Add each to `THIRD-PARTY-NOTICES.md`.
- Keep Core free of UI and HTTP concerns. Keep classes small and interfaces only where they help testing (clock, paths).
- Commit-sized, readable code with short comments on non-obvious decisions only.
- If a requirement here conflicts with the brief, follow this prompt and tell me.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux, with no warnings in Core.
- All required tests exist and pass. List any test you could not write and why.
- README explains prerequisites, build, test and CLI usage.
- Your final reply includes: a file tree, the assumptions you made, deviations from the brief, and the manual test checklist below with the exact commands for this repo.

## How I will test Phase 1

1. Run `dotnet build`, wait a minute, build again, then run `iconchest version` and confirm the version changed and matches `version.json`.
2. Run `dotnet test` and confirm everything passes.
3. Run `iconchest log-test` at each level and check the log file in the log folder. Confirm Off writes nothing, and that the file name has the component and date.
4. Copy in a log file dated 40 days ago, rerun, and confirm it is deleted.
5. Run `iconchest db init` and confirm the DB appears in the data folder. Open it in a SQLite browser and check the tables, constraints and `schema_version`.
6. Run `iconchest settings show`, change a value, hand-edit `settings.json` into garbage, and confirm the app recovers and keeps a `.corrupt` copy.
7. Set `ICONCHEST_CONFIG_DIR` and `ICONCHEST_DATA_DIR` to a temp folder and confirm nothing is written to the real user folders.
