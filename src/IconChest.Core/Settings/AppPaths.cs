namespace IconChest.Core.Settings;

public sealed class AppPaths : IAppPaths
{
    public const string ConfigDirEnvVar = "ICONCHEST_CONFIG_DIR";
    public const string DataDirEnvVar = "ICONCHEST_DATA_DIR";

    public string ConfigDirectory { get; }
    public string DataDirectory { get; }
    public string SettingsFilePath => Path.Combine(ConfigDirectory, "settings.json");
    public string CorruptSettingsFilePath => Path.Combine(ConfigDirectory, "settings.json.corrupt");
    public string DefaultDatabasePath => Path.Combine(DataDirectory, "iconchest.db");
    public string DefaultThumbnailCachePath => Path.Combine(DataDirectory, "thumbnails");
    public string DefaultLogDirectory => Path.Combine(DataDirectory, "logs");

    public AppPaths(
        string? configDirOverride = null,
        string? dataDirOverride = null,
        bool? isWindowsOverride = null,
        Func<string, string?>? getEnvironmentVariable = null)
    {
        var getEnv = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
        var isWindows = isWindowsOverride ?? OperatingSystem.IsWindows();

        // 1. Resolve Config Directory
        var envConfig = configDirOverride ?? getEnv(ConfigDirEnvVar);
        if (!string.IsNullOrWhiteSpace(envConfig))
        {
            ConfigDirectory = Path.GetFullPath(envConfig);
        }
        else if (isWindows)
        {
            var appData = getEnv("APPDATA") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
            ConfigDirectory = Path.Combine(appData, "IconChest");
        }
        else
        {
            var xdgConfig = getEnv("XDG_CONFIG_HOME");
            if (!string.IsNullOrWhiteSpace(xdgConfig))
            {
                ConfigDirectory = Path.Combine(xdgConfig, "IconChest");
            }
            else
            {
                var home = getEnv("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                ConfigDirectory = Path.Combine(home, ".config", "IconChest");
            }
        }

        // 2. Resolve Data Directory
        var envData = dataDirOverride ?? getEnv(DataDirEnvVar);
        if (!string.IsNullOrWhiteSpace(envData))
        {
            DataDirectory = Path.GetFullPath(envData);
        }
        else if (isWindows)
        {
            var localAppData = getEnv("LOCALAPPDATA") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
            DataDirectory = Path.Combine(localAppData, "IconChest");
        }
        else
        {
            var xdgData = getEnv("XDG_DATA_HOME");
            if (!string.IsNullOrWhiteSpace(xdgData))
            {
                DataDirectory = Path.Combine(xdgData, "IconChest");
            }
            else
            {
                var home = getEnv("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                DataDirectory = Path.Combine(home, ".local", "share", "IconChest");
            }
        }
    }
}

