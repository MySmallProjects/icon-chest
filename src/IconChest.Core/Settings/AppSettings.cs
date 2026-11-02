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

    public void Validate()
    {
        if (ThumbnailCacheMaxMb < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ThumbnailCacheMaxMb), "Thumbnail cache max size cannot be negative.");
        }

        if (ThumbnailQuality < 1 || ThumbnailQuality > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(ThumbnailQuality), "Thumbnail quality must be between 1 and 100.");
        }

        if (ThumbnailWorkers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ThumbnailWorkers), "Thumbnail workers cannot be negative.");
        }

        if (ScanWorkers < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ScanWorkers), "Scan workers cannot be negative.");
        }

        if (Window.Width <= 0 || Window.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Window), "Window width and height must be positive.");
        }

        if (Api.PortPreference < 0 || Api.PortPreference > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(Api.PortPreference), "Port preference must be between 0 and 65535.");
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
    }

    public AppSettings Clone()
    {
        var clone = new AppSettings
        {
            Version = Version,
            DbPath = DbPath,
            ThumbnailCachePath = ThumbnailCachePath,
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

