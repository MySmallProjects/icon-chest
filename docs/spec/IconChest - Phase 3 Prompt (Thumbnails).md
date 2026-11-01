# IconChest Phase 3 Prompt: Thumbnails

Paste the Master Project Brief, then this prompt, into a fresh AI coding session that has the Phase 1 and 2 code. Phases 1 and 2 must be complete and their tests passing.

## Goal

After a scan, generate thumbnails in the background from a persistent queue. The queue survives restarts and resumes. Every thumbnail is a WebP with alpha, in three sizes (64, 128, 256), stored in a cache folder outside the library. The work is keyed by content hash, so moved files keep their thumbnails and duplicates share them. There is no API or UI in this phase; everything is exercised through Core and the dev CLI.

## Before you write any code

1. Read the existing solution and follow its conventions.
2. Confirm that SkiaSharp can encode WebP with alpha and that Svg.Skia renders on both Linux and Windows. Report any limitation before building around it.
3. Reply with a short plan and assumptions, then build.

## Scope

### 1. Schema (migration `0003`, never edit earlier migrations)

Phase 1's `thumbnail_job` was keyed per image. Work is really per `(hash, size)`, so replace it with a table `thumbnail` (the table is empty in practice because nothing populated it yet; state that you checked):

- Primary key `(hash, size)`.
- `state`: `pending`, `running`, `done`, `failed`, `evicted`, `orphaned`.
- `priority` (integer, higher first), `attempts`, `next_attempt_utc`, `error`, `bytes`, `width`, `height`, `created_utc`, `updated_utc`, `last_accessed_utc`, `claimed_utc`.
- Indexes for claiming work (`state`, `priority`, `size`, `next_attempt_utc`) and for eviction (`state`, `last_accessed_utc`).

Explain any further columns you add. Keep `schema_version` consistent.

### 2. Settings additions

Add to `AppSettings` (with defaults, validation and tests, following the Phase 1 rules): `thumbnailQuality` (1-100, default 85) and `thumbnailWorkers` (0 = Auto = min(4, logical cores), default 0). The existing `thumbnailCachePath` and `thumbnailCacheMaxMb` are now used.

### 3. Rendering

Interface `IThumbnailRenderer`, implemented with SkiaSharp and Svg.Skia. Rules:

- Fit the image inside a square of the target size, **preserving aspect ratio** (512x256 at size 64 gives 64x32). **Never upscale** raster images: a 32x32 icon keeps 32x32 at every size. **SVG is the exception:** render the vector to fit the target size, so small SVGs are sharp.
- Use high-quality downscaling (not nearest-neighbour). Handle premultiplied alpha correctly so transparent edges have no dark or white halos.
- Output is always **WebP with alpha**, using `thumbnailQuality`.
- Apply JPEG EXIF orientation so thumbnails are upright.
- Animated GIF: first frame only. ICO: use the largest frame. WebP and PNG: first frame.
- For large JPEGs use scaled decoding where Skia supports it, to limit memory. Reject sources over 200 megapixels (a decompression-bomb guard) with the error `too_large`.
- SVG safety: no script execution, and no loading of external files or URLs referenced from the SVG. A render that takes too long (use a timeout, for example 10 seconds) fails with the error `timeout`.
- Open source files read-only with sharing allowed (`FileShare.ReadWrite`). Never write inside a library folder.
- Return the encoded bytes plus the output width and height. Never throw for bad input; return a typed failure (`unreadable`, `too_large`, `timeout`, `unsupported`).

### 4. Cache storage

- Location: `thumbnailCachePath` from settings, else the default in the per-user data folder.
- Path: `<cache>/<first two hex chars of hash>/<hash>_<size>.webp`.
- Writes are atomic: write a temp file in the same directory, then rename. A partial file must never be served, and temp files left by a crash are removed at startup.
- A `ThumbnailCache` class owns file paths, existence checks, writes, deletes, total size and folder creation, behind an interface for tests.

### 5. The queue

Interface `IThumbnailQueue` with an implementation over the `thumbnail` table:

- **Enqueue** is idempotent. `EnqueueForImages(imageIds)`, `EnqueueForFolder(folderId)` and `EnqueueAll()` add jobs for the three sizes for each distinct hash that has **no row yet**. Skip missing images. Include hidden images (they can still be viewed). Do not re-enqueue `evicted` rows automatically. Do not enqueue zero-length or `unreadable`-status files.
- **Priorities:** default priority is 0. Within equal priority, smaller sizes are claimed first (64, then 128, then 256) so the grid fills quickly. `Prioritize(hash, size)` raises a job's priority so a requested thumbnail jumps the queue (the API will use this in Phase 4).
- **Claiming** is atomic. A worker claims one job by moving it from `pending` to `running` in a single statement so two workers never take the same job. Respect `next_attempt_utc`.
- **Source resolution:** at run time find any non-missing image with that hash and use its current path. If none exists, mark the job `orphaned` (it is cleaned up later, see section 8). If the file exists but cannot be read, that is a failure of the attempt.
- **Retries:** on failure increment `attempts`, store `error`, set `next_attempt_utc` with a growing delay (for example 30 seconds, 2 minutes, 10 minutes using the injected clock), and return the job to `pending`. After 3 attempts set `failed`. A failure from a deterministic cause (`unreadable`, `too_large`, `unsupported`) goes straight to `failed`. `RetryFailed()` resets failed jobs.
- **Crash recovery:** `RecoverInterruptedJobs()` returns every `running` job to `pending`. It runs when the worker starts.
- **Status:** `GetStatus()` returns counts per state and per size, plus a rate and rough ETA from recent throughput.

### 6. Worker

`ThumbnailWorker` is a plain class in Core (no ASP.NET dependency, so the API can host it in Phase 4 and the CLI can run it now):

- `Start()`, `StopAsync()` (graceful: finish the current job, then stop), `Pause()`, `Resume()`.
- Worker count from `thumbnailWorkers`. Idle workers wait efficiently (a signal when jobs are enqueued, plus a slow poll for delayed retries), never spin.
- Raises events or uses an `IProgress` for job completed and queue drained, so the API can report progress later.
- On start it recovers interrupted jobs and **resumes** the pending queue. Closing the process mid-run and reopening continues where it left off.
- After each successful job update `bytes`, `width`, `height` and `state = done`. Failure to write the cache or the DB is logged and treated as a failed attempt, not a crash.
- When the queue drains, run cache eviction (section 7).

### 7. Retrieval, access tracking and eviction

- `IThumbnailProvider.GetAsync(imageId, size)` returns `Ready` (with file path, content type `image/webp` and an ETag derived from hash and size), `Pending`, `Failed` (with the error), or `NotFound`. Requesting a thumbnail that has no row enqueues it with raised priority. Requesting a pending one calls `Prioritize`. A `done` row whose file has gone missing from disk **self-heals**: mark it pending and report `Pending`.
- A static helper `ThumbnailSizes.PickFor(displayPixels)` returns the smallest of 64, 128, 256 that is at least the requested size, capped at 256. Test it as a table.
- **Access tracking:** update `last_accessed_utc` when a thumbnail is served, but batch the writes in memory and flush on an interval (for example every 30 seconds) and on shutdown, so serving a grid does not cause a write storm.
- **Eviction:** when total cached bytes exceed `thumbnailCacheMaxMb`, delete the least recently accessed `done` thumbnails until the total is at or below 90% of the maximum. Delete the file and set the row to `evicted`. Evicted thumbnails regenerate only when something requests them. A scan does not re-enqueue them. Eviction runs after the queue drains and on demand, never while serving a single request.

### 8. Cleanup and cache management

- `CleanupOrphans()` deletes the files and rows for any hash that no image record references anymore. A hash still used by another record (a duplicate elsewhere) keeps its thumbnail.
- Call it from the Phase 2 hooks: after removing a library folder and after deleting missing records (manually or via `autoRemoveMissing`). Replace the Phase 2 "hook" comment with this call.
- `ClearCacheAsync()` pauses workers, deletes all thumbnail files and rows, resumes, and optionally re-enqueues everything.
- `VerifyCacheAsync()` walks the rows: a `done` row with no file becomes pending, a file with no row is deleted. Report counts.
- Changing the cache path in settings does not move files. The old thumbnails are simply regenerated lazily through self-healing, and the old folder is left alone (document this).

### 9. Scanner integration

After a scan finishes without cancellation, the scanner (or its caller through a clean abstraction) calls `EnqueueForImages` for the new, changed, moved and restored images of that scan. Enqueue failure must be logged and must never fail the scan. If a worker is running it picks up the work immediately.

### 10. Dev CLI additions

| Command | Behaviour |
| --- | --- |
| `thumbs status` | Counts per state and size, cache size and location |
| `thumbs enqueue [--folder id]` | Enqueues jobs, prints how many were added |
| `thumbs run [--until-empty]` | Runs the worker with a live progress line. `Ctrl+C` stops gracefully. With `--until-empty`, exits when the queue is empty. |
| `thumbs retry-failed` | Resets failed jobs |
| `thumbs clear [--yes]` | Clears the cache after confirmation |
| `thumbs verify` | Runs cache verification and prints the report |
| `thumbs cleanup` | Removes orphans |
| `thumbs show <image id>` | Shows the state, path and dimensions of each size |
| `thumbs make <file> --size N --out <path>` | Renders one file for a visual check. Refuse if `--out` is inside a library folder. |

`scan` should now also print how many thumbnail jobs were enqueued.

## Out of scope

The API and its thumbnail endpoint, the grid, full-size preview, placeholders, thumbnail formats other than WebP, video, animated thumbnails, user-chosen thumbnail sizes, GPU use.

## Required tests

Generate all images in code (reuse the Phase 2 helpers). Temp folders for library and cache. Pass on Linux and Windows, independent of time zone and locale. Use the injected clock for retry timing.

- **Renderer:** output is valid WebP (check the RIFF and WEBP header and decode it back); dimensions for landscape, portrait and square sources at all three sizes; never upscales a small raster; SVG renders a non-empty image at the target size, including a tiny SVG scaled up; transparent PNG keeps transparent corners and a known interior pixel colour within tolerance; semi-transparent edges have no halo; animated GIF yields the first frame; ICO uses the largest frame; EXIF orientation produces an upright result; corrupt, truncated and zero-byte inputs return typed failures; the 200-megapixel guard; an SVG referencing an external file does not load it; an SVG with a script does not run it; the SVG timeout (use a pathological SVG or a fake renderer).
- **Cache:** path layout and fan-out, atomic write leaves no temp file, stale temp files are removed at startup, total size calculation.
- **Queue:** idempotent enqueue; three sizes per distinct hash; duplicates share jobs (two identical files yield 3 jobs, not 6); missing and unreadable images skipped; evicted rows not re-enqueued; claim order by priority then size; `Prioritize` reorders; delayed retries are not claimed early; concurrent claim (four workers, 200 jobs) processes each job exactly once; `RecoverInterruptedJobs` after a simulated crash; retry and backoff sequence ends in `failed` after 3 attempts; deterministic failures fail at once; `RetryFailed`; orphaned jobs when no source remains.
- **Worker:** end-to-end from a scanned folder to done thumbnails; pause and resume; graceful stop finishes the current job; restart resumes the remaining work (stop part-way, build a new worker, assert only the remainder is processed and nothing is redone); status counts and ETA are sensible; one failing file does not stop the others.
- **Moves and duplicates:** a moved file (Phase 2 move) keeps its thumbnail with no new job; a copy shares the thumbnail; an edited file with a changed hash gets new jobs and the old hash is orphaned.
- **Provider:** `Ready`, `Pending`, `Failed` and `NotFound` results; requesting a missing thumbnail enqueues it with raised priority; a deleted cache file self-heals; ETag stable for the same hash and size; `PickFor` table.
- **Access tracking and eviction:** access updates are batched and flushed; eviction removes least recently used first and stops at 90% of the maximum; evicted rows regenerate on request but not on rescan.
- **Cleanup:** orphan cleanup removes unreferenced hashes and keeps shared ones; folder removal and missing-record deletion trigger it; `ClearCacheAsync` and `VerifyCacheAsync` behave as described.
- **Safety:** hash and modification time of every original file are identical before and after a full scan and thumbnail run.
- **Settings:** the two new keys, defaults, validation.
- **Performance (slow or explicit, not in the default run):** generate thumbnails for 2,000 small images and print throughput with 1 and 4 workers.

## Working rules

- Follow the master brief and the earlier phases' conventions. Where silent, choose the simplest option and note the assumption.
- Core stays free of UI and HTTP concerns. Log queue and worker activity at Information (start, stop, drained), per-job detail at Debug, failures at Warning. Never log file contents.
- Add every new dependency to `THIRD-PARTY-NOTICES.md` after checking its licence.
- If a requirement here conflicts with the brief, follow this prompt and tell me.

## Definition of done

- `dotnet build` and `dotnet test` pass on Linux with no warnings in Core.
- All required tests exist and pass. List any you could not write and why.
- README and `THIRD-PARTY-NOTICES.md` updated, and migration `0003` explained.
- Your final reply includes: new files, assumptions, deviations, decisions you want me to confirm, and the manual checklist below with exact commands.

## How I will test Phase 3

1. Use the Phase 2 test folder (add a transparent PNG icon, a large JPG, an SVG and an animated GIF). Run `scan`, then `thumbs status` and confirm pending jobs (three per distinct hash).
2. `thumbs run --until-empty` and watch the progress. Open the cache folder, view several files, and confirm they are WebP, correctly sized, and that the transparent icon keeps its transparency.
3. Run `thumbs run`, press `Ctrl+C` part-way, then run `thumbs status` and `thumbs run --until-empty` again to confirm it resumes without redoing finished work. Also kill the process hard (`kill -9`) mid-run and repeat.
4. Copy a file, scan, and confirm no extra jobs. Move a file, scan, and confirm its thumbnail is reused.
5. Delete one cached file, run `thumbs verify`, then `thumbs run --until-empty`, and confirm it is regenerated.
6. Set `thumbnailCacheMaxMb` very low in `settings.json`, run `thumbs run --until-empty`, and confirm old entries become `evicted`.
7. Corrupt a source image, scan, run thumbnails, and confirm it ends as `failed` with an error while the rest finish. Fix the file and `thumbs retry-failed`.
8. `folder remove <id>`, then `thumbs cleanup`, and confirm the thumbnails disappear while the originals remain.
9. `thumbs make` on a few files with each size and eyeball the output.
10. Run `dotnet test`, then the slow performance test.
