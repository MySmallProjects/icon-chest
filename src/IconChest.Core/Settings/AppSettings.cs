using System.Text.Json;
using System.Text.Json.Serialization;
using IconChest.Core.Logging;

namespace IconChest.Core.Settings;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppTheme
{
    Light,
    Dark
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GridCaptionMode
{
    None,
    Filename,
    Title
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CheckerboardMode
{
    Checker,
    Light,
    Dark
}

public sealed class WindowSettings
{
    public int Width { get; set; } = 1280;
    public int Height { get; set; } = 800;
    public int? X { get; set; } = null;
    public int? Y { get; set; } = null;
    public bool Maximised { get; set; } = false;

    public WindowSettings Clone() => new()
    {
        Width = Width,
        Height = Height,
        X = X,
        Y = Y,
        Maximised = Maximised
    };
}

public sealed class ApiSettings
{
    public int PortPreference { get; set; } = 0;
    public bool AllowLan { get; set; } = false;
    public string? LanPasswordHash { get; set; } = null;

    public ApiSettings Clone() => new()
    {
        PortPreference = PortPreference,
        AllowLan = AllowLan,
        LanPasswordHash = LanPasswordHash
    };
}

public sealed class AppSettings
{
    public int Version { get; set; } = 1;
    public string? DbPath { get; set; } = null;
    public string? ThumbnailCachePath { get; set; } = null;
    public string? BackupFolder { get; set; } = null;
    public int ThumbnailCacheMaxMb { get; set; } = 2048;
    public int ThumbnailQuality { get; set; } = 85;
    public int ThumbnailWorkers { get; set; } = 0;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public AppLogLevel LogLevel { get; set; } = AppLogLevel.Information;

    public AppTheme Theme { get; set; } = AppTheme.Light;
    public GridCaptionMode GridCaption { get; set; } = GridCaptionMode.None;
    public CheckerboardMode CheckerboardMode { get; set; } = CheckerboardMode.Checker;

    public bool PreviewInfoOverlayDefault { get; set; } = true;
    public string DefaultView { get; set; } = "Folders";
    public bool RememberWindowState { get; set; } = true;

    public WindowSettings Window { get; set; } = new();

    public string IncludePatterns { get; set; } = "*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.ico";
    public string ExcludeNames { get; set; } = ".thumbs;@eaDir;.git;node_modules";

    public bool ScanOnStartup { get; set; } = false;
    public int ScanWorkers { get; set; } = 0;
    public bool AutoRemoveMissing { get; set; } = false;
    public bool ConfirmBeforeDelete { get; set; } = true;

    public ApiSettings Api { get; set; } = new();

    [JsonExtensionData]
    public Dictionary<string, JsonElement> ExtensionData { get; set; } = new();

    public void Validate() => Validate(null);

    public void Validate(IEnumerable<string>? libraryFolderPaths)
    {
        if (ThumbnailCacheMaxMb < 64 || ThumbnailCacheMaxMb > 1_048_576)
        {
            throw new ArgumentOutOfRangeException(nameof(ThumbnailCacheMaxMb), "Thumbnail cache max size must be between 64 and 1,048,576 MB.");
        }

        if (ThumbnailQuality < 1 || ThumbnailQuality > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ThumbnailQuality), "Thumbnail quality must be between 1 and 100.");
        }

        if (ThumbnailWorkers < 0 || ThumbnailWorkers > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(ThumbnailWorkers), "Thumbnail workers must be between 0 and 32.");
        }

        if (ScanWorkers < 0 || ScanWorkers > 32)
        {
            throw new ArgumentOutOfRangeException(nameof(ScanWorkers), "Scan workers must be between 0 and 32.");
        }

        if (Window.Width <= 0 || Window.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Window), "Window width and height must be positive.");
        }

        if (Api.PortPreference != 0 && (Api.PortPreference < 1024 || Api.PortPreference > 65535))
        {
            throw new ArgumentOutOfRangeException(nameof(Api.PortPreference), "Port preference must be 0 or between 1024 and 65535.");
        }

        if (Api.AllowLan && string.IsNullOrWhiteSpace(Api.LanPasswordHash))
        {
            throw new InvalidOperationException("API LAN access cannot be enabled without setting a LAN password.");
        }

        if (string.IsNullOrWhiteSpace(IncludePatterns))
        {
            IncludePatterns = "*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.ico";
        }

        if (ExcludeNames == null)
        {
            ExcludeNames = string.Empty;
        }

        if (string.IsNullOrWhiteSpace(DefaultView))
        {
            DefaultView = "Folders";
        }

        if (libraryFolderPaths != null)
        {
            ValidateNotInsideLibraryFolders(DbPath, nameof(DbPath), libraryFolderPaths);
            ValidateNotInsideLibraryFolders(ThumbnailCachePath, nameof(ThumbnailCachePath), libraryFolderPaths);
            ValidateNotInsideLibraryFolders(BackupFolder, nameof(BackupFolder), libraryFolderPaths);
        }
    }

    private static void ValidateNotInsideLibraryFolders(string? path, string propertyName, IEnumerable<string> libraryFolderPaths)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        var fullPath = Path.GetFullPath(path);

        foreach (var libPath in libraryFolderPaths)
        {
            if (string.IsNullOrWhiteSpace(libPath)) continue;
            var fullLibPath = Path.GetFullPath(libPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (fullPath.Equals(fullLibPath, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullLibPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
                fullPath.StartsWith(fullLibPath + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"Path '{path}' in {propertyName} must not be inside library folder '{libPath}'.", propertyName);
            }
        }
    }

    public AppSettings Clone()
    {
        var clone = new AppSettings
        {
            Version = Version,
            DbPath = DbPath,
            ThumbnailCachePath = ThumbnailCachePath,
            BackupFolder = BackupFolder,
            ThumbnailCacheMaxMb = ThumbnailCacheMaxMb,
            ThumbnailQuality = ThumbnailQuality,
            ThumbnailWorkers = ThumbnailWorkers,
            LogLevel = LogLevel,
            Theme = Theme,
            GridCaption = GridCaption,
            CheckerboardMode = CheckerboardMode,
            PreviewInfoOverlayDefault = PreviewInfoOverlayDefault,
            DefaultView = DefaultView,
            RememberWindowState = RememberWindowState,
            Window = Window.Clone(),
            IncludePatterns = IncludePatterns,
            ExcludeNames = ExcludeNames,
            ScanOnStartup = ScanOnStartup,
            ScanWorkers = ScanWorkers,
            AutoRemoveMissing = AutoRemoveMissing,
            ConfirmBeforeDelete = ConfirmBeforeDelete,
            Api = Api.Clone()
        };

        foreach (var kv in ExtensionData)
        {
            clone.ExtensionData[kv.Key] = kv.Value.Clone();
        }

        return clone;
    }
}

