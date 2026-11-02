# **IconChest Phase 4: The API — Implementation Complete**

Phase 4 of IconChest has been built, tested, and verified according to the Master Project Brief and Phase 4 specifications.

---

## **1. Summary of Deliverables**

### **A. HTTP Hosting & Child-Process Contract**

- **Process Contract:**
    - Added command-line arguments: `--port N` (0 = ephemeral port, or default from `settings.json`), `--parent-pid N`, and `--swagger`.
    - Configured stdout readiness line: `ICONCHEST_READY http://127.0.0.1:<port>`.
    - Built `ParentProcessMonitorService`: monitors the parent process PID every 2 seconds and triggers graceful shutdown if the parent desktop shell exits.
    - Added `POST /api/system/shutdown` for programmatic exit.
- **Static Asset Hosting & SPA Fallback:**
    - Configured Kestrel with `UseStaticFiles` and SPA fallback routing to `wwwroot/index.html`.
    - `index.html` is served with `Cache-Control: no-cache`.
    - Fingerprinted static assets are served with long-term immutable caching (`max-age=31536000, immutable`).
    - Dotfiles (`/.env`, `/.git`, etc.) are explicitly rejected with `404 Not Found`.

### **B. Security & Session Authentication**

- **Loopback Secret Auth:**
    - Loops back to `ICONCHEST_API_SECRET` environment variable or generates a 256-bit cryptographically secure URL-safe base64 secret printed once to stdout at startup.
    - Loopback authentication allows secret login only from loopback IP addresses (`127.0.0.1`, `::1`).
- **LAN Password Auth:**
    - Supports LAN access when `api.allowLan = true` with a PBKDF2-SHA256 password hash (600,000 iterations, 16-byte cryptographically secure salt).
    - Enforces loopback-only binding if `api.allowLan = true` but no LAN password hash has been configured.
- **Session Lifecycle:**
    - Uses `SessionManager` issuing sliding 24-hour sessions with token cookies (`iconchest_session`, `HttpOnly`, `SameSite=Strict`, `Path=/`).
    - `DELETE /api/session` terminates the session and expires the cookie.
- **Rate Limiting & Throttling:**
    - Built `LoginThrottler`: 5 failed attempts per IP within a 1-minute window triggers `429 Too Many Requests` with exponential `Retry-After` headers (60s, 120s, 300s). Successful login immediately resets the counter.
- **Network & Header Defenses:**
    - **DNS-Rebinding Protection:** `SecurityMiddleware` validates the `Host` header against loopback and configured LAN addresses, rejecting foreign hosts with `400 Bad Request`.
    - **CSRF / Origin Check:** Non-GET requests with foreign `Origin` headers are rejected with `403 Forbidden`.
    - **Content Security Policy:** Global CSP defaults to `default-src 'self'`.
    - **SVG Sandbox CSP:** When streaming SVG images via `GET /api/images/{id}/file`, a strict sandbox CSP (`default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox`) is applied to isolate SVG execution.
    - **Secrets Invariant:** Session secrets, passwords, cookies, and login request payloads are strictly barred from log files and diagnostic output.

### **C. OpenAPI Documentation**

- Configured Swagger via `Microsoft.AspNetCore.OpenApi` and `Swashbuckle.AspNetCore`.
- Serves Swagger UI at `/swagger` on the same origin.
- Included cookie authentication definition (`iconchest_session`) in Swagger definition.

### **D. SQLite Migration `0004_api_indexes.sql` & Core Services**

- **Migration `0004_api_indexes.sql`:**
    - Added covering index `idx_image_folder_dir` on `image(is_hidden, is_missing, folder_id, directory_key, directory_path)`.
    - Added covering index `idx_image_effective_tree` on `image(is_hidden, is_missing, effective_date_utc)`.
    - Added expression index `idx_image_year_month` on `image(is_hidden, is_missing, SUBSTR(effective_date_utc, 1, 4), SUBSTR(effective_date_utc, 6, 2))`.
    - Added reverse lookup index `idx_image_tag_reverse` on `image_tag(tag_id, image_id)`.
- **Core Services Implemented:**
    - `TagService`: Tag CRUD, hex color validation, case-insensitive uniqueness, image tag unlinking on tag deletion.
    - `ImageSearchService`: Structured image query engine supporting date ranges, aspect filters, tags (`all`/`any`), sorting, and fast ID list projections.
    - `ImageEditService`: Bulk metadata edits with presence-vs-null semantics.
    - `TreeService`: Hierarchical folder tree and date tree (year/month) aggregations.
    - `DuplicateService` & `MissingService`: Grouped duplicate listings and missing file reconciliation.
    - `ScanJobManager`: In-memory scan coordinator tracking active/recent scans with cancellation tokens.
    - `LibraryEventEmitter`: Channel-based Server-Sent Events (SSE) producer for real-time scans, thumbnail progress, and library updates.

### **E. Endpoints Implemented**

- **System:** `GET /health`, `GET /api/system/info`, `POST /api/system/shutdown`, `GET/PATCH /api/settings`, `POST/DELETE /api/settings/lan-password`
- **Session:** `POST /api/session`, `GET /api/session`, `DELETE /api/session`
- **Folders:** `GET/POST /api/folders`, `PATCH/DELETE /api/folders/{id}`
- **Scans:** `POST /api/folders/{id}/scan`, `GET /api/scans`, `GET/DELETE /api/scans/{id}`
- **Events:** `GET /api/events` (SSE streaming with 15-second keep-alive comments)
- **Trees:** `GET /api/tree/folders`, `GET /api/tree/years`
- **Images:**
    - `GET /api/images` (structured search, returns 501 for query string `q`)
    - `GET /api/images/ids` (lean array of IDs matching filters)
    - `GET /api/images/{id}` (full metadata with tag objects)
    - `PATCH /api/images` (bulk edits)
    - `GET /api/images/{id}/thumbnail?size=64|128|256` (supports ETag / 304 Not Modified, returns 202 Accepted + `X-Thumbnail-State: pending` when enqueued)
    - `GET /api/images/{id}/file` (original file streaming with HTTP Range requests, ETag, and SVG sandbox CSP)
- **Tags:** `GET/POST /api/tags`, `GET/PATCH/DELETE /api/tags/{id}`
- **Duplicates & Missing:** `GET /api/duplicates`, `GET /api/missing`
- **Thumbnails:** `GET /api/thumbnails/status`, `POST /api/thumbnails/verify`, `POST /api/thumbnails/retry-failed`, `POST /api/thumbnails/cleanup`, `POST /api/thumbnails/clear`

### **F. Dev CLI Additions (`IconChest.Cli`)**

- `iconchest api login [--url <url>] [--secret <secret>] [--password <pass>]` (persists session cookie with `0600` file permissions)
- `iconchest api get|post|patch|put|delete <path> [--body <json|@file>]`
- `iconchest api events` (connects to SSE stream and displays live server events)

---

## **2. Test Verification & Benchmarks**

### **50,000-Image Benchmark Results**

The benchmark in `ImageQueryPerformanceTests.cs` generated 50,000 realistic image records with tags and directory hierarchies on SQLite:

- `GET /api/tree/folders`: **59 ms** (target: < 100 ms)
- `GET /api/tree/years`: **60 ms** (target: < 100 ms)
- `GET /api/images/ids`: **119 ms** (returning all 50,000 IDs)
- Tag filter + date sort pagination (10 pages): **179 ms** (~18 ms/page, target: < 100 ms/page)
- `EXPLAIN QUERY PLAN`: Verified covering index usage with `SCAN/SEARCH ... USING COVERING INDEX`.

### **Full Test Suite**

- **`IconChest.Core.Tests`**: 171 passed, 0 failed.
- **`IconChest.Api.Tests`**: 32 passed, 0 failed.
- **Build Status**: 0 warnings, 0 errors across entire solution (`TreatWarningsAsErrors=true`).

---

## **3. How to Test Phase 4**

### **1. Run All Tests**

bash

XDG_RUNTIME_DIR=/tmp dotnet test IconChest.sln

### **2. Start the API Server**

In one terminal:

bash

ICONCHEST_API_SECRET=mytestsecret dotnet run --project src/IconChest.Api -- --port 5000 --swagger

Verify stdout emits:

text

ICONCHEST_READY http://127.0.0.1:5000

### **3. Open Swagger UI**

Visit `http://127.0.0.1:5000/swagger` in your browser.

### **4. Exercise via CLI**

In another terminal:

bash

# 1. Login with session secret

dotnet run --project src/IconChest.Cli -- api login --url http://127.0.0.1:5000 --secret mytestsecret

# 2. Query system info

dotnet run --project src/IconChest.Cli -- api get /api/system/info --url http://127.0.0.1:5000

# 3. Create a tag

dotnet run --project src/IconChest.Cli -- api post /api/tags --body '{"name":"nature","fgColor":"#FFFFFF","bgColor":"#228B22"}' --url http://127.0.0.1:5000

# 4. Stream events in real-time

dotnet run --project src/IconChest.Cli -- api events --url http://127.0.0.1:5000