using IconChest.Core.Common;
using IconChest.Core.Settings;
using Xunit;

namespace IconChest.Desktop.Tests;

public class WindowStateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly AppPaths _paths;
    private readonly SettingsService _settingsService;

    public WindowStateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "iconchest_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _paths = new AppPaths(_tempDir, _tempDir);
        _settingsService = new SettingsService(_paths);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch {}
        }
    }

    [Fact]
    public void RestoreBounds_NoSavedSettings_ReturnsDefaultsCentered()
    {
        using var service = new WindowStateService(_settingsService);
        var primary = new ScreenBounds(0, 0, 1920, 1080);

        var (width, height, x, y, maximised) = service.RestoreBounds(primary);

        Assert.Equal(WindowStateService.DefaultWidth, width);
        Assert.Equal(WindowStateService.DefaultHeight, height);
        Assert.Equal((1920 - WindowStateService.DefaultWidth) / 2, x);
        Assert.Equal((1080 - WindowStateService.DefaultHeight) / 2, y);
        Assert.False(maximised);
    }

    [Fact]
    public void RestoreBounds_ClampsToMinimumDimensions()
    {
        _settingsService.Update(s =>
        {
            s.Window.Width = 300;
            s.Window.Height = 200;
            s.Window.X = 100;
            s.Window.Y = 100;
        });

        using var service = new WindowStateService(_settingsService);
        var primary = new ScreenBounds(0, 0, 1920, 1080);

        var (width, height, _, _, _) = service.RestoreBounds(primary);

        Assert.Equal(WindowStateService.MinWidth, width);
        Assert.Equal(WindowStateService.MinHeight, height);
    }

    [Fact]
    public void RestoreBounds_OffScreenPosition_CentersOnPrimaryScreen()
    {
        _settingsService.Update(s =>
        {
            s.Window.Width = 1000;
            s.Window.Height = 600;
            s.Window.X = -5000; // Off-screen disconnected monitor
            s.Window.Y = -5000;
        });

        using var service = new WindowStateService(_settingsService);
        var primary = new ScreenBounds(0, 0, 1920, 1080);

        var (width, height, x, y, _) = service.RestoreBounds(primary);

        Assert.Equal(1000, width);
        Assert.Equal(600, height);
        // Recovered to center on primary screen
        Assert.Equal((1920 - 1000) / 2, x);
        Assert.Equal((1080 - 600) / 2, y);
    }

    [Fact]
    public void RestoreBounds_RememberWindowStateDisabled_AlwaysCentersDefault()
    {
        _settingsService.Update(s =>
        {
            s.RememberWindowState = false;
            s.Window.Width = 1400;
            s.Window.Height = 900;
            s.Window.X = 100;
            s.Window.Y = 100;
        });

        using var service = new WindowStateService(_settingsService);
        var primary = new ScreenBounds(0, 0, 1920, 1080);

        var (width, height, _, _, _) = service.RestoreBounds(primary);

        Assert.Equal(WindowStateService.DefaultWidth, width);
        Assert.Equal(WindowStateService.DefaultHeight, height);
    }

    [Fact]
    public async Task OnWindowStateChanged_DebouncesAndFlushesState()
    {
        using var service = new WindowStateService(_settingsService, TimeSpan.FromMilliseconds(50));

        service.OnWindowStateChanged(1024, 768, 50, 50, false);

        // Before debounce timer fires
        Assert.NotEqual(1024, _settingsService.Current.Window.Width);

        // Wait for debounce timer
        await Task.Delay(150);

        var win = _settingsService.Current.Window;
        Assert.Equal(1024, win.Width);
        Assert.Equal(768, win.Height);
        Assert.Equal(50, win.X);
        Assert.Equal(50, win.Y);
        Assert.False(win.Maximised);
    }

    [Fact]
    public void ConcurrentSettingsUpdate_DoesNotClobberWindow()
    {
        using var service = new WindowStateService(_settingsService);

        service.OnWindowStateChanged(1200, 850, 40, 40, true);
        service.Flush();

        // Concurrent update modifying an unrelated setting
        _settingsService.Update(s => s.Theme = AppTheme.Dark);

        var current = _settingsService.Current;
        Assert.Equal(AppTheme.Dark, current.Theme);
        Assert.Equal(1200, current.Window.Width);
        Assert.Equal(850, current.Window.Height);
        Assert.True(current.Window.Maximised);
    }
}

