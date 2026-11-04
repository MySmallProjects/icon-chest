using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using IconChest.Api.Security;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Logging;
using IconChest.Core.Security;
using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using IconChest.Core.Versioning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Hosting;

namespace IconChest.Api.Endpoints;

public sealed record LanPasswordRequest(
    [property: JsonPropertyName("currentPassword")] string? CurrentPassword,
    [property: JsonPropertyName("newPassword")] string NewPassword);

public static class SystemEndpoints
{
    public static void MapSystemEndpoints(
        this IEndpointRouteBuilder app,
        int boundPort,
        IIconChestDatabase database)
    {
        // 1. GET /health (Anonymous)
        app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
            .WithTags("Health")
            .WithSummary("Health check");

        var group = app.MapGroup("/api").WithTags("System");

        // 2. GET /api/system/info
        group.MapGet("/system/info", (
            HttpContext context,
            ISettingsService settingsService) =>
        {
            var versionInfo = new VersionInfo();
            var paths = settingsService.Paths;
            var todayLogFile = Path.Combine(paths.DefaultLogDirectory, $"app-{DateTime.UtcNow:yyyyMMdd}.log");

            var lanUrls = new List<string>();
            if (settingsService.Current.Api.AllowLan)
            {
                try
                {
                    var hostName = System.Net.Dns.GetHostName();
                    var hostEntry = System.Net.Dns.GetHostEntry(hostName);
                    foreach (var ip in hostEntry.AddressList)
                    {
                        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !System.Net.IPAddress.IsLoopback(ip))
                        {
                            lanUrls.Add($"http://{ip}:{boundPort}");
                        }
                    }
                }
                catch
                {
                    // Fallback
                }
            }

            bool canRestart = Environment.GetEnvironmentVariable("ICONCHEST_DESKTOP") == "1" ||
                              Environment.GetEnvironmentVariable("ICONCHEST_CAN_RESTART") == "1" ||
                              !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ICONCHEST_API_SECRET"));

            return Results.Ok(new
            {
                version = versionInfo.Version,
                buildDateUtc = versionInfo.BuildDateUtc?.ToString("o"),
                executablePath = Environment.ProcessPath ?? string.Empty,
                logFilePath = todayLogFile,
                logFolder = paths.DefaultLogDirectory,
                platform = RuntimeInformation.OSDescription,
                apiPort = boundPort,
                dbPath = database.DatabasePath,
                schemaVersion = database.GetSchemaVersion(),
                hashAlgorithm = "xxh128",
                isLocal = context.IsLoopback(),
                lanUrls = lanUrls,
                canRestart = canRestart
            });
        })
        .WithName("GetSystemInfo")
        .WithSummary("System and environment diagnostics info");

        // 3. POST /api/system/shutdown (Loopback only)
        group.MapPost("/system/shutdown", (
            HttpContext context,
            IHostApplicationLifetime appLifetime) =>
        {
            if (!context.IsLoopback())
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Shutdown can only be triggered from loopback connections.");
            }

            _ = Task.Run(async () =>
            {
                await Task.Delay(200);
                appLifetime.StopApplication();
            });

            return Results.Ok(new { status = "shutting_down" });
        })
        .WithName("Shutdown")
        .WithSummary("Initiates graceful shutdown of the API host (loopback only)");

        // 3b. POST /api/system/client-log
        group.MapPost("/system/client-log", (
            JsonElement body,
            ILoggerFactory loggerFactory) =>
        {
            var logger = loggerFactory.CreateLogger("ClientBrowser");
            var msg = body.TryGetProperty("message", out var m) ? m.GetString() : "Unknown client error";
            var stack = body.TryGetProperty("stack", out var st) ? st.GetString() : null;
            if (!string.IsNullOrWhiteSpace(stack))
            {
                logger.LogError("Client browser error: {Message}\n{Stack}", msg, stack);
            }
            else
            {
                logger.LogError("Client browser error: {Message}", msg);
            }
            return Results.Ok();
        })
        .WithName("ClientLog")
        .WithSummary("Logs unhandled client-side errors into the application log file");

        // 4. GET /api/settings
        group.MapGet("/settings", (ISettingsService settingsService) =>
        {
            var s = settingsService.Current;
            return Results.Ok(FormatSettingsResponse(s));
        })
        .WithName("GetSettings")
        .WithSummary("Gets all application settings (hides password hash)");

        // 5. PATCH /api/settings
        group.MapPatch("/settings", (
            HttpContext context,
            JsonElement patchBody,
            ISettingsService settingsService,
            IThumbnailWorker thumbnailWorker) =>
        {
            try
            {
                var (newSettings, restartRequired) = SettingsPatchHelper.ApplyPatch(settingsService.Current, patchBody);

                int oldWorkers = settingsService.Current.ThumbnailWorkers;

                settingsService.Update(s =>
                {
                    s.DbPath = newSettings.DbPath;
                    s.ThumbnailCachePath = newSettings.ThumbnailCachePath;
                    s.ThumbnailCacheMaxMb = newSettings.ThumbnailCacheMaxMb;
                    s.ThumbnailQuality = newSettings.ThumbnailQuality;
                    s.ThumbnailWorkers = newSettings.ThumbnailWorkers;
                    s.LogLevel = newSettings.LogLevel;
                    s.Theme = newSettings.Theme;
                    s.GridCaption = newSettings.GridCaption;
                    s.CheckerboardMode = newSettings.CheckerboardMode;
                    s.PreviewInfoOverlayDefault = newSettings.PreviewInfoOverlayDefault;
                    s.DefaultView = newSettings.DefaultView;
                    s.RememberWindowState = newSettings.RememberWindowState;
                    s.Window = newSettings.Window;
                    s.IncludePatterns = newSettings.IncludePatterns;
                    s.ExcludeNames = newSettings.ExcludeNames;
                    s.ScanOnStartup = newSettings.ScanOnStartup;
                    s.ScanWorkers = newSettings.ScanWorkers;
                    s.AutoRemoveMissing = newSettings.AutoRemoveMissing;
                    s.ConfirmBeforeDelete = newSettings.ConfirmBeforeDelete;
                    s.Api.PortPreference = newSettings.Api.PortPreference;
                    s.Api.AllowLan = newSettings.Api.AllowLan;
                });

                // If thumbnail workers changed, restart workers
                if (oldWorkers != newSettings.ThumbnailWorkers)
                {
                    _ = Task.Run(async () =>
                    {
                        await thumbnailWorker.StopAsync();
                        thumbnailWorker.Start();
                    });
                }

                var s = settingsService.Current;
                return Results.Ok(new
                {
                    settings = FormatSettingsResponse(s),
                    restartRequired = restartRequired
                });
            }
            catch (ArgumentException ex)
            {
                return EndpointHelpers.ValidationError(context, "settings", ex.Message);
            }
        })
        .WithName("PatchSettings")
        .WithSummary("Applies a JSON merge patch to application settings");

        // 6. PUT /api/settings/lan-password
        group.MapPut("/settings/lan-password", (
            HttpContext context,
            LanPasswordRequest request,
            ISettingsService settingsService) =>
        {
            if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 8)
            {
                return EndpointHelpers.ValidationError(context, "newPassword", "LAN password must be at least 8 characters.");
            }

            var currentHash = settingsService.Current.Api.LanPasswordHash;
            if (!string.IsNullOrWhiteSpace(currentHash))
            {
                if (string.IsNullOrWhiteSpace(request.CurrentPassword) ||
                    !PasswordHasher.VerifyPassword(request.CurrentPassword, currentHash))
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Bad Request",
                        "Current password is incorrect.");
                }
            }

            var newHash = PasswordHasher.HashPassword(request.NewPassword);
            settingsService.Update(s => s.Api.LanPasswordHash = newHash);

            return Results.Ok(new
            {
                status = "password_updated",
                hasLanPassword = true
            });
        })
        .WithName("SetLanPassword")
        .WithSummary("Sets or updates the LAN password (requires current password if already set)");

        // 7. GET /api/system/licenses (Authenticated)
        group.MapGet("/system/licenses", () =>
        {
            var assembly = typeof(SystemEndpoints).Assembly;
            var resourceName = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("THIRD-PARTY-NOTICES.md", StringComparison.OrdinalIgnoreCase));

            if (!string.IsNullOrEmpty(resourceName))
            {
                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    return Results.Text(reader.ReadToEnd(), "text/plain; charset=utf-8");
                }
            }

            // Fallback to searching disk paths if running unpacked or during development
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md"),
                Path.Combine(AppContext.BaseDirectory, "docs", "THIRD-PARTY-NOTICES.md"),
                Path.Combine(Directory.GetCurrentDirectory(), "docs", "THIRD-PARTY-NOTICES.md"),
                Path.Combine(Directory.GetCurrentDirectory(), "..", "..", "docs", "THIRD-PARTY-NOTICES.md")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Results.Text(File.ReadAllText(candidate), "text/plain; charset=utf-8");
                }
            }

            return Results.NotFound(new { error = "Third-party notices document not found." });
        })
        .WithName("GetLicenses")
        .WithSummary("Retrieves third-party license notices");
    }

    private static object FormatSettingsResponse(AppSettings s) => new
    {
        version = s.Version,
        dbPath = s.DbPath,
        thumbnailCachePath = s.ThumbnailCachePath,
        thumbnailCacheMaxMb = s.ThumbnailCacheMaxMb,
        thumbnailQuality = s.ThumbnailQuality,
        thumbnailWorkers = s.ThumbnailWorkers,
        logLevel = s.LogLevel.ToString(),
        theme = s.Theme.ToString(),
        gridCaption = s.GridCaption.ToString(),
        checkerboardMode = s.CheckerboardMode.ToString(),
        previewInfoOverlayDefault = s.PreviewInfoOverlayDefault,
        defaultView = s.DefaultView,
        rememberWindowState = s.RememberWindowState,
        window = s.Window,
        includePatterns = s.IncludePatterns,
        excludeNames = s.ExcludeNames,
        scanOnStartup = s.ScanOnStartup,
        scanWorkers = s.ScanWorkers,
        autoRemoveMissing = s.AutoRemoveMissing,
        confirmBeforeDelete = s.ConfirmBeforeDelete,
        appearance = new
        {
            theme = s.Theme.ToString(),
            gridCaption = s.GridCaption.ToString(),
            checkerboardMode = s.CheckerboardMode.ToString(),
            previewInfoOverlayDefault = s.PreviewInfoOverlayDefault
        },
        behaviour = new
        {
            defaultView = s.DefaultView,
            rememberWindowState = s.RememberWindowState,
            confirmBeforeDelete = s.ConfirmBeforeDelete
        },
        scanning = new
        {
            includePatterns = s.IncludePatterns.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            excludeNames = s.ExcludeNames.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            scanOnStartup = s.ScanOnStartup,
            scanWorkers = s.ScanWorkers,
            autoRemoveMissing = s.AutoRemoveMissing
        },
        api = new
        {
            portPreference = s.Api.PortPreference,
            allowLan = s.Api.AllowLan,
            hasLanPassword = !string.IsNullOrWhiteSpace(s.Api.LanPasswordHash)
        }
    };
}
