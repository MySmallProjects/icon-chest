using System.Net;
using System.Security.Cryptography;
using IconChest.Api.Endpoints;
using IconChest.Api.Security;
using IconChest.Api.Services;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Events;
using IconChest.Core.Images;
using IconChest.Core.Logging;
using IconChest.Core.Missing;
using IconChest.Core.Duplicates;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using IconChest.Core.Tags;
using IconChest.Core.Thumbnails;
using IconChest.Core.Trees;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.OpenApi.Models;

// Locate wwwroot directory
string? webRoot = null;
var webRootCandidates = new[]
{
    Path.Combine(AppContext.BaseDirectory, "wwwroot"),
    Path.Combine(Directory.GetCurrentDirectory(), "src", "IconChest.Api", "wwwroot"),
    Path.Combine(Directory.GetCurrentDirectory(), "wwwroot")
};

foreach (var candidate in webRootCandidates)
{
    if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
    {
        webRoot = Path.GetFullPath(candidate);
        break;
    }
}

if (webRoot == null)
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null)
    {
        var candidate = Path.Combine(dir.FullName, "src", "IconChest.Api", "wwwroot");
        if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "index.html")))
        {
            webRoot = Path.GetFullPath(candidate);
            break;
        }
        dir = dir.Parent;
    }
}

var builderOptions = new WebApplicationOptions
{
    Args = args,
    WebRootPath = webRoot
};

var builder = WebApplication.CreateBuilder(builderOptions);

// 1. Parse command line options
int? cliPort = null;
int? parentPid = null;
bool enableSwaggerCli = false;

for (int i = 0; i < args.Length; i++)
{
    if (args[i].Equals("--port", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out var p))
    {
        cliPort = p;
        i++;
    }
    else if (args[i].Equals("--parent-pid", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
    {
        parentPid = pid;
        i++;
    }
    else if (args[i].Equals("--swagger", StringComparison.OrdinalIgnoreCase))
    {
        enableSwaggerCli = true;
    }
}

// 2. Secret resolution
string? sessionSecret = Environment.GetEnvironmentVariable("ICONCHEST_API_SECRET");
if (string.IsNullOrWhiteSpace(sessionSecret))
{
    byte[] randBytes = RandomNumberGenerator.GetBytes(32);
    sessionSecret = Convert.ToBase64String(randBytes)
        .Replace("+", "-")
        .Replace("/", "_")
        .TrimEnd('=');

    // Print to console only, never to log file
    Console.WriteLine($"IconChest Session Secret: {sessionSecret}");
}

// 3. Core foundations: Paths, Settings, Logging, Database
var appPaths = new AppPaths();
var settingsService = new SettingsService(appPaths);
var appLogging = new AppLogging(LogComponent.Api, appPaths.DefaultLogDirectory, settingsService.Current.LogLevel);

settingsService.SettingsChanged += (_, s) =>
{
    appLogging.SetLevel(s.LogLevel);
};

var database = new IconChestDatabase(settingsService);
database.Migrate();

var connFactory = new DatabaseConnectionFactory(database.DatabasePath);

// 4. Kestrel server binding configuration
var currentSettings = settingsService.Current;
bool bindLan = currentSettings.Api.AllowLan && !string.IsNullOrWhiteSpace(currentSettings.Api.LanPasswordHash);

if (currentSettings.Api.AllowLan && string.IsNullOrWhiteSpace(currentSettings.Api.LanPasswordHash))
{
    var logger = appLogging.LoggerFactory.CreateLogger("IconChest.Api.Startup");
    logger.LogWarning("api.allowLan is true but no LAN password is set. Binding to loopback only for security.");
}

int targetPort = cliPort ?? (currentSettings.Api.PortPreference > 0 ? currentSettings.Api.PortPreference : 0);

builder.WebHost.UseKestrel(kestrel =>
{
    kestrel.Limits.MaxRequestBodySize = 1024 * 1024; // 1 MB limit
    if (bindLan)
    {
        kestrel.ListenAnyIP(targetPort);
    }
    else
    {
        kestrel.Listen(IPAddress.Loopback, targetPort);
    }
});

// 5. Dependency Injection Registration
builder.Services.AddSingleton<ISettingsService>(settingsService);
builder.Services.AddSingleton(appPaths);
builder.Services.AddSingleton<ILogManager>(appLogging);
builder.Services.AddSingleton(appLogging.LoggerFactory);
builder.Services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));

builder.Services.AddSingleton<IIconChestDatabase>(database);
builder.Services.AddSingleton<IDatabaseConnectionFactory>(connFactory);

// Repositories
builder.Services.AddSingleton<ILibraryFolderRepository, LibraryFolderRepository>();
builder.Services.AddSingleton<IImageRepository, ImageRepository>();

// Core Services
builder.Services.AddSingleton<IFileHasher, XxHash128Hasher>();
builder.Services.AddSingleton<IImageMetadataReader, ImageMetadataReader>();
builder.Services.AddSingleton<IThumbnailRenderer, SkiaThumbnailRenderer>();
builder.Services.AddSingleton<IThumbnailCache, ThumbnailCache>();
builder.Services.AddSingleton<IThumbnailQueue, ThumbnailQueue>();
builder.Services.AddSingleton<IThumbnailProvider, ThumbnailProvider>();
builder.Services.AddSingleton<IThumbnailWorker, ThumbnailWorker>();
builder.Services.AddSingleton<ILibraryScanner, LibraryScanner>();
builder.Services.AddSingleton<ILibraryFolderService, LibraryFolderService>();

// Phase 4 Services
builder.Services.AddSingleton<ILibraryEventEmitter, LibraryEventEmitter>();
builder.Services.AddSingleton<IScanJobManager, ScanJobManager>();
builder.Services.AddSingleton<ITagService, TagService>();
builder.Services.AddSingleton<IImageSearchService, ImageSearchService>();
builder.Services.AddSingleton<IImageEditService, ImageEditService>();
builder.Services.AddSingleton<ITreeService, TreeService>();
builder.Services.AddSingleton<IDuplicateService, DuplicateService>();
builder.Services.AddSingleton<IMissingService, MissingService>();

// Security Services
builder.Services.AddSingleton<ISessionManager, SessionManager>();
builder.Services.AddSingleton<ILoginThrottler, LoginThrottler>();

// Hosted Services
builder.Services.AddHostedService<ThumbnailWorkerHostedService>();
builder.Services.AddHostedService<StartupScannerHostedService>();
builder.Services.AddHostedService(sp => new ParentProcessMonitorService(
    parentPid,
    sp.GetRequiredService<IHostApplicationLifetime>(),
    sp.GetRequiredService<ILogger<ParentProcessMonitorService>>()));

// 6. Swagger configuration
bool isDevelopment = builder.Environment.IsDevelopment();
bool enableSwagger = enableSwaggerCli || isDevelopment;

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "IconChest API",
        Version = "v1",
        Description = "Local image library organizer HTTP API"
    });

    c.AddSecurityDefinition("CookieAuth", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Cookie,
        Name = SessionAuthenticationMiddleware.SessionCookieName,
        Description = "Session cookie authentication via iconchest_session"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "CookieAuth"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// 7. Pipeline configuration
app.UseMiddleware<GlobalExceptionHandlerMiddleware>();
app.UseMiddleware<SecurityMiddleware>();
app.UseMiddleware<SessionAuthenticationMiddleware>();

if (enableSwagger)
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "IconChest API v1");
        c.RoutePrefix = "swagger";
    });
}

// Dotfile filter for static files
app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? string.Empty;
    var filename = Path.GetFileName(path);
    if (!string.IsNullOrEmpty(filename) && filename.StartsWith('.') && !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase))
    {
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        return;
    }
    await next();
});

// Static hosting with no-cache on index.html, .js, and .css files
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        var path = ctx.Context.Request.Path.Value ?? string.Empty;
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase) ||
            path.Equals("/", StringComparison.Ordinal) ||
            path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".js", StringComparison.OrdinalIgnoreCase) ||
            path.EndsWith(".css", StringComparison.OrdinalIgnoreCase))
        {
            ctx.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});

// Map Endpoints
int effectivePort = targetPort;
app.MapSystemEndpoints(effectivePort, database);
app.MapSessionEndpoints(sessionSecret);
app.MapFolderEndpoints();
app.MapTreeEndpoints();
app.MapTagEndpoints();
app.MapImageEndpoints();
app.MapQueryEndpoints();
app.MapDuplicateEndpoints();
app.MapMissingEndpoints();
app.MapThumbnailEndpoints();
app.MapEventEndpoints();

// SPA fallback for non-/api paths
app.MapFallbackToFile("index.html", new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
    }
});

// 8. Readiness line
app.Lifetime.ApplicationStarted.Register(() =>
{
    var server = app.Services.GetRequiredService<IServer>();
    var addressFeature = server.Features.Get<IServerAddressesFeature>();
    var boundAddress = addressFeature?.Addresses.FirstOrDefault();

    int actualPort = targetPort;
    if (!string.IsNullOrEmpty(boundAddress) && Uri.TryCreate(boundAddress, UriKind.Absolute, out var uri))
    {
        actualPort = uri.Port;
    }

    Console.WriteLine($"ICONCHEST_READY http://127.0.0.1:{actualPort}");
    Console.Out.Flush();
});

app.Run();

public partial class Program { }
