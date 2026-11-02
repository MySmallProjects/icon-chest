using IconChest.Core.Settings;

namespace IconChest.Desktop;

public readonly record struct ScreenBounds(int X, int Y, int Width, int Height)
{
    public bool Contains(int px, int py) =>
        px >= X && px < X + Width && py >= Y && py < Y + Height;

    public bool Intersects(int px, int py, int pWidth, int pHeight) =>
        px < X + Width && px + pWidth > X && py < Y + Height && py + pHeight > Y;
}

public sealed class WindowStateService : IDisposable
{
    public const int MinWidth = 800;
    public const int MinHeight = 500;
    public const int DefaultWidth = 1280;
    public const int DefaultHeight = 800;

    private readonly ISettingsService _settingsService;
    private readonly TimeSpan _debounceDelay;
    private readonly object _lock = new();

    private Timer? _debounceTimer;
    private WindowSettings? _pendingState;
    private bool _disposed;

    public WindowStateService(ISettingsService settingsService, TimeSpan? debounceDelay = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _debounceDelay = debounceDelay ?? TimeSpan.FromMilliseconds(500);
    }

    public (int Width, int Height, int? X, int? Y, bool Maximised) RestoreBounds(
        ScreenBounds primaryScreen,
        IEnumerable<ScreenBounds>? allScreens = null)
    {
        var settings = _settingsService.Current;

        // If rememberWindowState is disabled in settings, return defaults
        if (settings.RememberWindowState == false)
        {
            return CenterOnScreen(DefaultWidth, DefaultHeight, primaryScreen);
        }

        var win = settings.Window;
        int width = Math.Max(MinWidth, win.Width > 0 ? win.Width : DefaultWidth);
        int height = Math.Max(MinHeight, win.Height > 0 ? win.Height : DefaultHeight);
        bool maximised = win.Maximised;

        if (!win.X.HasValue || !win.Y.HasValue)
        {
            var centered = CenterOnScreen(width, height, primaryScreen);
            return (centered.Width, centered.Height, centered.X, centered.Y, maximised);
        }

        int x = win.X.Value;
        int y = win.Y.Value;

        // Check whether the saved coordinates are visible on any currently connected screen
        var screens = allScreens?.ToList() ?? [primaryScreen];
        bool isVisibleOnAnyScreen = screens.Any(s => s.Intersects(x, y, Math.Min(width, 100), Math.Min(height, 100)));

        if (!isVisibleOnAnyScreen)
        {
            var centered = CenterOnScreen(width, height, primaryScreen);
            return (centered.Width, centered.Height, centered.X, centered.Y, maximised);
        }

        return (width, height, x, y, maximised);
    }

    private static (int Width, int Height, int? X, int? Y, bool Maximised) CenterOnScreen(
        int width,
        int height,
        ScreenBounds screen)
    {
        int x = screen.X + Math.Max(0, (screen.Width - width) / 2);
        int y = screen.Y + Math.Max(0, (screen.Height - height) / 2);
        return (width, height, x, y, false);
    }

    public void OnWindowStateChanged(int width, int height, int? x, int? y, bool isMaximised)
    {
        if (_disposed) return;

        // Clamp to minimum dimensions
        int clampedWidth = Math.Max(MinWidth, width);
        int clampedHeight = Math.Max(MinHeight, height);

        var state = new WindowSettings
        {
            Width = clampedWidth,
            Height = clampedHeight,
            X = x,
            Y = y,
            Maximised = isMaximised
        };

        lock (_lock)
        {
            _pendingState = state;
            _debounceTimer?.Dispose();
            _debounceTimer = new Timer(_ => Flush(), null, _debounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    public void Flush()
    {
        WindowSettings? toSave = null;
        lock (_lock)
        {
            if (_pendingState != null)
            {
                toSave = _pendingState;
                _pendingState = null;
            }
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }

        if (toSave != null)
        {
            _settingsService.Update(s =>
            {
                s.Window.Width = toSave.Width;
                s.Window.Height = toSave.Height;
                s.Window.X = toSave.X;
                s.Window.Y = toSave.Y;
                s.Window.Maximised = toSave.Maximised;
            });
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Flush();
    }
}
