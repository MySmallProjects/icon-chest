using System.Globalization;
using System.Text.Json;
using IconChest.Core.Data;
using IconChest.Core.Logging;
using IconChest.Core.Settings;
using IconChest.Core.Versioning;
using Microsoft.Extensions.Logging;

namespace IconChest.Cli;

public static class Program
{
    public static int Main(string[] args)
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
        Console.WriteLine("  version                       Print version, build date, and executable path");
        Console.WriteLine("  log-test [--level <level>]    Write messages at each level and report filtering");
        Console.WriteLine("  settings show                 Display current application settings");
        Console.WriteLine("  settings get <key>            Display value of a setting key");
        Console.WriteLine("  settings set <key> <value>    Update value of a setting key");
        Console.WriteLine("  db init                       Create or migrate database to latest schema");
        Console.WriteLine("  db info                       Print database path and current schema version");
    }

    private static int UnknownCommand(string command)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine($"Unknown command: '{command}'");
        Console.ResetColor();
        PrintUsage();
        return 1;
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
                    Console.Error.WriteLine($"Invalid log level '{args[i + 1]}'. Allowed values: Off, Error, Warning, Information, Debug, Trace.");
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
            Console.Error.WriteLine("Missing db subcommand: init or info");
            return 1;
        }

        var sub = args[0].ToLowerInvariant();
        var paths = new AppPaths();
        var settingsService = new SettingsService(paths);
        var db = new IconChestDatabase(settingsService);

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
            default:
                Console.Error.WriteLine($"Unknown db subcommand: '{sub}'");
                return 1;
        }
    }
}

