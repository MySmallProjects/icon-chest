using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace IconChest.Core.Versioning;

public sealed class VersionInfo : IVersionInfo
{
    private static readonly DateTime Epoch = new(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public string Version { get; }
    public DateTime? BuildDateUtc { get; }
    public string ExecutablePath { get; }
    public string ExecutableDirectory { get; }

    public VersionInfo(Assembly? targetAssembly = null, string? baseDirectory = null)
    {
        var assembly = targetAssembly ?? Assembly.GetEntryAssembly() ?? typeof(VersionInfo).Assembly;
        var directory = baseDirectory ?? AppContext.BaseDirectory;

        ExecutableDirectory = directory;
        ExecutablePath = ResolveExecutablePath(directory);

        // 1. Try reading from assembly informational version
        string? resolvedVersion = null;
        DateTime? resolvedBuildDate = null;

        var infoAttr = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
        if (!string.IsNullOrWhiteSpace(infoAttr?.InformationalVersion))
        {
            var rawVersion = infoAttr.InformationalVersion.Trim();
            // Strip git commit hash if attached (+commit)
            var plusIdx = rawVersion.IndexOf('+');
            resolvedVersion = plusIdx >= 0 ? rawVersion[..plusIdx] : rawVersion;
        }

        // 2. Try reading version.json next to executable/baseDirectory
        var versionJsonPath = Path.Combine(directory, "version.json");
        if (File.Exists(versionJsonPath))
        {
            try
            {
                var json = File.ReadAllText(versionJsonPath);
                using var doc = JsonDocument.Parse(json);
                if (string.IsNullOrWhiteSpace(resolvedVersion) &&
                    doc.RootElement.TryGetProperty("version", out var verProp))
                {
                    resolvedVersion = verProp.GetString();
                }

                if (doc.RootElement.TryGetProperty("buildDateUtc", out var dateProp))
                {
                    if (DateTime.TryParse(dateProp.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsedDate))
                    {
                        resolvedBuildDate = parsedDate.ToUniversalTime();
                    }
                }
            }
            catch
            {
                // version.json fallback failure must not crash version lookup
            }
        }

        // Fallback to Assembly.GetName().Version if still empty
        if (string.IsNullOrWhiteSpace(resolvedVersion))
        {
            var asmVersion = assembly.GetName().Version;
            resolvedVersion = asmVersion != null ? asmVersion.ToString() : "0.1.0.0";
        }

        // If build date wasn't in version.json, derive build date from version if possible
        if (!resolvedBuildDate.HasValue && TryParseBuildDateFromVersion(resolvedVersion, out var derivedDate))
        {
            resolvedBuildDate = derivedDate;
        }

        Version = resolvedVersion;
        BuildDateUtc = resolvedBuildDate;
    }

    private static string ResolveExecutablePath(string directory)
    {
        var currentProcessPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(currentProcessPath))
        {
            return currentProcessPath;
        }

        var entryAsm = Assembly.GetEntryAssembly();
        if (entryAsm != null && !string.IsNullOrWhiteSpace(entryAsm.Location))
        {
            return entryAsm.Location;
        }

        return Path.Combine(directory, "iconchest");
    }

    public static (int Build, int Revision) ComputeVersionNumbers(DateTime utcTime)
    {
        var utc = utcTime.ToUniversalTime();
        var days = (int)Math.Floor((utc.Date - Epoch).TotalDays);
        var seconds = (int)Math.Floor(utc.TimeOfDay.TotalSeconds / 2.0);

        var build = Math.Clamp(days, 0, 65535);
        var rev = Math.Clamp(seconds, 0, 65535);
        return (build, rev);
    }

    public static bool TryParseBuildDateFromVersion(string version, out DateTime buildDateUtc)
    {
        buildDateUtc = default;
        var parts = version.Split('.');
        if (parts.Length >= 4 &&
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var build) &&
            int.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var rev))
        {
            buildDateUtc = Epoch.AddDays(build).AddSeconds(rev * 2);
            return true;
        }

        return false;
    }
}

