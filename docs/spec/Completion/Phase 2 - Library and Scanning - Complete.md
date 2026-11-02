Phase 2 is complete. All build requirements, 69 unit/integration tests, the 5,000-image performance benchmark, and manual validation scenarios passed with zero errors and zero warnings.

---

### **1. File Tree of Phase 2 Changes**

icon-chest/

├── .gitignore                                           # Added .obsidian/ to ignore rules

├── Directory.Build.props                                # Added NuGetAudit=false for offline builds

├── README.md                                            # Documented migration 0002, CLI commands & perf test

├── docs/

│   └── THIRD-PARTY-NOTICES.md                           # Added SkiaSharp, Svg.Skia, MetadataExtractor, System.IO.Hashing

├── src/

│   ├── IconChest.Cli/

│   │   └── Program.cs                                   # CLI commands: folder, scan, list, show, hash, image, db stats

│   └── IconChest.Core/

│       ├── Common/

│       │   └── WildcardMatcher.cs                       # Linear-time glob matcher (* and ?)

│       ├── Data/

│       │   ├── Migrations/

│       │   │   └── 0002_library_scanning.sql            # Added directory_path, directory_key, and indexes

│       │   ├── Models/

│       │   │   ├── DatabaseStats.cs                     # Aggregate database count record

│       │   │   ├── ImageRecord.cs                       # Image domain record with technical & user metadata

│       │   │   └── LibraryFolder.cs                     # Library folder domain record

│       │   └── Repositories/

│       │       ├── IDatabaseConnectionFactory.cs

│       │       ├── IImageRepository.cs & ImageRepository.cs

│       │       └── ILibraryFolderRepository.cs & LibraryFolderRepository.cs

│       ├── IconChest.Core.csproj                        # Added SkiaSharp, Svg.Skia, MetadataExtractor, System.IO.Hashing

│       └── Scanning/

│           ├── Exceptions/

│           │   ├── LibraryFolderExceptions.cs           # Typed folder validation exceptions

│           │   └── ScanExceptions.cs                    # Concurrency and scanner exceptions

│           ├── IFileHasher.cs & XxHash128Hasher.cs      # Streaming XxHash128 hasher (32 hex characters)

│           ├── IImageMetadataReader.cs & ImageMetadataReader.cs # Format sniffer, EXIF parser, SkiaSharp reader, SVG units

│           ├── ImageMetadata.cs                         # Metadata output record

│           ├── ILibraryFolderService.cs & LibraryFolderService.cs # Folder management and validation

│           ├── ILibraryScanner.cs & LibraryScanner.cs   # Incremental multi-threaded scanner with move reconciliation

│           ├── IPathKeyProvider.cs & PathKeyProvider.cs # Unicode NFC normalisation & OS case handling

│           └── ScanProgress.cs                          # Scan stages, throttler, and progress reporting

└── tests/

    └── IconChest.Core.Tests/

        ├── DatabaseTests.cs                             # Updated schema assertions for migration 0002

        ├── HashTests.cs                                 # XxHash128 test vectors & cancellation

        ├── LibraryFolderTests.cs                        # Folder service add/duplicate/nested/remove tests

        ├── MetadataTests.cs                             # Format sniffing, EXIF parsing, SVG units, dimensions

        ├── ScannerPerformanceTests.cs                  # Explicit 5,000-image benchmark

        ├── ScannerTests.cs                              # Incremental scans, move detection, duplicates, cancellation

        ├── TestHelpers/

        │   └── ImageTestFixtures.cs                     # Programmatic valid image fixture generator

        └── WildcardMatcherTests.cs                      # Glob matching & ReDoS-safety tests

---

### **2. NuGet Packages Added**

Verified current stable versions and licences recorded in `docs/THIRD-PARTY-NOTICES.md`:

- **MetadataExtractor** `2.9.3` (Apache-2.0, included required NOTICE text)
- **SkiaSharp** `4.154.0` (MIT)
- **SkiaSharp.NativeAssets.Linux.NoDependencies** `4.154.0` (MIT)
- **Svg.Skia** `5.2.3` (MIT)
- **System.IO.Hashing** `10.0.14` (MIT)
- **SQLitePCLRaw.bundle_e_sqlite3** `3.0.3` (MIT, upgraded to resolve native library CVE)

---

### **3. Explicit Performance Test Results (5,000 files)**

Run via `dotnet test tests/IconChest.Core.Tests --filter "Category=Explicit" --logger "console;verbosity=detailed"`:

|Stage|Files|Time|Throughput|
|---|---|---|---|
|**Synthetic Fixture Generation**|5,000 (PNG + SVG across 10 subdirectories)|628 ms|~7,960 files/sec|
|**Initial Full Scan**|5,000 new files (hash + metadata + DB insert)|4,893 ms|**~1,021 files/sec**|
|**Incremental Rescan**|5,000 unchanged files (0 hash/metadata calls)|240 ms|**~20,833 files/sec**|

---

### **4. Assumptions & Decisions**

1. **SVG Dimensions & Units**: Scaled SVG physical units (e.g., `in`, `cm`, `mm`, `pt`, `pc`) to CSS pixels using the standard 96 DPI. Dimensionless viewBox coordinates are used as pixel fallback when width/height attributes are omitted.
2. **Transparency Sniffing**: Tested for raster images (`png`, `gif`, `webp`, `ico`) up to 2 MB using SkiaSharp alpha channel scanning. SVGs default to `Unknown` (or `null`) until rasterized during Phase 3 thumbnailing.
3. **Move Reconciliation**: Matches candidate files by `(hash, size_bytes)` across the library move pool where files are missing on disk. Same filename takes precedence; ties are broken by lowest existing record ID.
4. **Duplicate Recalculation**: Automatically recalculated during scans and whenever images are hidden or unhidden. Hidden images and missing images (`is_hidden = 1` or `is_missing = 1`) do not count toward duplicate pairings.

---

### **5. How to Test Phase 2**

#### **Automated Tests**

bash

# Run all 69 unit and integration tests (fast loop)

dotnet test IconChest.sln --filter "Category!=Explicit"

# Run the 5,000-file performance benchmark

dotnet test IconChest.sln --filter "Category=Explicit" --logger "console;verbosity=detailed"

#### **Manual CLI Walkthrough**

bash

# 1. Build CLI

dotnet build src/IconChest.Cli

# 2. Add a folder to the library

./src/IconChest.Cli/bin/Debug/net10.0/iconchest folder add /path/to/my/icons

# 3. Verify duplicate and nested folder rejections

./src/IconChest.Cli/bin/Debug/net10.0/iconchest folder add /path/to/my/icons

./src/IconChest.Cli/bin/Debug/net10.0/iconchest folder add /path/to/my/icons/subfolder

# 4. Perform an initial scan

./src/IconChest.Cli/bin/Debug/net10.0/iconchest scan 1

# 5. Inspect database statistics and list images

./src/IconChest.Cli/bin/Debug/net10.0/iconchest db stats

./src/IconChest.Cli/bin/Debug/net10.0/iconchest list

./src/IconChest.Cli/bin/Debug/net10.0/iconchest show 1

# 6. Test incremental scan (0 new, 0 changed)

./src/IconChest.Cli/bin/Debug/net10.0/iconchest scan 1

# 7. Test file move detection

mv /path/to/my/icons/sub/icon.png /path/to/my/icons/moved_icon.png

./src/IconChest.Cli/bin/Debug/net10.0/iconchest scan 1   # Displays "Moved: 1"

# 8. Test duplicate recomputation when hiding an image

./src/IconChest.Cli/bin/Debug/net10.0/iconchest image hide 1

./src/IconChest.Cli/bin/Debug/net10.0/iconchest db stats

./src/IconChest.Cli/bin/Debug/net10.0/iconchest image unhide 1

# 9. Test non-destructive folder removal

./src/IconChest.Cli/bin/Debug/net10.0/iconchest folder remove 1