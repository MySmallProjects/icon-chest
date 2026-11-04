using System.Text.Json.Serialization;
using IconChest.Api.Security;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Native;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed class OpenFileRequest
{
    [JsonPropertyName("imageId")]
    public long ImageId { get; set; }
}

public sealed class OpenFolderRequest
{
    [JsonPropertyName("target")]
    public string? Target { get; set; }
}

public static class NativeEndpoints
{
    public static void MapNativeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/native").WithTags("Native");

        group.MapPost("/open-file", (
            HttpContext context,
            OpenFileRequest request,
            IImageRepository imageRepository,
            INativeLauncher launcher) =>
        {
            if (!context.IsLoopback())
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Native file opening is only allowed from local loopback requests.");
            }

            var img = imageRepository.GetById(request.ImageId);
            if (img == null || img.IsMissing || !File.Exists(img.Path))
            {
                return EndpointHelpers.NotFound(context, $"File for image ID {request.ImageId} was not found on disk.");
            }

            try
            {
                launcher.OpenFile(img.Path);
                return Results.Ok(new
                {
                    status = "opened",
                    imageId = img.Id,
                    path = img.Path
                });
            }
            catch (Exception ex)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Launch Error",
                    $"Failed to launch file with default application: {ex.Message}");
            }
        })
        .WithName("OpenImageInNativeViewer")
        .WithSummary("Opens the image file in the host operating system's default viewer (loopback only)");

        group.MapPost("/open-folder", (
            HttpContext context,
            OpenFolderRequest? request,
            IconChest.Core.Settings.ISettingsService settingsService,
            INativeLauncher launcher) =>
        {
            if (!context.IsLoopback())
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Native folder opening is only allowed from local loopback requests.");
            }

            string? targetDir = request?.Target?.Trim().ToLowerInvariant() switch
            {
                "logs" => settingsService.Paths.DefaultLogDirectory,
                "backups" => !string.IsNullOrWhiteSpace(settingsService.Current.BackupFolder) ? settingsService.Current.BackupFolder : settingsService.Paths.DefaultBackupDirectory,
                "thumbnails" => !string.IsNullOrWhiteSpace(settingsService.Current.ThumbnailCachePath) ? settingsService.Current.ThumbnailCachePath : settingsService.Paths.DefaultThumbnailCachePath,
                "data" => settingsService.Paths.DataDirectory,
                _ => null
            };

            if (targetDir == null)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Target must be one of: logs, backups, thumbnails, data.");
            }

            try
            {
                Directory.CreateDirectory(targetDir);
                launcher.OpenFile(targetDir);
                return Results.Ok(new
                {
                    status = "opened",
                    target = request!.Target,
                    path = targetDir
                });
            }
            catch (Exception ex)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status500InternalServerError,
                    "Launch Error",
                    $"Failed to open folder: {ex.Message}");
            }
        })
        .WithName("OpenFolderInNativeExplorer")
        .WithSummary("Opens a whitelisted system folder in the native file manager (loopback only)");
    }
}

