using System.Text.Json;
using IconChest.Core.Logging;
using IconChest.Core.Settings;
using Xunit;

namespace IconChest.Core.Tests;

public class SettingsTests
{
    private static (SettingsService Service, string ConfigDir, string DataDir) CreateTestContext()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"iconchest-settings-test-{Guid.NewGuid():N}");
        var configDir = Path.Combine(tempRoot, "config");
        var dataDir = Path.Combine(tempRoot, "data");

        var paths = new AppPaths(configDirOverride: configDir, dataDirOverride: dataDir, isWindowsOverride: false);
        var service = new SettingsService(paths);
        return (service, configDir, dataDir);
    }

    [Fact]
    public void Defaults_MatchRequirementsOnFirstRun()
    {
        var (service, configDir, _) = CreateTestContext();
        try
        {
            var s = service.Current;
            Assert.Equal(1, s.Version);
            Assert.Null(s.DbPath);
            Assert.Null(s.ThumbnailCachePath);
            Assert.Equal(2048, s.ThumbnailCacheMaxMb);
            Assert.Equal(85, s.ThumbnailQuality);
            Assert.Equal(0, s.ThumbnailWorkers);
            Assert.Equal(AppLogLevel.Information, s.LogLevel);
            Assert.Equal(AppTheme.Light, s.Theme);
            Assert.Equal(GridCaptionMode.None, s.GridCaption);
            Assert.Equal(CheckerboardMode.Checker, s.CheckerboardMode);
            Assert.True(s.PreviewInfoOverlayDefault);
            Assert.Equal("Folders", s.DefaultView);
            Assert.True(s.RememberWindowState);
            Assert.Equal(1280, s.Window.Width);
            Assert.Equal(800, s.Window.Height);
            Assert.Null(s.Window.X);
            Assert.Null(s.Window.Y);
            Assert.False(s.Window.Maximised);
            Assert.Equal("*.png;*.jpg;*.jpeg;*.gif;*.webp;*.svg;*.bmp;*.ico", s.IncludePatterns);
            Assert.Equal(".thumbs;@eaDir;.git;node_modules", s.ExcludeNames);
            Assert.False(s.ScanOnStartup);
            Assert.Equal(0, s.ScanWorkers);
            Assert.False(s.AutoRemoveMissing);
            Assert.True(s.ConfirmBeforeDelete);
            Assert.Equal(0, s.Api.PortPreference);
            Assert.False(s.Api.AllowLan);
            Assert.Null(s.Api.LanPasswordHash);

            Assert.True(File.Exists(Path.Combine(configDir, "settings.json")));
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void RoundTrip_SaveAndLoad()
    {
        var (service, configDir, dataDir) = CreateTestContext();
        try
        {
            service.Update(s =>
            {
                s.Theme = AppTheme.Dark;
                s.ThumbnailCacheMaxMb = 4096;
                s.Window.Width = 1920;
                s.Window.Height = 1080;
                s.Window.Maximised = true;
            });

            var reloaded = new SettingsService(new AppPaths(configDirOverride: configDir, dataDirOverride: dataDir, isWindowsOverride: false));
            var s = reloaded.Current;
            Assert.Equal(AppTheme.Dark, s.Theme);
            Assert.Equal(4096, s.ThumbnailCacheMaxMb);
            Assert.Equal(1920, s.Window.Width);
            Assert.Equal(1080, s.Window.Height);
            Assert.True(s.Window.Maximised);
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void UnknownKeys_ArePreservedOnSave()
    {
        var (service, configDir, dataDir) = CreateTestContext();
        try
        {
            var settingsFile = Path.Combine(configDir, "settings.json");
            var json = File.ReadAllText(settingsFile);

            using var doc = JsonDocument.Parse(json);
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json) ?? new();
            dict["futureCustomPluginSetting"] = "custom-value-12345";
            File.WriteAllText(settingsFile, JsonSerializer.Serialize(dict, new JsonSerializerOptions { WriteIndented = true }));

            // Reload and modify known setting
            service.Reload();
            service.Update(s => s.Theme = AppTheme.Dark);

            var savedRawJson = File.ReadAllText(settingsFile);
            Assert.Contains("futureCustomPluginSetting", savedRawJson);
            Assert.Contains("custom-value-12345", savedRawJson);
            Assert.Contains("Dark", savedRawJson);
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void CorruptFile_IsRenamedToCorruptAndReplacedWithDefaults()
    {
        var (service, configDir, _) = CreateTestContext();
        try
        {
            var settingsFile = Path.Combine(configDir, "settings.json");
            var corruptFile = Path.Combine(configDir, "settings.json.corrupt");

            // Overwrite with broken JSON
            File.WriteAllText(settingsFile, "{ this is not valid json! %$#@");

            service.Reload();

            Assert.True(File.Exists(corruptFile), "settings.json.corrupt should have been created");
            Assert.True(File.Exists(settingsFile), "A fresh settings.json should have been created");

            var corruptContent = File.ReadAllText(corruptFile);
            Assert.Contains("{ this is not valid json!", corruptContent);

            var recovered = service.Current;
            Assert.Equal(AppTheme.Light, recovered.Theme);
            Assert.Equal(1, recovered.Version);
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void InvalidValues_AreRejectedOrClamped()
    {
        var (service, configDir, _) = CreateTestContext();
        try
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                service.Update(s => s.ThumbnailCacheMaxMb = -10);
            });

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                service.Update(s => s.ScanWorkers = -1);
            });

            Assert.Throws<ArgumentOutOfRangeException>(() =>
            {
                service.Update(s => s.Window.Width = 0);
            });
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ChangeEvent_FiresOnUpdate()
    {
        var (service, configDir, _) = CreateTestContext();
        try
        {
            var fired = false;
            AppSettings? receivedSettings = null;

            service.SettingsChanged += (sender, settings) =>
            {
                fired = true;
                receivedSettings = settings;
            };

            service.Update(s => s.Theme = AppTheme.Dark);

            Assert.True(fired);
            Assert.NotNull(receivedSettings);
            Assert.Equal(AppTheme.Dark, receivedSettings.Theme);
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void AtomicWrite_LeavesNoTempFiles()
    {
        var (service, configDir, _) = CreateTestContext();
        try
        {
            for (var i = 0; i < 10; i++)
            {
                var val = i;
                service.Update(s => s.ThumbnailCacheMaxMb = 1000 + val);
            }

            var tmpFiles = Directory.GetFiles(configDir, "*.tmp");
            Assert.Empty(tmpFiles);
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Fact]
    public void ConcurrentUpdates_DoNotCorruptFile()
    {
        var (service, configDir, _) = CreateTestContext();
        try
        {
            Parallel.For(0, 30, i =>
            {
                service.Update(s =>
                {
                    s.Window.Width = 1000 + i;
                });
            });

            var settingsFile = Path.Combine(configDir, "settings.json");
            var json = File.ReadAllText(settingsFile);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(loaded);
            Assert.InRange(loaded.Window.Width, 1000, 1030);
        }
        finally
        {
            var parent = Path.GetDirectoryName(configDir);
            if (parent != null && Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    [InlineData(-5)]
    public void Validation_InvalidThumbnailQuality_Throws(int quality)
    {
        var settings = new AppSettings { ThumbnailQuality = quality };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }

    [Fact]
    public void Validation_NegativeThumbnailWorkers_Throws()
    {
        var settings = new AppSettings { ThumbnailWorkers = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => settings.Validate());
    }
}

