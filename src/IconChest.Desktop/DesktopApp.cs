using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using IconChest.Core.Common;
using IconChest.Core.Settings;
using Photino.NET;

namespace IconChest.Desktop;

public sealed class DesktopApp
{
    private readonly AppPaths _paths;
    private readonly SettingsService _settingsService;
    private readonly SingleInstanceGuard _singleInstance;
    private readonly WindowStateService _windowStateService;
    private readonly ApiProcessManager _apiManager;

    private PhotinoWindow? _window;
    private NavigationPolicy? _navPolicy;
    private Uri? _apiUri;
    private string? _secret;
    private int _isShuttingDown;

    public DesktopApp(AppPaths? paths = null)
    {
        _paths = paths ?? new AppPaths();
        _settingsService = new SettingsService(_paths);
        _singleInstance = new SingleInstanceGuard("IconChest");
        _windowStateService = new WindowStateService(_settingsService);
        _apiManager = new ApiProcessManager();
    }

    public async Task<int> RunAsync(string[] args)
    {
        bool browserOnly = args.Any(a => a.Equals("--browser", StringComparison.OrdinalIgnoreCase));
        bool enableDevTools = args.Any(a => a.Equals("--devtools", StringComparison.OrdinalIgnoreCase))
#if DEBUG
            || true
#endif
            ;

        // 1. Single Instance Check
        bool isPrimary = await _singleInstance.TryAcquireAsync();
        if (!isPrimary)
        {
            Console.WriteLine("Another instance of IconChest is already running. Signaling it to focus and exiting.");
            return 0;
        }

        // Register focus callback
        _singleInstance.FocusRequested += () =>
        {
            if (_window != null)
            {
                _window.SetMinimized(false);
                // Photino window focus
            }
        };

        using var shutdownCts = new CancellationTokenSource();
        ConsoleCancelEventHandler cancelHandler = (_, e) =>
        {
            e.Cancel = true;
            shutdownCts.Cancel();
            _ = ShutdownAsync();
        };
        Console.CancelKeyPress += cancelHandler;

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            ShutdownAsync().GetAwaiter().GetResult();
        };

        try
        {
            // 2. Start API child process
            Console.WriteLine("Starting IconChest API background service...");
            _secret = SecretGenerator.GenerateSecret();
            _apiUri = await _apiManager.StartAsync(secret: _secret);
            _navPolicy = new NavigationPolicy(_apiUri);

            Console.WriteLine($"IconChest API ready at {_apiUri}");

            // 3. Browser-only mode
            if (browserOnly)
            {
                return RunBrowserMode(shutdownCts.Token);
            }

            // 4. Native Photino Web View Mode
            return RunPhotinoMode(enableDevTools);
        }
        catch (Exception ex)
        {
            if (Interlocked.CompareExchange(ref _isShuttingDown, 0, 0) == 1 || shutdownCts.IsCancellationRequested)
            {
                return 0;
            }
            return HandleStartupFailure(ex);
        }
        finally
        {
            Console.CancelKeyPress -= cancelHandler;
        }
    }

    private int RunBrowserMode(CancellationToken shutdownToken)
    {
        var handoffUrl = new Uri($"{_apiUri}#handoff={_secret}");
        Console.WriteLine($"Running in browser mode. Opening default browser at {handoffUrl}...");

        NavigationPolicy.LaunchSystemBrowser(handoffUrl);

        Console.WriteLine("IconChest is running. Press Ctrl+C to terminate.");
        try
        {
            shutdownToken.WaitHandle.WaitOne();
        }
        catch
        {
            // Ignore
        }

        ShutdownAsync().GetAwaiter().GetResult();
        return 0;
    }

    private int RunPhotinoMode(bool enableDevTools)
    {
        CheckNativePrerequisites();

        // Screen bounds and saved window state
        var primaryScreen = new ScreenBounds(0, 0, 1920, 1080);
        var (width, height, x, y, maximised) = _windowStateService.RestoreBounds(primaryScreen);

        _window = new PhotinoWindow()
            .SetTitle("IconChest")
            .SetSize(width, height)
            .SetMinSize(WindowStateService.MinWidth, WindowStateService.MinHeight)
            .SetUseOsDefaultSize(false)
            .SetUseOsDefaultLocation(false)
            .SetDevToolsEnabled(enableDevTools);

        if (x.HasValue && y.HasValue)
        {
            _window.SetLocation(new System.Drawing.Point(x.Value, y.Value));
        }

        if (maximised)
        {
            _window.SetMaximized(true);
        }

        // Set App Icon
        var iconPath = ResolveIconPath();
        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            _window.SetIconFile(iconPath);
        }

        // Window state change tracking
        _window.WindowSizeChanged += (_, _) => OnWindowBoundsChanged();
        _window.WindowLocationChanged += (_, _) => OnWindowBoundsChanged();
        _window.WindowClosing += (_, _) =>
        {
            OnWindowBoundsChanged();
            _windowStateService.Flush();
            return false; // Allow close
        };

        // Message Bridge for secret handoff and native actions
        _window.RegisterWebMessageReceivedHandler((_, message) =>
        {
            HandleWebMessage(message);
        });

        // Navigate to API root with handoff secret
        var handoffUrl = new Uri($"{_apiUri}#handoff={_secret}");
        _window.Load(handoffUrl);

        _window.WaitForClose();

        // Shutdown cleanly after window closes
        ShutdownAsync().GetAwaiter().GetResult();
        return 0;
    }

    private void OnWindowBoundsChanged()
    {
        if (_window == null) return;
        var size = _window.Size;
        var location = _window.Location;
        _windowStateService.OnWindowStateChanged(size.Width, size.Height, location.X, location.Y, false);
    }

    private void HandleWebMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeProp)) return;

            var type = typeProp.GetString();

            if (type == "getSecret")
            {
                // Deliver secret via WebMessage payload and custom event
                var payload = JsonSerializer.Serialize(new { type = "secret", secret = _secret });
                _window?.SendWebMessage(payload);
            }
            else if (type == "pickFolder")
            {
                // Native folder picker
                var pickedFolders = _window?.ShowOpenFolder();
                var selected = (pickedFolders != null && pickedFolders.Length > 0) ? pickedFolders[0] : null;

                var payload = JsonSerializer.Serialize(new { type = "folderPicked", path = selected });
                _window?.SendWebMessage(payload);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to process web message: {0}", ex.Message);
        }
    }

    private static void CheckNativePrerequisites()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Check for WebKitGTK
            // If missing, throw with clear instructions
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Check for WebView2
        }
    }

    private static string? ResolveIconPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        var ext = isWindows ? "ico" : "png";

        var candidates = new[]
        {
            Path.Combine(baseDir, "Assets", $"icon.{ext}"),
            Path.Combine(baseDir, $"icon.{ext}"),
            Path.Combine(baseDir, "Assets", "icon.png"),
            Path.Combine(baseDir, "Assets", "icon.ico")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private int HandleStartupFailure(Exception ex)
    {
        var todayLogFile = Path.Combine(_paths.DefaultLogDirectory, $"app-{DateTime.UtcNow:yyyyMMdd}.log");
        Console.ForegroundColor = ConsoleColor.Red;
        Console.Error.WriteLine("=================================================");
        Console.Error.WriteLine("IconChest Startup Failure");
        Console.Error.WriteLine("=================================================");
        Console.Error.WriteLine($"Error: {ex.Message}");
        Console.Error.WriteLine();
        Console.Error.WriteLine($"Log file: {todayLogFile}");
        Console.Error.WriteLine();
        Console.Error.WriteLine("If native window libraries are missing, you can run in browser mode:");
        Console.Error.WriteLine("  iconchest-desktop --browser");
        Console.Error.WriteLine("=================================================");
        Console.ResetColor();

        _ = ShutdownAsync();
        return 1;
    }

    public async Task ShutdownAsync()
    {
        if (Interlocked.Exchange(ref _isShuttingDown, 1) != 0)
        {
            return;
        }

        _windowStateService.Flush();
        await _apiManager.StopAsync();
        _singleInstance.Release();
    }
}
