using IconChest.Core.Settings;
using Xunit;

namespace IconChest.Core.Tests;

public class PathsTests
{
    [Fact]
    public void EnvironmentOverrides_TakePrecedence()
    {
        var customConfig = "/custom/config/dir";
        var customData = "/custom/data/dir";

        var paths = new AppPaths(
            configDirOverride: customConfig,
            dataDirOverride: customData,
            isWindowsOverride: false);

        Assert.Equal(Path.GetFullPath(customConfig), paths.ConfigDirectory);
        Assert.Equal(Path.GetFullPath(customData), paths.DataDirectory);
        Assert.Equal(Path.Combine(Path.GetFullPath(customConfig), "settings.json"), paths.SettingsFilePath);
        Assert.Equal(Path.Combine(Path.GetFullPath(customData), "iconchest.db"), paths.DefaultDatabasePath);
    }

    [Fact]
    public void Defaults_ResolveCorrectlyForLinux()
    {
        var fakeEnv = new Dictionary<string, string?>
        {
            ["HOME"] = "/home/testuser",
            ["XDG_CONFIG_HOME"] = "/home/testuser/.config",
            ["XDG_DATA_HOME"] = "/home/testuser/.local/share"
        };

        var paths = new AppPaths(
            isWindowsOverride: false,
            getEnvironmentVariable: key => fakeEnv.GetValueOrDefault(key));

        Assert.Equal("/home/testuser/.config/IconChest", paths.ConfigDirectory);
        Assert.Equal("/home/testuser/.local/share/IconChest", paths.DataDirectory);
        Assert.Equal("/home/testuser/.config/IconChest/settings.json", paths.SettingsFilePath);
        Assert.Equal("/home/testuser/.local/share/IconChest/iconchest.db", paths.DefaultDatabasePath);
        Assert.Equal("/home/testuser/.local/share/IconChest/thumbnails", paths.DefaultThumbnailCachePath);
        Assert.Equal("/home/testuser/.local/share/IconChest/logs", paths.DefaultLogDirectory);
    }

    [Fact]
    public void Defaults_ResolveCorrectlyForWindows()
    {
        var fakeEnv = new Dictionary<string, string?>
        {
            ["APPDATA"] = @"C:\Users\testuser\AppData\Roaming",
            ["LOCALAPPDATA"] = @"C:\Users\testuser\AppData\Local"
        };

        var paths = new AppPaths(
            isWindowsOverride: true,
            getEnvironmentVariable: key => fakeEnv.GetValueOrDefault(key));

        Assert.Equal(Path.Combine(@"C:\Users\testuser\AppData\Roaming", "IconChest"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine(@"C:\Users\testuser\AppData\Local", "IconChest"), paths.DataDirectory);
        Assert.Equal(Path.Combine(paths.ConfigDirectory, "settings.json"), paths.SettingsFilePath);
        Assert.Equal(Path.Combine(paths.DataDirectory, "iconchest.db"), paths.DefaultDatabasePath);
    }
}

