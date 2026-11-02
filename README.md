# IconChest

IconChest is a cross-platform (Linux and Windows) desktop application for organizing a library of images, primarily icons up to 512x512 with transparency, plus photos.

IconChest strictly adheres to a non-destructive principle: **it never modifies, moves, or deletes the user's image files or folders**. All edits and metadata live exclusively in the SQLite database and application settings.

---

## Architecture Overview

1. **IconChest.Core**: Class library containing all domain logic, database migrations, settings management, logging abstractions, and versioning. Free of UI and HTTP dependencies.
2. **IconChest.Api**: ASP.NET Core Web API layer serving endpoints and static frontend assets from the same origin.
3. **IconChest.Desktop**: Desktop host shell using Photino.NET (placeholder in Phase 1; full shell in Phase 6).
4. **IconChest.Cli**: Developer command-line tool for maintenance, migrations, logging tests, and settings inspection.

---

## Prerequisites

* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (`net10.0`)
* On Linux Mint / Ubuntu:
  ```bash
  sudo apt install -y dotnet-sdk-10.0
  ```
  *(Or install via `curl -sSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 10.0`)*

---

## Build Instructions

To build the entire solution:

```bash
dotnet build IconChest.sln
```

### Note on Build Versioning
Version numbers are generated dynamically at build time using the formula:
* `Major.Minor` configured in `Directory.Build.props` (default `0.1`)
* `Build` = days since 2024-01-01 (UTC)
* `Revision` = seconds since UTC midnight divided by 2

Every build of an executable automatically writes an adjacent `version.json` containing the full version string and UTC ISO 8601 build timestamp.

---

## Running Tests

Run all unit and integration tests across Linux and Windows:

```bash
dotnet test IconChest.sln --filter "Category!=Explicit"
```

To run the slow explicit performance benchmark (scanning 5,000 generated image files):
```bash
dotnet test IconChest.sln --filter "Category=Explicit" --logger "console;verbosity=detailed"
```

Tests run against in-memory or isolated temporary databases and temporary directories. They do not touch real user folders and do not depend on system timezone or locale.

---

## Dev CLI Usage (`iconchest`)

The CLI binary is output to `src/IconChest.Cli/bin/Debug/net10.0/iconchest`.

### 1. Library Folder Operations
Add a folder to the library (validates readable, exists, and rejects duplicates or nested parents/children):
```bash
iconchest folder add /path/to/folder
```

List all configured library folders:
```bash
iconchest folder list
```

Enable or disable scanning for a library folder:
```bash
iconchest folder enable <id>
iconchest folder disable <id>
```

Remove a folder from the library (prompts confirmation, cascade deletes database records, never touches files on disk):
```bash
iconchest folder remove <id>
```

### 2. Scanning
Scan a folder by ID or path:
```bash
iconchest scan <id|path>
```

Scan all enabled library folders:
```bash
iconchest scan --all
```

### 3. Image Operations
List indexed images with paging and status flags:
```bash
iconchest list [--folder <id>] [--all] [--missing] [--duplicates] [--limit 50] [--offset 0]
```

Display detailed metadata for a single image:
```bash
iconchest show <id>
```

Hide or unhide an image (hiding automatically recomputes duplicate flags for surviving images):
```bash
iconchest image hide <id>
iconchest image unhide <id>
```

Compute the standalone XxHash128 hash of any file:
```bash
iconchest hash /path/to/file.png
```

### 4. Thumbnail Operations
Inspect the state of the thumbnail queue and disk cache:
```bash
iconchest thumbs status
```

Enqueue images for thumbnail generation:
```bash
iconchest thumbs enqueue [--folder <id>]
```

Run thumbnail workers interactively:
```bash
iconchest thumbs run [--until-empty]
```

Reset failed thumbnail jobs back to pending:
```bash
iconchest thumbs retry-failed
```

Render an on-demand single thumbnail directly to an output file:
```bash
iconchest thumbs make /path/to/image.png --size 128 --out /tmp/thumb.webp
```

Display thumbnail cache status and paths for an image:
```bash
iconchest thumbs show <imageId>
```

Audit and verify the thumbnail cache against database records:
```bash
iconchest thumbs verify
```

Purge orphaned thumbnails and clean up disk files:
```bash
iconchest thumbs cleanup
```

Clear all cached thumbnail files and reset the queue:
```bash
iconchest thumbs clear
```

### 5. Database Operations
Initialize or migrate the database (migrations 0001, 0002, and 0003):
```bash
iconchest db init
```

Inspect current schema version:
```bash
iconchest db info
```

View database entity counts and statistics:
```bash
iconchest db stats
```

### 6. Settings Operations
Show full configuration:
```bash
iconchest settings show
```
Read a specific setting:
```bash
iconchest settings get theme
```
Update a setting (e.g. `thumbnailQuality`, `thumbnailWorkers`, `thumbnailCacheMaxMb`):
```bash
iconchest settings set thumbnailQuality 90
iconchest settings set thumbnailWorkers 4
```

### 7. API CLI Client Operations
Authenticate with a running IconChest API and save session cookie to `~/.iconchest_cli_session`:
```bash
iconchest api login [--secret <secret>] [--password <pass>] [--url http://127.0.0.1:<port>]
```

Execute authenticated API requests against the running instance:
```bash
iconchest api get /api/system/info
iconchest api get /api/folders
iconchest api post /api/folders '{"path":"/path/to/icons"}'
iconchest api patch /api/folders/1 '{"enabled":false}'
iconchest api delete /api/folders/1
iconchest api get /api/images
iconchest api get /api/tree/folders
iconchest api get /api/tree/years
```

Listen to live Server-Sent Events (SSE) from `/api/events`:
```bash
iconchest api events
```

### 8. Logging and Diagnostics
Print version info:
```bash
iconchest version
```

Test logging output across levels:
```bash
iconchest log-test --level Information
```

---

## IconChest API & Child-Process Contract

The API is built to be run either standalone or as a child process of the desktop shell (Photino.NET in Phase 6).

### Configuration & Arguments
* `--port N`: Listen port. Default is `0` (or `api.portPreference` from `settings.json`), binding to an ephemeral free port.
* `--parent-pid N`: Process ID of parent desktop shell. The API monitors this process every 2 seconds and initiates graceful shutdown if the parent process exits.
* `--swagger`: Forces Swagger UI to be enabled even in Production mode.
* `ICONCHEST_API_SECRET`: Optional environment variable providing the session secret. If not set, a cryptographically secure 256-bit random URL-safe base64 string is generated on startup and printed once to stdout.

### Readiness Protocol
Upon successfully binding to Kestrel, the API emits a single line to standard output:
```text
ICONCHEST_READY http://127.0.0.1:<port>
```
The desktop host monitors stdout for this line before loading the webview.

### Security Invariants
* **DNS Rebinding Protection:** The API validates the `Host` header against loopback (`127.0.0.1`, `localhost`, `[::1]`) and the host machine's LAN IP addresses if LAN binding is active. Foreign host headers are rejected with `400 Bad Request`.
* **Cross-Site Request Forgery (CSRF) & Origin Defense:** Non-GET requests with foreign `Origin` headers are rejected with `403 Forbidden`.
* **Content Security Policy:** Strict CSP applied (`default-src 'self'`). Original SVG files served at `/api/images/{id}/file` include a strict sandbox CSP (`default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox`) preventing script execution.
* **Authentication & Throttling:**
  * Secret authentication via loopback only.
  * LAN authentication requires `api.allowLan = true` and a password hash (PBKDF2-SHA256 >= 600,000 iterations).
  * Sliding 24-hour sessions via `iconchest_session` cookie (`SameSite=Strict`, `HttpOnly`).
  * Throttling: 5 failed login attempts per IP per minute triggers `429 Too Many Requests` with exponential `Retry-After` headers (60s, 120s, 300s). Successful login resets the counter.
* **Secrets Invariant:** Session secrets, passwords, cookies, and authentication payloads are strictly excluded from logging.

---

### Environment Variable Overrides
To isolate configurations and data paths during development or testing, use:
* `ICONCHEST_CONFIG_DIR`: directory where `settings.json` resides.
* `ICONCHEST_DATA_DIR`: directory where `iconchest.db`, `logs/`, and `thumbnails/` reside.

Example:
```bash
ICONCHEST_CONFIG_DIR=/tmp/test-config ICONCHEST_DATA_DIR=/tmp/test-data ./src/IconChest.Cli/bin/Debug/net10.0/iconchest db init
```

---

## Security & Privacy: No Logging of Secrets

A core architectural invariant of IconChest:
> **Never log or print secrets, passwords, cookies, or session secrets.**

All logging configurations and sinks are strictly forbidden from writing authentication credentials, session cookies, or raw authorization payload contents to log files or standard output.

---

## Third-Party Licenses

All dependencies use verified permissive open-source licenses (MIT and Apache 2.0). See [docs/THIRD-PARTY-NOTICES.md](docs/THIRD-PARTY-NOTICES.md) for complete details.

