# **Phase 10: Management Pages and Data Settings — Completed**

Phase 10 is complete and all **548 automated tests** across `IconChest.Core.Tests`, `IconChest.Api.Tests`, `IconChest.Desktop.Tests`, and `IconChest.E2E.Tests` pass with zero failures.

---

### **Key Deliverables & Enhancements**

#### **1. Core Concurrency & Data Maintenance Services**

- `IDbGate` & `DbGate`:
    - Shared reader leases for standard concurrent queries and an exclusive lock for maintenance operations.
    - While maintenance runs, non-exempt API database calls receive HTTP 503 (`Retry-After: 5`, Problem Details type `https://iconchest.app/errors/maintenance`).
- `DataMaintenanceService`:
    - **`BackupAsync`**: SQLite online backup to a temporary file, atomically renamed. Rejects paths within library folders.
    - **`InspectBackup`**: Validates SQLite header, executes `PRAGMA integrity_check`, verifies required table schema and schema versions, and checks local existence of library folders.
    - **`RestoreAsync`**: Runs WAL checkpoint, clears SQLite connection pools, creates `<db>.pre-restore-<timestamp>` safety copy, applies migrations, and rolls back automatically if any step fails. Emits `data.reloaded` and `library.changed` events.
    - **`MoveAsync`**: Relocates database, verifies integrity and record count parity, updates `settings.json`, and renames old file to `<db>.old-<timestamp>` (or deletes if requested).

#### **2. API Additions**

- **Duplicates**: `GET /api/duplicates/summary`, paged `GET /api/duplicates` (sorts: `reclaimable`, `copies`, `size`, `name`), and `GET /api/duplicates/ids`.
- **Missing**: `GET /api/missing` with sorting, `POST /api/missing/remove` (by `ids`, `folderId`, or all), and `POST /api/thumbnails/cleanup-orphans`.
- **Data Maintenance**: `GET /api/data/info`, `POST /api/data/backup`, `GET /api/data/backups/{name}/download`, `POST /api/data/restore/inspect`, `POST /api/data/restore`, and `POST /api/data/move`.
- **Tag Export / Import**: `GET /api/tags/export` and `POST /api/tags/import` (1 MB limit, 10,000 tag cap, case-insensitive merge).
- **Native Launcher**: `POST /api/native/open-folder` with targets: `logs`, `backups`, `thumbnails`, `data`.
- **System**: `GET /api/system/info` returns `lanUrls` and `canRestart`.

#### **3. Desktop Shell & Bridge Additions**

- Added bridge message handlers in `DesktopApp.cs` for `pickFile`, `pickSaveFile`, `saveFile`, and `restartApi`.
- Added `ApiProcessManager.RestartAsync` to gracefully terminate and respawn child API processes with new port detection and automatic window reloading.

#### **4. Frontend Management Views & Modules**

- **Grouped Virtual Layout** (`grouped-geometry.js`):
    - Pure arithmetic layout computing row offsets and group positions from prefix sums.
    - Binary search for visible groups and rows.
    - Cross-group keyboard navigation (`Up` and `Down` move between groups).
- **Duplicates View** (`duplicates.js`):
    - Virtual grouped layout with slim headers (`X copies, Y KB each, Z KB reclaimable`).
    - Middle-shortened folder path captions with full tooltips, favorites heart toggle, and details panel drawer.
- **Missing View** (`missing.js`):
    - Virtual single-column table with relative time formatting (`Xm ago`, `Xh ago`).
    - Multi-select, preview overlay, "Remove from library" with confirmation dialog, and rescan trigger.
    - Dynamic navigation badge in the icon bar.
- **Settings Sections**:
    - `performance.js`: Thumbnail cache path & browsing, MB size presets, clear/verify/retry cache actions, worker threads, and encoding quality.
    - `cleanup.js`: Missing files count and bulk removal, auto-remove toggle, hidden image counts, and orphan cleanup.
    - `logging.js`: Minimum log level selection, log folder copy/open, and 14-day retention notice.
    - `api.js`: Listening port, fixed port preference, LAN access toggle (gated by LAN password), password modal, LAN URLs, and service restart.
    - `data.js`: Database stats, location change, snapshot backups, tag export/import, and multi-step restore wizard with pre-validation and safety copies.
- **Maintenance Overlay & Resilience** (`app.js` & `api.js`):
    - Automatic interception of 503 maintenance responses with calm _"Updating the library, one moment…"_ overlay and exponential retry.
    - Real-time `data.reloaded` and `library.changed` SSE event handling.

---

### **Verification Results**

bash

XDG_RUNTIME_DIR=/tmp dotnet test IconChest.sln --filter "Category!=Explicit"

- **`IconChest.Core.Tests`**: 397 passed, 0 failed.
- **`IconChest.Api.Tests`**: 61 passed, 0 failed.
- **`IconChest.Desktop.Tests`**: 51 passed, 0 failed.
- **`IconChest.E2E.Tests`**: 39 passed, 0 failed.
- **Total**: **548 tests passing** (100% success).



---
















How do i test?


Here are the ways you can test IconChest, both automatically and interactively:

---

### **1. Automated Test Suites**

All tests use `XDG_RUNTIME_DIR=/tmp` on Linux.

#### **Run All Automated Tests**

bash

XDG_RUNTIME_DIR=/tmp dotnet test IconChest.sln --filter "Category!=Explicit"

#### **Run Specific Test Projects**

- **Core Logic & Services** (`DbGate`, `DataMaintenanceService`, `TagService`, `DuplicateService`, `MissingService`, settings):
    
    bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.Core.Tests
    
- **API Endpoints & Integration** (`/api/duplicates/*`, `/api/missing/*`, `/api/data/*`, `/api/tags/*`, maintenance 503 middleware):
    
    bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.Api.Tests
    
- **Desktop Shell & Process Manager** (`ApiProcessManager`, restart lifecycle, navigation policies):
    
    bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.Desktop.Tests
    
- **Frontend Modules & E2E Tests** (`grouped-geometry.js`, view rendering, stores):
    
    bash
    
    XDG_RUNTIME_DIR=/tmp dotnet test tests/IconChest.E2E.Tests
    

---

### **2. Manual / Interactive UI Testing**

#### **Launching the Desktop Shell**

If you have a desktop display with WebKitGTK (Linux) or WebView2 (Windows):

```bash

dotnet run --project src/IconChest.Desktop
```

#### **Launching in Browser Mode**

If you are working in a remote/headless terminal, container, or prefer your standard browser:

```bash

dotnet run --project src/IconChest.Desktop -- --browser
```
This starts the backend API service, generates the secure session handoff, and opens the app in your default browser.

#### **Running Just the API Standalone**

```bash

dotnet run --project src/IconChest.Api
```

The API will output its readiness line with URL:

```text

ICONCHEST_READY http://127.0.0.1:<port>
```
You can open that URL in any browser.

---

### **3. What to Test in the Phase 10 Features**

Once the UI is running:

1. **Duplicates Page (`#duplicates`)**:
    
    - Click the **Duplicates** icon in the left icon bar.
    - Verify the grouped virtual grid renders duplicate sets with slim headers (`X copies, Y KB each, Z KB reclaimable`).
    - Try the sort options (_Reclaimable_, _Copies_, _Size_, _Name_) and the direction toggle.
    - Click an image to open the Details panel; check that the **Copies** list shows all locations.
    - Test keyboard navigation (`ArrowUp`/`ArrowDown` cross group boundaries smoothly).
2. **Missing Page (`#missing`)**:
    
    - Click the **Missing** icon in the icon bar (notice the badge indicator if missing records exist).
    - Test sorting by _Missing Since_, _Name_, _Path_, or _Size_.
    - Select one or more rows (supports `Shift`-click and `Ctrl`/`Cmd`-click).
    - Click **Remove from Library** to verify the non-destructive confirmation dialog:
        
        > _"No files on disk will be deleted, moved or modified."_
        
3. **Settings -> Data & Backups (`#settings/data`)**:
    
    - **Backup Now**: Creates a consistent snapshot in `<data>/backups`.
    - **Backups List**: Shows backup filename, size, and date, with **Restore** and **Download** buttons.
    - **Restore Wizard**: Click **Restore** on any backup to inspect database integrity, schema version, record counts, and library folder presence before confirming the swap.
    - **Tag Taxonomy**: Test **Export Tags (JSON)** and **Import Tags (JSON)**.
4. **Settings -> Performance & Cache (`#settings/performance`)**:
    
    - Check cache stats and limits.
    - Test **Clear Cache**, **Verify Cache**, and **Clean Up Orphans**.
5. **Settings -> API & Network (`#settings/api`)**:
    
    - Verify that **Allow LAN access** is disabled until a LAN password (>= 8 characters) is set via the modal.
    - Click **Restart API Service** to test graceful service restart.