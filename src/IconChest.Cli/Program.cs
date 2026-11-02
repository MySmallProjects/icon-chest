using System.Globalization;
using System.Text.Json;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Logging;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using IconChest.Core.Versioning;
using Microsoft.Extensions.Logging;

namespace IconChest.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            PrintUsage();
            return 0;
        }

        var command = args[0].ToLowerInvariant();
        var cmdArgs = args.Skip(1).ToArray();

        try
        {
            return command switch
            {
                "version" => HandleVersion(),
                "log-test" => HandleLogTest(cmdArgs),
                "settings" => HandleSettings(cmdArgs),
                "db" => HandleDb(cmdArgs),
                "folder" => HandleFolder(cmdArgs),
                "scan" => await HandleScanAsync(cmdArgs),
                "list" => HandleList(cmdArgs),
                "show" => HandleShow(cmdArgs),
                "hash" => await HandleHashAsync(cmdArgs),
                "image" => HandleImage(cmdArgs),
                "thumbs" => await HandleThumbsAsync(cmdArgs),
                "api" => await ApiCliCommands.HandleAsync(cmdArgs),
                _ => UnknownCommand(command)
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Error.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("IconChest Dev CLI");
        Console.WriteLine();
        Console.WriteLine("Usage: iconchest <command> [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  version                              Print version, build date, and executable path");
        Console.WriteLine("  log-test [--level <level>]           Write messages at each level and report filtering");
        Console.WriteLine("  settings show|get|set                Manage application settings");
        Console.WriteLine("  db init|info|stats                   Manage SQLite database and view statistics");
        Console.WriteLine("  folder add <path>                    Add a directory to the library");
        Console.WriteLine("  folder list                          List library folders and file counts");
        Console.WriteLine("  folder remove <id or path> [--yes]   Remove a folder from the library");
        Console.WriteLine("  scan <id or path> | scan --all       Scan library folder(s) with live progress");
        Console.WriteLine("  list [--folder id] [--hidden]...     List indexed images in a table");
        Console.WriteLine("  show <image id>                      Display all metadata and EXIF for an image");
        Console.WriteLine("  hash <file>                          Compute XxHash128 for any file");
        Console.WriteLine("  image hide|unhide <id>               Toggle hidden state of an image");
        Console.WriteLine("  thumbs status                        Show thumbnail queue status and cache size");
        Console.WriteLine("  thumbs enqueue [--folder <id>]       Enqueue thumbnail generation jobs");
        Console.WriteLine("  thumbs run [--until-empty]           Run background worker to process queue");
        Console.WriteLine("  thumbs retry-failed                  Reset failed thumbnail jobs to pending");
        Console.WriteLine("  thumbs clear [--yes]                 Clear all thumbnail files and database rows");
        Console.WriteLine("  thumbs verify                        Verify cache and restore missing / delete stale files");
        Console.WriteLine("  thumbs cleanup                       Clean up orphaned thumbnail records and files");
        Console.WriteLine("  thumbs show <image id>               Display thumbnail state and paths for an image");
        Console.WriteLine("  thumbs make <file> --size N --out <path>  Render a single file for visual checking");
        Console.WriteLine("  api <login|get|post|patch|put|delete|events>  Interact with the running API daemon");
    }

    private static int UnknownCommand(string command)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Unknown command: '{command}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
    }

    private static (IconChestDatabase Db,
                    LibraryFolderRepository FolderRepo,
                    ImageRepository ImageRepo,
                    SettingsService Settings,
                    ThumbnailCache Cache,
                    ThumbnailQueue Queue,
                    SkiaThumbnailRenderer Renderer,
                    ThumbnailProvider Provider) CreateContext()
    {
        var paths = new AppPaths();
        var settingsService = new SettingsService(paths);
        var db = new IconChestDatabase(settingsService);
        db.Migrate();

        var connFactory = new DatabaseConnectionFactory(db.DatabasePath);
        var folderRepo = new LibraryFolderRepository(connFactory);
        var imageRepo = new ImageRepository(connFactory);
        var cache = new ThumbnailCache(settingsService);
        var queue = new ThumbnailQueue(connFactory);
        var renderer = new SkiaThumbnailRenderer();
        var provider = new ThumbnailProvider(connFactory, queue, cache, settingsService);

        return (db, folderRepo, imageRepo, settingsService, cache, queue, renderer, provider);
    }

    private static int HandleVersion()
    {
        var info = new VersionInfo();
        Console.WriteLine($"Version:         {info.Version}");
        Console.WriteLine($"Build Date UTC:  {(info.BuildDateUtc.HasValue ? info.BuildDateUtc.Value.ToString("o") : "Unknown")}");
        Console.WriteLine($"Executable Path: {info.ExecutablePath}");
        return 0;
    }

    private static int HandleLogTest(string[] args)
    {
        var level = AppLogLevel.Information;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--level" or "-l" && i + 1 < args.Length)
            {
                if (!Enum.TryParse(args[i + 1], ignoreCase: true, out level))
                {
                    Console.Error.WriteLine($"Invalid log level '{args[i + 1]}'.");
                    return 1;
                }
                i++;
            }
        }

        var paths = new AppPaths();
        using var logManager = new AppLogging(LogComponent.App, paths.DefaultLogDirectory, level);
        var logger = logManager.CreateLogger("IconChest.Cli.LogTest");

        Console.WriteLine($"Log file path: {logManager.CurrentLogFilePath}");
        Console.WriteLine($"Active log level: {level}");
        Console.WriteLine();

        logger.LogTrace("Trace test message");
        logger.LogDebug("Debug test message");
        logger.LogInformation("Information test message");
        logger.LogWarning("Warning test message");
        logger.LogError("Error test message");

        Console.WriteLine("Summary of messages written / filtered:");
        PrintFilterStatus("Trace", AppLogLevel.Trace, level);
        PrintFilterStatus("Debug", AppLogLevel.Debug, level);
        PrintFilterStatus("Information", AppLogLevel.Information, level);
        PrintFilterStatus("Warning", AppLogLevel.Warning, level);
        PrintFilterStatus("Error", AppLogLevel.Error, level);

        if (level == AppLogLevel.Off)
        {
            Console.WriteLine("Log level is Off. No messages were written to the log file.");
        }

        return 0;
    }

    private static void PrintFilterStatus(string levelName, AppLogLevel messageLevel, AppLogLevel activeLevel)
    {
        if (activeLevel == AppLogLevel.Off)
        {
            Console.WriteLine($"  [{levelName,-11}] FILTERED (logging is Off)");
            return;
        }

        var isWritten = messageLevel <= activeLevel;
        var status = isWritten ? "WRITTEN" : "FILTERED";
        Console.WriteLine($"  [{levelName,-11}] {status}");
    }

    private static int HandleSettings(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing settings subcommand: show, get, or set");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var paths = new AppPaths();
        var settingsService = new SettingsService(paths);

        switch (sub)
        {
            case "show":
            {
                var json = JsonSerializer.Serialize(settingsService.Current, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                });
                Console.WriteLine(json);
                return 0;
            }
            case "get":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: iconchest settings get <key>");
                    return 1;
                }
                var key = args[1];
                var value = GetSettingValue(settingsService.Current, key);
                if (value == null)
                {
                    Console.Error.WriteLine($"Setting '{key}' not found or null.");
                    return 1;
                }
                Console.WriteLine(value);
                return 0;
            }
            case "set":
            {
                if (args.Length < 3)
                {
                    Console.Error.WriteLine("Usage: iconchest settings set <key> <value>");
                    return 1;
                }
                var key = args[1];
                var value = args[2];
                settingsService.Update(s => SetSettingValue(s, key, value));
                Console.WriteLine($"Updated '{key}' to '{value}'.");
                return 0;
            }
            default:
                Console.Error.WriteLine($"Unknown settings subcommand: '{sub}'");
                return 1;
        }
    }

    private static string? GetSettingValue(AppSettings settings, string key) => key.ToLowerInvariant() switch
    {
        "version" => settings.Version.ToString(CultureInfo.InvariantCulture),
        "dbpath" => settings.DbPath,
        "thumbnailcachepath" => settings.ThumbnailCachePath,
        "thumbnailcachemaxmb" => settings.ThumbnailCacheMaxMb.ToString(CultureInfo.InvariantCulture),
        "loglevel" => settings.LogLevel.ToString(),
        "theme" => settings.Theme.ToString(),
        "gridcaption" => settings.GridCaption.ToString(),
        "checkerboardmode" => settings.CheckerboardMode.ToString(),
        "previewinfooverlaydefault" => settings.PreviewInfoOverlayDefault.ToString().ToLowerInvariant(),
        "defaultview" => settings.DefaultView,
        "rememberwindowstate" => settings.RememberWindowState.ToString().ToLowerInvariant(),
        "window.width" => settings.Window.Width.ToString(CultureInfo.InvariantCulture),
        "window.height" => settings.Window.Height.ToString(CultureInfo.InvariantCulture),
        "window.maximised" => settings.Window.Maximised.ToString().ToLowerInvariant(),
        "includepatterns" => settings.IncludePatterns,
        "excludenames" => settings.ExcludeNames,
        "scanonstartup" => settings.ScanOnStartup.ToString().ToLowerInvariant(),
        "scanworkers" => settings.ScanWorkers.ToString(CultureInfo.InvariantCulture),
        "autoremovemissing" => settings.AutoRemoveMissing.ToString().ToLowerInvariant(),
        "confirmbeforedelete" => settings.ConfirmBeforeDelete.ToString().ToLowerInvariant(),
        "api.portpreference" => settings.Api.PortPreference.ToString(CultureInfo.InvariantCulture),
        "api.allowlan" => settings.Api.AllowLan.ToString().ToLowerInvariant(),
        "api.lanpasswordhash" => settings.Api.LanPasswordHash,
        _ => settings.ExtensionData.TryGetValue(key, out var elem) ? elem.ToString() : null
    };

    private static void SetSettingValue(AppSettings settings, string key, string value)
    {
        switch (key.ToLowerInvariant())
        {
            case "dbpath":
                settings.DbPath = string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : value;
                break;
            case "thumbnailcachepath":
                settings.ThumbnailCachePath = string.IsNullOrWhiteSpace(value) || value.Equals("null", StringComparison.OrdinalIgnoreCase) ? null : value;
                break;
            case "thumbnailcachemaxmb":
                settings.ThumbnailCacheMaxMb = int.Parse(value, CultureInfo.InvariantCulture);
                break;
            case "loglevel":
                settings.LogLevel = Enum.Parse<AppLogLevel>(value, ignoreCase: true);
                break;
            case "theme":
                settings.Theme = Enum.Parse<AppTheme>(value, ignoreCase: true);
                break;
            case "gridcaption":
                settings.GridCaption = Enum.Parse<GridCaptionMode>(value, ignoreCase: true);
                break;
            case "checkerboardmode":
                settings.CheckerboardMode = Enum.Parse<CheckerboardMode>(value, ignoreCase: true);
                break;
            case "previewinfooverlaydefault":
                settings.PreviewInfoOverlayDefault = bool.Parse(value);
                break;
            case "defaultview":
                settings.DefaultView = value;
                break;
            case "rememberwindowstate":
                settings.RememberWindowState = bool.Parse(value);
                break;
            case "includepatterns":
                settings.IncludePatterns = value;
                break;
            case "excludenames":
                settings.ExcludeNames = value;
                break;
            case "scanonstartup":
                settings.ScanOnStartup = bool.Parse(value);
                break;
            case "scanworkers":
                settings.ScanWorkers = int.Parse(value, CultureInfo.InvariantCulture);
                break;
            case "autoremovemissing":
                settings.AutoRemoveMissing = bool.Parse(value);
                break;
            case "confirmbeforedelete":
                settings.ConfirmBeforeDelete = bool.Parse(value);
                break;
            case "api.portpreference":
                settings.Api.PortPreference = int.Parse(value, CultureInfo.InvariantCulture);
                break;
            case "api.allowlan":
                settings.Api.AllowLan = bool.Parse(value);
                break;
            default:
                throw new ArgumentException($"Unsupported setting key '{key}'");
        }
    }

    private static int HandleDb(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing db subcommand: init, info, or stats");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var (db, _, imageRepo, _, _, _, _, _) = CreateContext();

        switch (sub)
        {
            case "init":
            {
                var version = db.Migrate();
                Console.WriteLine($"Database Path:  {db.DatabasePath}");
                Console.WriteLine($"Schema Version: {version}");
                return 0;
            }
            case "info":
            {
                var version = db.GetSchemaVersion();
                Console.WriteLine($"Database Path:  {db.DatabasePath}");
                Console.WriteLine($"Schema Version: {version}");
                return 0;
            }
            case "stats":
            {
                var stats = imageRepo.GetStats();
                Console.WriteLine("Database Statistics:");
                Console.WriteLine($"  Library Folders: {stats.FoldersCount}");
                Console.WriteLine($"  Total Images:    {stats.ImagesCount}");
                Console.WriteLine($"  Missing:         {stats.MissingCount}");
                Console.WriteLine($"  Hidden:          {stats.HiddenCount}");
                Console.WriteLine($"  Duplicates:      {stats.DuplicatesCount}");
                Console.WriteLine($"  Tags:            {stats.TagsCount}");
                return 0;
            }
            default:
                Console.Error.WriteLine($"Unknown db subcommand: '{sub}'");
                return 1;
        }
    }

    private static int HandleFolder(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing folder subcommand: add, list, or remove");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var (_, folderRepo, _, _, _, _, _, provider) = CreateContext();
        var folderService = new LibraryFolderService(folderRepo, thumbnailProvider: provider);

        switch (sub)
        {
            case "add":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: iconchest folder add <path> [includePatterns]");
                    return 1;
                }
                var path = args[1];
                var includePatterns = args.Length > 2 ? args[2] : null;
                var id = folderService.AddFolder(path, includePatterns);
                Console.WriteLine($"Added folder with ID: {id}");
                return 0;
            }
            case "list":
            {
                var list = folderService.ListFolders();
                if (list.Count == 0)
                {
                    Console.WriteLine("No library folders configured.");
                    return 0;
                }

                Console.WriteLine($"{"ID",-5} {"ENABLED",-8} {"IMAGES",-8} {"LAST SCAN",-22} {"PATH"}");
                Console.WriteLine(new string('-', 80));
                foreach (var f in list)
                {
                    var count = folderService.GetImageCount(f.Id);
                    var lastScan = !string.IsNullOrWhiteSpace(f.LastScanUtc) ? f.LastScanUtc : "Never";
                    Console.WriteLine($"{f.Id,-5} {(f.Enabled ? "Yes" : "No"),-8} {count,-8} {lastScan,-22} {f.Path}");
                }
                return 0;
            }
            case "remove":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: iconchest folder remove <id or path> [--yes]");
                    return 1;
                }

                var target = args[1];
                var assumeYes = args.Any(a => a.Equals("--yes", StringComparison.OrdinalIgnoreCase) || a.Equals("-y", StringComparison.OrdinalIgnoreCase));

                LibraryFolder? folder = null;
                if (long.TryParse(target, out var id))
                {
                    folder = folderService.GetById(id);
                }
                if (folder == null)
                {
                    folder = folderService.GetByPath(target);
                }

                if (folder == null)
                {
                    Console.Error.WriteLine($"Folder '{target}' not found in library.");
                    return 1;
                }

                if (!assumeYes)
                {
                    Console.Write($"Remove library folder '{folder.Path}' (ID: {folder.Id})? Disk files will not be touched. (y/N): ");
                    var response = Console.ReadLine();
                    if (!string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(response?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Removal cancelled.");
                        return 0;
                    }
                }

                folderService.RemoveFolder(folder.Id);
                Console.WriteLine($"Removed folder {folder.Id} from library.");
                return 0;
            }
            default:
                Console.Error.WriteLine($"Unknown folder subcommand: '{sub}'");
                return 1;
        }
    }

    private static async Task<int> HandleScanAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: iconchest scan <id or path> | iconchest scan --all");
            return 1;
        }

        var (_, folderRepo, imageRepo, settingsService, cache, queue, renderer, provider) = CreateContext();
        var folderService = new LibraryFolderService(folderRepo, thumbnailProvider: provider);
        var scanner = new LibraryScanner(
            folderRepo,
            imageRepo,
            settingsService,
            new XxHash128Hasher(),
            new ImageMetadataReader(),
            thumbnailQueue: queue,
            thumbnailProvider: provider);

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, eventArgs) =>
        {
            eventArgs.Cancel = true;
            Console.WriteLine();
            Console.WriteLine("Cancelling scan (finishing current batch)...");
            cts.Cancel();
        };

        var progress = new Progress<ScanProgress>(p =>
        {
            var pathDisplay = !string.IsNullOrWhiteSpace(p.CurrentPath)
                ? (p.CurrentPath.Length > 35 ? "..." + p.CurrentPath[^32..] : p.CurrentPath)
                : "";

            Console.Write($"\r[{p.Stage,-11}] Found: {p.FilesFound} | Proc: {p.FilesProcessed} | New: {p.NewFiles} | Chg: {p.ChangedFiles} | Mv: {p.MovedFiles} | Miss: {p.MissingFiles} | {pathDisplay,-35}");
        });

        if (args[0] is "--all" or "-a")
        {
            Console.WriteLine("Starting scan of all library folders...");
            var results = await scanner.ScanAllAsync(progress, cts.Token);
            Console.WriteLine();
            Console.WriteLine("--- Scan Summary ---");
            foreach (var r in results)
            {
                PrintScanResult(r);
            }
            return results.Any(r => r.IsFatalError) ? 1 : 0;
        }
        else
        {
            var target = args[0];
            LibraryFolder? folder = null;
            if (long.TryParse(target, out var id))
            {
                folder = folderService.GetById(id);
            }
            if (folder == null)
            {
                folder = folderService.GetByPath(target);
            }

            if (folder == null)
            {
                Console.Error.WriteLine($"Folder '{target}' not found in library.");
                return 1;
            }

            Console.WriteLine($"Starting scan of folder {folder.Id}: {folder.Path}");
            var result = await scanner.ScanAsync(folder.Id, progress, cts.Token);
            Console.WriteLine();
            Console.WriteLine("--- Scan Summary ---");
            PrintScanResult(result);
            return result.IsFatalError ? 1 : 0;
        }
    }

    private static void PrintScanResult(ScanResult r)
    {
        Console.WriteLine($"Folder:       {r.FolderPath} (ID: {r.FolderId})");
        Console.WriteLine($"Duration:     {r.Duration.TotalSeconds:F2}s");
        Console.WriteLine($"Status:       {(r.IsCancelled ? "CANCELLED" : r.IsFatalError ? "FATAL ERROR" : r.IsIncomplete ? "INCOMPLETE (Warnings)" : "COMPLETED")}");
        if (!string.IsNullOrWhiteSpace(r.FatalErrorMessage))
        {
            Console.WriteLine($"Error:        {r.FatalErrorMessage}");
        }
        Console.WriteLine($"Files Found:  {r.FilesFound}");
        Console.WriteLine($"  Unchanged:  {r.UnchangedFiles}");
        Console.WriteLine($"  New:        {r.NewFiles}");
        Console.WriteLine($"  Changed:    {r.ChangedFiles}");
        Console.WriteLine($"  Moved:      {r.MovedFiles}");
        Console.WriteLine($"  Missing:    {r.MissingFiles}");
        Console.WriteLine($"  Duplicates: {r.Duplicates}");
        Console.WriteLine($"  Errors:     {r.Errors}");
        Console.WriteLine($"  Thumbnails: {r.ThumbnailsEnqueued} enqueued");

        if (r.WarningsAndErrors.Count > 0)
        {
            Console.WriteLine("Warnings/Errors (first 50):");
            foreach (var w in r.WarningsAndErrors)
            {
                Console.WriteLine($"  - {w}");
            }
        }
        Console.WriteLine();
    }

    private static int HandleList(string[] args)
    {
        long? folderId = null;
        var includeHidden = false;
        var includeMissing = false;
        var duplicatesOnly = false;
        var limit = 50;

        for (var i = 0; i < args.Length; i++)
        {
            if (args[i] is "--folder" or "-f" && i + 1 < args.Length && long.TryParse(args[i + 1], out var fid))
            {
                folderId = fid;
                i++;
            }
            else if (args[i] is "--hidden" or "-h")
            {
                includeHidden = true;
            }
            else if (args[i] is "--missing" or "-m")
            {
                includeMissing = true;
            }
            else if (args[i] is "--duplicates" or "-d")
            {
                duplicatesOnly = true;
            }
            else if (args[i] is "--limit" or "-l" && i + 1 < args.Length && int.TryParse(args[i + 1], out var lim))
            {
                limit = lim;
                i++;
            }
        }

        var (_, _, imageRepo, _, _, _, _, _) = CreateContext();
        var images = imageRepo.List(folderId, includeHidden, includeMissing, duplicatesOnly, limit: limit, offset: 0);
        var total = imageRepo.Count(folderId, includeHidden, includeMissing, duplicatesOnly);

        Console.WriteLine($"Showing {images.Count} of {total} images:");
        Console.WriteLine($"{"ID",-6} {"FORMAT",-7} {"DIMENSIONS",-12} {"SIZE",-10} {"FLAGS",-10} {"HASH (xxh128)",-18} {"PATH"}");
        Console.WriteLine(new string('-', 95));

        foreach (var img in images)
        {
            var dims = img.Width.HasValue && img.Height.HasValue ? $"{img.Width}x{img.Height}" : "-";
            var sizeStr = FormatBytes(img.SizeBytes);
            var flags = (img.IsFavourite ? "F" : "-") +
                        (img.IsHidden ? "H" : "-") +
                        (img.IsMissing ? "M" : "-") +
                        (img.IsDuplicate ? "D" : "-") +
                        (img.HasTransparency == true ? "T" : "-");

            var shortHash = img.Hash.Length >= 16 ? img.Hash[..16] + ".." : img.Hash;
            Console.WriteLine($"{img.Id,-6} {img.Format,-7} {dims,-12} {sizeStr,-10} {flags,-10} {shortHash,-18} {img.Path}");
        }

        return 0;
    }

    private static int HandleShow(string[] args)
    {
        if (args.Length == 0 || !long.TryParse(args[0], out var id))
        {
            Console.Error.WriteLine("Usage: iconchest show <image id>");
            return 1;
        }

        var (_, _, imageRepo, _, _, _, _, _) = CreateContext();
        var img = imageRepo.GetById(id);
        if (img == null)
        {
            Console.Error.WriteLine($"Image with ID {id} not found.");
            return 1;
        }

        Console.WriteLine($"Image Details (ID: {img.Id}):");
        Console.WriteLine($"  Path:               {img.Path}");
        Console.WriteLine($"  Path Key:           {img.PathKey}");
        Console.WriteLine($"  Directory Path:     {img.DirectoryPath}");
        Console.WriteLine($"  File Name:          {img.FileName}");
        Console.WriteLine($"  Format:             {img.Format}");
        Console.WriteLine($"  Dimensions:         {(img.Width.HasValue ? $"{img.Width}x{img.Height}" : "Unknown")}");
        Console.WriteLine($"  Size:               {FormatBytes(img.SizeBytes)} ({img.SizeBytes} bytes)");
        Console.WriteLine($"  Hash ({img.HashAlgo}):  {img.Hash}");
        Console.WriteLine($"  Status:             {img.Status}");
        Console.WriteLine($"  Title:              {img.Title}");
        Console.WriteLine($"  Description:        {img.Description}");
        Console.WriteLine($"  Rating:             {img.Rating} / 5");
        Console.WriteLine($"  Is Favourite:       {img.IsFavourite}");
        Console.WriteLine($"  Is Hidden:          {img.IsHidden}");
        Console.WriteLine($"  Is Missing:         {img.IsMissing} (Since: {img.MissingSinceUtc ?? "N/A"})");
        Console.WriteLine($"  Is Duplicate:       {img.IsDuplicate}");
        Console.WriteLine($"  Has Transparency:   {(img.HasTransparency.HasValue ? img.HasTransparency.Value.ToString() : "Unknown")}");
        Console.WriteLine($"  Created (UTC):      {img.CreatedUtc ?? "N/A"}");
        Console.WriteLine($"  Modified (UTC):     {img.ModifiedUtc ?? "N/A"}");
        Console.WriteLine($"  Exif Date (UTC):    {img.ExifDateUtc ?? "N/A"}");
        Console.WriteLine($"  Taken Override:     {img.TakenOverrideUtc ?? "N/A"}");
        Console.WriteLine($"  Effective Date:     {img.EffectiveDateUtc ?? "N/A"}");
        Console.WriteLine($"  Added to DB (UTC):  {img.AddedUtc}");

        if (!string.IsNullOrWhiteSpace(img.ExifJson))
        {
            Console.WriteLine("  EXIF Metadata:");
            try
            {
                using var doc = JsonDocument.Parse(img.ExifJson);
                var formatted = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                foreach (var line in formatted.Split('\n'))
                {
                    Console.WriteLine($"    {line}");
                }
            }
            catch
            {
                Console.WriteLine($"    {img.ExifJson}");
            }
        }

        return 0;
    }

    private static async Task<int> HandleHashAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Usage: iconchest hash <file path>");
            return 1;
        }

        var path = args[0];
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File '{path}' does not exist.");
            return 1;
        }

        var hasher = new XxHash128Hasher();
        var hash = await hasher.HashFileAsync(path);
        Console.WriteLine($"Algorithm: {hasher.AlgorithmName}");
        Console.WriteLine($"Hash:      {hash}");
        return 0;
    }

    private static int HandleImage(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: iconchest image hide|unhide <id>");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        if (!long.TryParse(args[1], out var id))
        {
            Console.Error.WriteLine("Invalid image id.");
            return 1;
        }

        var (_, _, imageRepo, _, _, _, _, _) = CreateContext();
        var img = imageRepo.GetById(id);
        if (img == null)
        {
            Console.Error.WriteLine($"Image with ID {id} not found.");
            return 1;
        }

        switch (sub)
        {
            case "hide":
                imageRepo.SetHidden(id, true);
                imageRepo.RecomputeDuplicates(new[] { img.Hash });
                Console.WriteLine($"Image {id} is now hidden. Recomputed duplicates.");
                return 0;
            case "unhide":
                imageRepo.SetHidden(id, false);
                imageRepo.RecomputeDuplicates(new[] { img.Hash });
                Console.WriteLine($"Image {id} is now unhidden. Recomputed duplicates.");
                return 0;
            default:
                Console.Error.WriteLine($"Unknown image subcommand: '{sub}'");
                return 1;
        }
    }

    private static async Task<int> HandleThumbsAsync(string[] args)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Missing thumbs subcommand: status, enqueue, run, retry-failed, clear, verify, cleanup, show, make");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var (_, folderRepo, imageRepo, settingsService, cache, queue, renderer, provider) = CreateContext();

        switch (sub)
        {
            case "status":
            {
                var status = queue.GetStatus();
                var totalBytes = cache.GetTotalBytes();
                var maxMb = settingsService.Current.ThumbnailCacheMaxMb;

                Console.WriteLine("Thumbnail Queue Status:");
                Console.WriteLine($"  Total Jobs:     {status.TotalJobs}");
                Console.WriteLine($"  Pending:        {status.Pending}");
                Console.WriteLine($"  Running:        {status.Running}");
                Console.WriteLine($"  Done:           {status.Done}");
                Console.WriteLine($"  Failed:         {status.Failed}");
                Console.WriteLine($"  Evicted:        {status.Evicted}");
                Console.WriteLine($"  Orphaned:       {status.Orphaned}");
                Console.WriteLine();
                Console.WriteLine("Counts by Size:");
                foreach (var size in ThumbnailSizes.All)
                {
                    Console.WriteLine($"  {size}x{size}:       {status.CountsBySize.GetValueOrDefault(size, 0)}");
                }
                Console.WriteLine();
                Console.WriteLine("Cache Information:");
                Console.WriteLine($"  Location:       {cache.CacheDirectory}");
                Console.WriteLine($"  Current Size:   {FormatBytes(totalBytes)}");
                Console.WriteLine($"  Max Size:       {(maxMb > 0 ? $"{maxMb} MB" : "Unlimited")}");
                if (status.ItemsPerSecond > 0.05)
                {
                    Console.WriteLine($"  Throughput:     {status.ItemsPerSecond:F1} items/sec");
                }
                if (status.EstimatedTimeRemaining.HasValue)
                {
                    Console.WriteLine($"  Estimated ETA:  {status.EstimatedTimeRemaining.Value.TotalSeconds:F0}s");
                }
                return 0;
            }
            case "enqueue":
            {
                long? folderId = null;
                for (int i = 1; i < args.Length; i++)
                {
                    if (args[i] is "--folder" or "-f" && i + 1 < args.Length)
                    {
                        if (long.TryParse(args[i + 1], out var fid)) folderId = fid;
                    }
                }

                int enqueued = folderId.HasValue
                    ? queue.EnqueueForFolder(folderId.Value)
                    : queue.EnqueueAll();

                Console.WriteLine($"Enqueued {enqueued} thumbnail jobs.");
                return 0;
            }
            case "run":
            {
                bool untilEmpty = args.Any(a => a.Equals("--until-empty", StringComparison.OrdinalIgnoreCase));
                var worker = new ThumbnailWorker(queue, cache, renderer, settingsService, provider);

                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (sender, eventArgs) =>
                {
                    eventArgs.Cancel = true;
                    Console.WriteLine();
                    Console.WriteLine("Stopping thumbnail worker (finishing current job)...");
                    cts.Cancel();
                };

                worker.Start();
                Console.WriteLine($"Thumbnail worker started (until-empty: {untilEmpty}). Press Ctrl+C to stop.");

                while (!cts.IsCancellationRequested)
                {
                    var status = queue.GetStatus();
                    var etaStr = status.EstimatedTimeRemaining.HasValue
                        ? $"{status.EstimatedTimeRemaining.Value.TotalSeconds:F0}s"
                        : "N/A";

                    Console.Write($"\r[Running] Pending: {status.Pending} | Running: {status.Running} | Done: {status.Done} | Failed: {status.Failed} | Rate: {status.ItemsPerSecond:F1}/s | ETA: {etaStr,-6}");

                    if (untilEmpty && status.Pending == 0 && status.Running == 0)
                    {
                        break;
                    }

                    try
                    {
                        await Task.Delay(300, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }

                Console.WriteLine();
                await worker.StopAsync();
                var finalStatus = queue.GetStatus();
                Console.WriteLine($"Worker finished. Done: {finalStatus.Done}, Pending: {finalStatus.Pending}, Failed: {finalStatus.Failed}.");
                return 0;
            }
            case "retry-failed":
            {
                int reset = queue.RetryFailed();
                Console.WriteLine($"Reset {reset} failed jobs to pending.");
                return 0;
            }
            case "clear":
            {
                bool assumeYes = args.Any(a => a is "--yes" or "-y");
                if (!assumeYes)
                {
                    Console.Write("Clear all thumbnail files and database records? (y/N): ");
                    var ans = Console.ReadLine();
                    if (!string.Equals(ans?.Trim(), "y", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(ans?.Trim(), "yes", StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine("Operation cancelled.");
                        return 0;
                    }
                }

                await provider.ClearCacheAsync();
                Console.WriteLine("Thumbnail cache and records cleared.");
                return 0;
            }
            case "verify":
            {
                Console.WriteLine("Verifying thumbnail cache...");
                var report = await provider.VerifyCacheAsync();
                Console.WriteLine("Cache Verification Report:");
                Console.WriteLine($"  Total Done Records Checked: {report.TotalChecked}");
                Console.WriteLine($"  Valid Thumbnails On Disk:   {report.ValidDone}");
                Console.WriteLine($"  Restored to Pending:        {report.MissingFileRestoredToPending}");
                Console.WriteLine($"  Stale Disk Files Deleted:   {report.StaleFilesDeleted}");
                return 0;
            }
            case "cleanup":
            {
                int count = provider.CleanupOrphans();
                Console.WriteLine($"Cleaned up {count} orphaned thumbnail entries.");
                return 0;
            }
            case "show":
            {
                if (args.Length < 2 || !long.TryParse(args[1], out var imgId))
                {
                    Console.Error.WriteLine("Usage: iconchest thumbs show <image id>");
                    return 1;
                }

                var img = imageRepo.GetById(imgId);
                if (img == null)
                {
                    Console.Error.WriteLine($"Image with ID {imgId} not found.");
                    return 1;
                }

                Console.WriteLine($"Image {img.Id} ({img.FileName}) - Hash: {img.Hash}:");
                foreach (var size in ThumbnailSizes.All)
                {
                    var record = queue.GetRecord(img.Hash, size);
                    var filePath = cache.GetThumbnailPath(img.Hash, size);
                    bool exists = File.Exists(filePath);
                    var state = record?.State ?? "no_record";
                    var dims = record?.Width > 0 ? $"{record.Width}x{record.Height}" : "N/A";
                    var bytes = record?.Bytes.HasValue == true ? FormatBytes(record.Bytes.Value) : (exists ? FormatBytes(new FileInfo(filePath).Length) : "N/A");

                    Console.WriteLine($"  Size {size,3}: State={state,-8} Dims={dims,-9} Size={bytes,-9} OnDisk={exists} Path={filePath}");
                }
                return 0;
            }
            case "make":
            {
                if (args.Length < 2)
                {
                    Console.Error.WriteLine("Usage: iconchest thumbs make <file> --size N --out <path>");
                    return 1;
                }

                var sourceFile = args[1];
                if (!File.Exists(sourceFile))
                {
                    Console.Error.WriteLine($"File not found: {sourceFile}");
                    return 1;
                }

                int size = 128;
                string? outPath = null;

                for (int i = 2; i < args.Length; i++)
                {
                    if (args[i] is "--size" or "-s" && i + 1 < args.Length && int.TryParse(args[i + 1], out var s))
                    {
                        size = s;
                        i++;
                    }
                    else if (args[i] is "--out" or "-o" && i + 1 < args.Length)
                    {
                        outPath = args[i + 1];
                        i++;
                    }
                }

                if (string.IsNullOrWhiteSpace(outPath))
                {
                    Console.Error.WriteLine("Error: --out <path> is required.");
                    return 1;
                }

                // Refuse if --out is inside a library folder
                var fullOut = Path.GetFullPath(outPath);
                var libraryFolders = folderRepo.List();
                foreach (var lf in libraryFolders)
                {
                    var lfFull = Path.GetFullPath(lf.Path);
                    if (fullOut.StartsWith(lfFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                        fullOut.Equals(lfFull, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.Error.WriteLine($"Error: Output path '{outPath}' is inside library folder '{lf.Path}'. Writing inside library folders is refused.");
                        return 1;
                    }
                }

                var result = await renderer.RenderAsync(sourceFile, size, settingsService.Current.ThumbnailQuality);
                if (result.Status != ThumbnailRenderStatus.Success || result.Bytes == null)
                {
                    Console.Error.WriteLine($"Error rendering thumbnail: {result.ErrorMessage ?? result.Status.ToString()}");
                    return 1;
                }

                var outDir = Path.GetDirectoryName(fullOut);
                if (!string.IsNullOrWhiteSpace(outDir)) Directory.CreateDirectory(outDir);

                await File.WriteAllBytesAsync(fullOut, result.Bytes);
                Console.WriteLine($"Rendered {size}x{size} thumbnail ({result.Width}x{result.Height}, {FormatBytes(result.Bytes.Length)}) to {outPath}");
                return 0;
            }
            default:
                Console.Error.WriteLine($"Unknown thumbs subcommand: '{sub}'");
                return 1;
        }
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F2} MB";
    }
}
