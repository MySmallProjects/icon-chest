## **Phase 1: Foundation Completed**

The foundation for IconChest has been built, tested, and verified on .NET 10. All projects build with zero warnings under `<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`, and all 32 unit and integration tests pass.

---

### **File Tree**

IconChest/

├── .editorconfig

├── .gitignore

├── Directory.Build.props

├── IconChest.sln

├── README.md

├── docs/

│   └── THIRD-PARTY-NOTICES.md

├── src/

│   ├── IconChest.Api/

│   │   ├── IconChest.Api.csproj

│   │   └── Program.cs

│   ├── IconChest.Cli/

│   │   ├── IconChest.Cli.csproj

│   │   └── Program.cs

│   ├── IconChest.Core/

│   │   ├── Common/

│   │   │   └── IClock.cs

│   │   ├── Data/

│   │   │   ├── DatabaseConnectionFactory.cs

│   │   │   ├── Exceptions/

│   │   │   │   └── SchemaVersionException.cs

│   │   │   ├── IconChestDatabase.cs

│   │   │   ├── IIconChestDatabase.cs

│   │   │   └── Migrations/

│   │   │       ├── 0001_initial.sql

│   │   │       └── MigrationRunner.cs

│   │   ├── IconChest.Core.csproj

│   │   ├── Logging/

│   │   │   ├── AppLogging.cs

│   │   │   ├── AppLogLevel.cs

│   │   │   └── ILogManager.cs

│   │   ├── Settings/

│   │   │   ├── AppPaths.cs

│   │   │   ├── AppSettings.cs

│   │   │   ├── IAppPaths.cs

│   │   │   ├── ISettingsService.cs

│   │   │   └── SettingsService.cs

│   │   └── Versioning/

│   │       ├── IVersionInfo.cs

│   │       └── VersionInfo.cs

│   └── IconChest.Desktop/

│       ├── IconChest.Desktop.csproj

│       └── Program.cs

└── tests/

    ├── IconChest.Api.Tests/

    │   ├── HealthEndpointTests.cs

    │   └── IconChest.Api.Tests.csproj

    └── IconChest.Core.Tests/

        ├── DatabaseTests.cs

        ├── IconChest.Core.Tests.csproj

        ├── LoggingTests.cs

        ├── PathsTests.cs

        ├── SettingsTests.cs

        └── VersioningTests.cs

---

### **Assumptions Made**

1. **Effective Date Generation (`effective_date_utc`):** Implemented as a SQLite `STORED` generated column:
    
    sql
    
    effective_date_utc TEXT GENERATED ALWAYS AS (
    
        COALESCE(taken_override_utc, exif_date_utc, created_utc, modified_utc)
    
    ) STORED
    
    A `STORED` generated column calculates values on write and allows creating a direct B-tree index (`CREATE INDEX idx_image_effective_date ON image(effective_date_utc)`), avoiding query-time `COALESCE` overhead during filtering and grouping.
2. **Nullable File Timestamps in Image Table:** While files on disk typically have created and modified timestamps, `created_utc` and `modified_utc` were marked nullable in the schema so that in rare instances where files lack filesystem timestamps (or are inserted without them), all four date candidates can be null, matching Section 7's requirement that the Years view can display an "Unknown" date bucket.
3. **Incremental Versioning Mechanism:** To ensure MSBuild triggers assembly recompilation when only time passes between builds, an intermediate version stamp file (`$(IntermediateOutputPath)version.stamp`) is written before `BeforeCompile` and added to `CustomAdditionalCompileInputs`. This causes MSBuild to detect input changes between subsequent runs and update the assembly version and adjacent `version.json`.
4. **CI Workflow:** As permitted in Phase 1 scope ("Skip it if you think it is premature"), GitHub Actions workflow creation was deferred to Phase 11 polish.

---

### **Deviations from the Brief**

- **SQLite Dependency Upgrade for Vulnerability Mitigation:** When restoring dependencies under .NET 10, NuGet flagged a known upstream vulnerability in `SQLitePCLRaw.lib.e_sqlite3` (`<= 2.1.11`, [GHSA-2m69-gcr7-jv3q](https://github.com/advisories/GHSA-2m69-gcr7-jv3q)). In accordance with warnings-as-errors policy, an explicit reference to `SQLitePCLRaw.bundle_e_sqlite3` (version `3.0.3`, Apache-2.0) was added to ensure non-vulnerable native SQLite binaries are resolved.
- **CLI Executable Naming:** Set `<AssemblyName>iconchest</AssemblyName>` on `IconChest.Cli` so that the compiled binary is named `iconchest` (or `iconchest.exe` on Windows), directly matching the test commands specified in the project brief.

---

### **How to Test Phase 1 (Verification Checklist)**

Execute the following commands from the repository root (`/home/user/git/icon-chest`):

bash

# 1. Build and verify version change between builds

dotnet build IconChest.sln

./src/IconChest.Cli/bin/Debug/net10.0/iconchest version

sleep 3

dotnet build IconChest.sln

./src/IconChest.Cli/bin/Debug/net10.0/iconchest version

cat ./src/IconChest.Cli/bin/Debug/net10.0/version.json

# 2. Run all unit and integration tests (32 tests)

dotnet test IconChest.sln

# 3. Test dev CLI logging and levels

./src/IconChest.Cli/bin/Debug/net10.0/iconchest log-test --level Information

./src/IconChest.Cli/bin/Debug/net10.0/iconchest log-test --level Off

# 4. Test database initialization and migration

ICONCHEST_CONFIG_DIR=/tmp/test-config ICONCHEST_DATA_DIR=/tmp/test-data \

  ./src/IconChest.Cli/bin/Debug/net10.0/iconchest db init

ICONCHEST_CONFIG_DIR=/tmp/test-config ICONCHEST_DATA_DIR=/tmp/test-data \

  ./src/IconChest.Cli/bin/Debug/net10.0/iconchest db info

# 5. Test settings show, set, and corrupt recovery

ICONCHEST_CONFIG_DIR=/tmp/test-config ICONCHEST_DATA_DIR=/tmp/test-data \

  ./src/IconChest.Cli/bin/Debug/net10.0/iconchest settings show

ICONCHEST_CONFIG_DIR=/tmp/test-config ICONCHEST_DATA_DIR=/tmp/test-data \

  ./src/IconChest.Cli/bin/Debug/net10.0/iconchest settings set theme Dark

ICONCHEST_CONFIG_DIR=/tmp/test-config ICONCHEST_DATA_DIR=/tmp/test-data \

  ./src/IconChest.Cli/bin/Debug/net10.0/iconchest settings get theme

# Corrupt settings test

echo "{ corrupted content..." > /tmp/test-config/settings.json

ICONCHEST_CONFIG_DIR=/tmp/test-config ICONCHEST_DATA_DIR=/tmp/test-data \

  ./src/IconChest.Cli/bin/Debug/net10.0/iconchest settings show

ls -la /tmp/test-config/ # Verify settings.json.corrupt is created and settings.json is reset to defaults

# Cleanup temporary test directories

rm -rf /tmp/test-config /tmp/test-data