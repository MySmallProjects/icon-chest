# IconChest Phase 2 Prompt: Library and Scanning

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 code. Phase 1 must be complete and its tests passing before starting.

## Goal

Let the user add folders to the library and scan them. A scan finds image files, reads their metadata, hashes them, and records everything in SQLite. Rescans are incremental. The scanner detects moves, missing files and duplicates, and it preserves the user's edits. Nothing here touches the original files or folders. There is no API, UI or thumbnail work in this phase, and everything is exercised through Core and the dev CLI.

## Before you write any code

1. Read the existing Phase 1 solution and follow its conventions (settings service, logging, `AppPaths`, `IClock`, migrations, test layout).
2. Verify current versions and licences of SkiaSharp, Svg.Skia and MetadataExtractor, plus System.IO.Hashing. Confirm the native SkiaSharp assets work on Linux and Windows. Add all to `docs/THIRD-PARTY-NOTICES.md`, including the MetadataExtractor NOTICE text if its licence requires it.
3. Reply with a short plan and assumptions, then build.

## Scope

### 1. Library folders

- Core service `LibraryFolderService` with: add, remove, list, enable or disable, and get by id.
- **Add** validates that the path exists, is a directory, is readable, and is not already in the library. Reject a folder that is inside an existing library folder, or that contains one, with a clear typed error (otherwise files are counted twice). Store the path as the OS reports it, with a trailing separator removed.
- **Remove** deletes the folder's image records, tag links and thumbnail jobs through cascading deletes in one transaction. It never touches files on disk. (Thumbnail files on disk are cleaned up in Phase 3, leave a clearly marked hook.) Tags themselves are kept even if they become unused.
- Adding a folder does not scan it automatically in Core. The caller (CLI now, API later) decides.

### 2. Schema addition

Add migration `0002` (never edit `0001`). It adds `directory_path` and `directory_key` to `image` (the containing folder of each file, with a normalised comparison key) plus an index on `directory_key`, so Phase 7's folder tree and "files in this folder" queries are fast. Backfill is not needed because the table is empty. Explain any other column you find missing while implementing, and add it in the same migration.

### 3. Path keys

An injectable `IPathKeyProvider` turns a path into `path_key` and `directory_key`. Rules: absolute, fully normalised (no `..`, duplicate separators or trailing separator), Unicode NFC. On Windows the key is also case-folded (OrdinalIgnoreCase semantics). On Linux it is case-sensitive. The platform is injectable so both behaviours are testable on one OS.

### 4. Include and exclude rules

- Read `includePatterns` and `excludeNames` from the settings service (semicolon-separated, trimmed, empty entries ignored, case-insensitive).
- Include patterns are globs matched against the file name (`*` any run, `?` one character). Exclude names are matched against **directory names** at any depth, so an excluded directory is not entered. Put the glob matcher in a reusable class (`WildcardMatcher`), because the query language's `like` operator will reuse it in Phase 5. Matching must be linear-time safe (no regex backtracking on hostile patterns).
- Even if a pattern matches, the file must also be a format the app supports (png, jpg, jpeg, gif, webp, svg, bmp, ico). Unsupported matches are skipped silently at Debug level.
- Do not follow symbolic-link directories (avoids loops). Symlinked files are scanned as normal files.

### 5. Metadata extraction

Interface `IImageMetadataReader`, implemented with SkiaSharp (decode info without a full decode where possible), Svg.Skia (or XML parsing for SVG dimensions) and MetadataExtractor.

For each file record:

- **Format** by sniffing the file header, not the extension. If the extension disagrees with the content, record the real format and log at Information.
- **Width and height** in pixels. For JPEGs with an EXIF orientation of 5 to 8, swap them so they are the displayed dimensions. For SVG, use `width` and `height` (convert px, pt, pc, mm, cm, in; ignore percentages and em), else the `viewBox`, else leave null and set a status flag.
- **File size, created, modified.** `created_utc` is the file's creation time where the OS provides it, but if the platform returns nothing or a value later than modified, use modified.
- **EXIF:** for any file MetadataExtractor can read, store a flat JSON object in `exif_json` with a small, documented set of keys (camera make and model, lens if present, date taken, exposure time, f-number, ISO, focal length, orientation, software, GPS latitude and longitude as decimals if present). Set `exif_date_utc` from the original date taken. EXIF dates have no time zone: interpret them in an injected `TimeZoneInfo` (the local zone by default) and store UTC. An unreadable or absent EXIF block is not an error.
- **Transparency:** `has_transparency` is true or false for png, gif, webp and ico files up to 2 MB (true if any pixel is not fully opaque), and null when unknown (larger files, svg, jpg, bmp). Do not decode large photos just for this.
- **Status:** `ok`, or `unreadable` if the file cannot be decoded (corrupt, truncated, zero bytes, permission denied), or `no_dimensions` for an SVG with no determinable size. Unreadable files are still recorded (with size, times and hash when readable) so they can be seen and fixed, and logged at Warning. They never stop a scan.
- **Title:** the default title is the file name without extension, with underscores and dashes replaced by spaces and the whitespace collapsed. Do this only when a record is first created. Never overwrite a title on rescan.

### 6. Hashing

Interface `IFileHasher` with an XxHash128 implementation (System.IO.Hashing). Stream the file with a reusable buffer (about 80 KB), never load whole files into memory, support cancellation. Output is the 128-bit hash as 32 lowercase hex characters. Store `hash_algo = "xxh128"`. Because the hasher is behind an interface, tests can count calls.

### 7. The scan algorithm

Service `LibraryScanner` with `ScanAsync(folderId, progress, cancellationToken)` returning a `ScanResult`. Rules:

1. **Concurrency:** only one scan per folder at a time. A second request for the same folder fails fast with a typed error. Scans of different folders may run together.
2. **Safety check first:** if the folder's root path does not exist or cannot be listed (an unplugged drive, a renamed parent), fail the scan with a clear error, log it, and change nothing. A missing root must never mark every file missing.
3. **Enumerate** the tree recursively, applying include and exclude rules. Unreadable subdirectories are logged at Warning and skipped, and the scan is then flagged as `incomplete` (see step 8).
4. **Load** the folder's existing records into a dictionary by `path_key`.
5. **Classify each file found:**
   - Known path with the same size and the same modified time: **unchanged**. Do not open or hash it. If it was marked missing, restore it (`is_missing = 0`).
   - Known path with a different size or modified time: **changed**. Re-read metadata and re-hash, update the technical fields, and **keep every user field** (title, description, rating, favourite, hidden, taken override, tags). If the hash changed, update `hash`.
   - Unknown path: **new candidate**. Read metadata and hash it.
6. **Hash and read in parallel** using `scanWorkers` from settings (0 = Auto = min(4, logical cores)). Database writes happen on a single writer, in batches of about 500 rows per transaction.
7. **Reconcile moves.** The pool of move candidates is every record in the **whole library** (not only this folder) whose file no longer exists on disk. Check `File.Exists` for records of this folder that were not found in the enumeration, and include records already flagged missing. Match each new candidate to the pool by `hash` and `size`. A match is a **move**: update `path`, `path_key`, `directory_*`, `file_name`, `folder_id`, modified time and so on, keep the record id, and clear the missing flag. This preserves tags, rating, favourite, hidden state and (later) thumbnails. If several pool entries match, prefer the same file name, then the lowest id. If an entry in the pool has the same hash but its file **still exists** on disk, the new file is not a move: it is a new record and a duplicate.
8. **Mark missing.** Records of this folder that were not found, and that no new file claimed as a move, get `is_missing = 1` and `missing_since_utc` (only if not already set). **Skip this step if the scan was cancelled, incomplete, or hit a fatal error.**
9. **Auto-remove:** if the `autoRemoveMissing` setting is true, delete this folder's missing records at the end of a complete scan (the setting is off by default).
10. **Duplicates:** after reconciling, recompute `is_duplicate` for the affected hashes (every hash seen, changed or moved in this scan, including those just vacated). A duplicate set is two or more records with the same hash and size that are neither hidden nor missing. Zero-byte files are never duplicates. Expose `RecomputeDuplicates(hashes)` and `RecomputeAllDuplicates()` from the image repository too, because hiding and unhiding files (Phase 8) changes duplicate sets.
11. **Update** `library_folder.last_scan_utc` on completion, including completion with warnings, but not on a fatal error.
12. **Cancellation:** leaves the database consistent (finished batches stay, the current batch is rolled back). Cancelling is a normal outcome and is reported in `ScanResult`, not thrown at the caller.

**Progress reporting:** an `IProgress<ScanProgress>` with the stage (Enumerating, Reading, Reconciling, Finalising), counts of files found, processed, new, changed, unchanged, moved, missing, duplicates and errors, and the current file path. Throttle updates (for example at most 10 per second) so a fast scan does not flood the consumer.

**ScanResult:** the same counters, duration, whether it was cancelled or incomplete, and a list of the first 50 warnings and errors with paths.

**Time:** a scan of a folder with no changes must not read any file contents. Prove it in a test with a counting hasher and counting metadata reader.

### 8. Repositories

Keep them thin. `ILibraryFolderRepository` and `IImageRepository` with Dapper. The image repository needs only what scanning, the CLI and the tests require: insert, update technical fields, update path (move), get by id or path key, list by folder with paging, counts, mark missing and restore, delete missing for a folder, set hidden for ids (used by tests and Phase 8), recompute duplicates, and a simple filtered list (folder, include hidden, include missing, duplicates only) with limit and offset. Do not build the query language or full filtering here.

### 9. Dev CLI additions

| Command | Behaviour |
| --- | --- |
| `folder add <path>` | Adds a library folder, prints its id |
| `folder list` | Id, path, enabled, last scan, image count |
| `folder remove <id or path> [--yes]` | Asks for confirmation unless `--yes`, then removes |
| `scan <id or path>` or `scan --all` | Runs a scan with a live single-line progress display and prints the summary. `Ctrl+C` cancels cleanly. |
| `list [--folder id] [--hidden] [--missing] [--duplicates] [--limit n]` | Lists images in a compact table |
| `show <image id>` | Prints every field, including EXIF JSON |
| `hash <file>` | Prints the hash of any file |
| `image hide <id>` and `image unhide <id>` | Developer helpers for testing duplicates and hidden behaviour |
| `db stats` | Counts of folders, images, missing, hidden, duplicates, tags |

Use the same path overrides (`ICONCHEST_CONFIG_DIR`, `ICONCHEST_DATA_DIR`) as Phase 1.

## Out of scope

Thumbnails and the thumbnail queue (only leave the hook mentioned above), the API, authentication, any front end, tag management (the `tag` tables exist, but no tag service yet), the query language, scheduled or startup scans, folder watching, backup and restore.

## Required tests

Generate all test images in code or from small fixtures you create yourself (no third-party images). SkiaSharp can encode PNG, JPEG and WebP; build minimal GIF, BMP and ICO files from bytes, SVG from strings, and a JPEG with EXIF (including an orientation tag and a date) by writing the EXIF segment yourself or embedding a tiny generated fixture. Include transparent and fully opaque PNGs. Use temp folders per test. Tests must pass on Linux and Windows and not depend on time zone or locale.

- **Library folders:** add, duplicate add, nested add (both directions), non-existent path, file instead of directory, remove cascades to images and tag links but keeps tags, and removal leaves disk files untouched (assert the files still exist).
- **Path keys:** normalisation, Unicode, trailing separators, both platform behaviours.
- **WildcardMatcher:** `*`, `?`, empty pattern, case-insensitivity, escaping, a hostile pattern such as many `*` in a row against a long string completes quickly.
- **Include and exclude:** defaults, custom patterns, excluded directory at depth, unsupported extension with matching pattern, symlinked directory not followed (skip this test where symlinks cannot be created).
- **Metadata:** each format (png, jpg, gif, webp, svg, bmp, ico) gives correct format, width, height and size. Wrong extension is detected. SVG unit conversion, `viewBox` fallback and no-size case. EXIF fields and date conversion with an injected time zone. Orientation swaps dimensions. Transparency true, false and null cases. Corrupt, truncated and zero-byte files give `unreadable` without throwing.
- **Hash:** known test vectors for XxHash128, same content gives same hash, one-byte difference differs, cancellation stops a large hash promptly.
- **Scanner, first scan:** all supported files recorded, titles derived correctly, counts correct.
- **Scanner, rescan:** unchanged files cause zero hash and metadata calls; a changed file is re-read but keeps title, description, rating, favourite, hidden, taken override and tags.
- **Moves:** rename within a folder, move to a sub-folder, move to another library folder, move with edits preserved (same id), two identical files with one moved, multiple candidates preferring the same name.
- **Missing:** deleted file marked missing with a timestamp; reappearing file restored; a missing root path changes nothing and reports an error; a cancelled scan marks nothing missing; an incomplete scan (unreadable subdirectory, where the OS allows making one) marks nothing missing; `autoRemoveMissing` deletes only after a complete scan.
- **Duplicates:** copy a file, so both are flagged; delete one, so the other is cleared; hide one, so the other is cleared; zero-byte files are not duplicates; a duplicate of a hidden file is not flagged.
- **Hidden:** hidden state survives a rescan and a move.
- **Concurrency:** a second scan of the same folder is rejected; two folders scan in parallel without errors or lost writes; results are identical for 1 worker and 4 workers.
- **Progress and cancellation:** progress is monotonic and throttled; cancel mid-scan leaves a consistent database that the next scan completes.
- **Resilience:** a file that disappears or becomes locked mid-scan is logged and skipped; Unicode and space-containing names work.
- **Performance (mark as slow or explicit, not in the default run):** scan 5,000 small generated images, then rescan, and print the timings. A rescan with no changes should be many times faster than the first scan.

## Working rules

- Follow the master brief and the Phase 1 conventions. Where silent, pick the simplest option and note the assumption.
- Keep Core free of UI and HTTP concerns. Interfaces only where they help testing (hasher, metadata reader, clock, path keys, file enumeration if needed).
- Log scan start and summary at Information, per-file decisions at Debug, unreadable files at Warning, never file contents.
- Never write to, rename or delete anything inside a library folder. Add a test that scans a read-only copy of a folder where the OS allows it.
- If a requirement here conflicts with the brief, follow this prompt and tell me.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux with no warnings in Core.
- All required tests exist and pass. List any you could not write and why.
- README and `THIRD-PARTY-NOTICES.md` updated, and the migration `0002` explained.
- Your final reply includes: new files, assumptions, deviations from the brief, any decisions you want me to confirm, and the manual checklist below with exact commands.

## How I will test Phase 2

1. Make a folder with a mix of png, jpg (one with EXIF from a real camera), gif, webp, svg, bmp and ico files, plus a sub-folder and a text file.
2. `iconchest folder add <path>`, `iconchest scan <id>`, then `iconchest list` and `iconchest show <id>`. Check formats, sizes, dimensions, EXIF JSON, transparency, and that the text file is ignored.
3. Run `scan` again and confirm it finishes almost instantly and reports everything as unchanged.
4. Rename a file, move one into a sub-folder, delete one, and copy one. Rescan. Confirm one move (same id), one missing, and a duplicate pair in `list --duplicates`.
5. Use `image hide` on one of the duplicates and confirm the duplicate flag clears. Unhide it and recompute with a rescan.
6. Unmount or rename the whole folder, rescan, and confirm the scan reports an error and marks nothing missing.
7. Press `Ctrl+C` during a large scan, rerun it, and confirm it completes.
8. `iconchest folder remove <id>` and confirm the records go but the files on disk remain.
9. Run `dotnet test` and the slow performance test, and check the timings.
