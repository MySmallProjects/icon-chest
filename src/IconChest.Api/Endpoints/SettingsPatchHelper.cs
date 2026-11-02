using System.Text.Json;
using IconChest.Core.Logging;
using IconChest.Core.Settings;

namespace IconChest.Api.Endpoints;

public static class SettingsPatchHelper
{
    private static readonly string[] RestartKeys = { "dbPath", "api.allowLan", "api.portPreference" };

    public static (AppSettings NewSettings, IReadOnlyList<string> RestartRequired) ApplyPatch(
        AppSettings current,
        JsonElement patchElement)
    {
        var clone = current.Clone();
        var restartRequired = new List<string>();

        if (patchElement.ValueKind != JsonValueKind.Object)
        {
            throw new ArgumentException("Patch body must be a JSON object.");
        }

        foreach (var prop in patchElement.EnumerateObject())
        {
            var propName = prop.Name;
            var val = prop.Value;

            switch (propName.ToLowerInvariant())
            {
                case "dbpath":
                    var newDb = val.ValueKind == JsonValueKind.Null ? null : val.GetString();
                    if (clone.DbPath != newDb)
                    {
                        clone.DbPath = newDb;
                        restartRequired.Add("dbPath");
                    }
                    break;

                case "thumbnailcachepath":
                    clone.ThumbnailCachePath = val.ValueKind == JsonValueKind.Null ? null : val.GetString();
                    break;

                case "thumbnailcachemaxmb":
                    if (val.TryGetInt32(out var maxMb)) clone.ThumbnailCacheMaxMb = maxMb;
                    break;

                case "thumbnailquality":
                    if (val.TryGetInt32(out var tq)) clone.ThumbnailQuality = tq;
                    break;

                case "thumbnailworkers":
                    if (val.TryGetInt32(out var tw)) clone.ThumbnailWorkers = tw;
                    break;

                case "loglevel":
                    if (val.ValueKind == JsonValueKind.String &&
                        Enum.TryParse<AppLogLevel>(val.GetString(), ignoreCase: true, out var ll))
                    {
                        clone.LogLevel = ll;
                    }
                    break;

                case "theme":
                    if (val.ValueKind == JsonValueKind.String &&
                        Enum.TryParse<AppTheme>(val.GetString(), ignoreCase: true, out var theme))
                    {
                        clone.Theme = theme;
                    }
                    break;

                case "gridcaption":
                    if (val.ValueKind == JsonValueKind.String &&
                        Enum.TryParse<GridCaptionMode>(val.GetString(), ignoreCase: true, out var gc))
                    {
                        clone.GridCaption = gc;
                    }
                    break;

                case "checkerboardmode":
                    if (val.ValueKind == JsonValueKind.String &&
                        Enum.TryParse<CheckerboardMode>(val.GetString(), ignoreCase: true, out var cm))
                    {
                        clone.CheckerboardMode = cm;
                    }
                    break;

                case "previewinfooverlaydefault":
                    if (val.ValueKind is JsonValueKind.True or JsonValueKind.False) clone.PreviewInfoOverlayDefault = val.GetBoolean();
                    break;

                case "defaultview":
                    if (val.ValueKind == JsonValueKind.String) clone.DefaultView = val.GetString() ?? "Folders";
                    break;

                case "rememberwindowstate":
                    if (val.ValueKind is JsonValueKind.True or JsonValueKind.False) clone.RememberWindowState = val.GetBoolean();
                    break;

                case "includepatterns":
                    if (val.ValueKind == JsonValueKind.String) clone.IncludePatterns = val.GetString() ?? string.Empty;
                    break;

                case "excludenames":
                    if (val.ValueKind == JsonValueKind.String) clone.ExcludeNames = val.GetString() ?? string.Empty;
                    break;

                case "scanonstartup":
                    if (val.ValueKind is JsonValueKind.True or JsonValueKind.False) clone.ScanOnStartup = val.GetBoolean();
                    break;

                case "scanworkers":
                    if (val.TryGetInt32(out var sw)) clone.ScanWorkers = sw;
                    break;

                case "autoremovemissing":
                    if (val.ValueKind is JsonValueKind.True or JsonValueKind.False) clone.AutoRemoveMissing = val.GetBoolean();
                    break;

                case "confirmbeforedelete":
                    if (val.ValueKind is JsonValueKind.True or JsonValueKind.False) clone.ConfirmBeforeDelete = val.GetBoolean();
                    break;

                case "window":
                    if (val.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var wp in val.EnumerateObject())
                        {
                            switch (wp.Name.ToLowerInvariant())
                            {
                                case "width": if (wp.Value.TryGetInt32(out var w)) clone.Window.Width = w; break;
                                case "height": if (wp.Value.TryGetInt32(out var h)) clone.Window.Height = h; break;
                                case "x": clone.Window.X = wp.Value.ValueKind == JsonValueKind.Null ? null : wp.Value.GetInt32(); break;
                                case "y": clone.Window.Y = wp.Value.ValueKind == JsonValueKind.Null ? null : wp.Value.GetInt32(); break;
                                case "maximised": if (wp.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) clone.Window.Maximised = wp.Value.GetBoolean(); break;
                            }
                        }
                    }
                    break;

                case "api":
                    if (val.ValueKind == JsonValueKind.Object)
                    {
                        foreach (var ap in val.EnumerateObject())
                        {
                            switch (ap.Name.ToLowerInvariant())
                            {
                                case "portpreference":
                                    if (ap.Value.TryGetInt32(out var port) && clone.Api.PortPreference != port)
                                    {
                                        clone.Api.PortPreference = port;
                                        restartRequired.Add("api.portPreference");
                                    }
                                    break;

                                case "allowlan":
                                    if (ap.Value.ValueKind is JsonValueKind.True or JsonValueKind.False)
                                    {
                                        bool lan = ap.Value.GetBoolean();
                                        if (clone.Api.AllowLan != lan)
                                        {
                                            clone.Api.AllowLan = lan;
                                            restartRequired.Add("api.allowLan");
                                        }
                                    }
                                    break;
                            }
                        }
                    }
                    break;
            }
        }

        // Validate via Core
        clone.Validate();
        return (clone, restartRequired);
    }
}

