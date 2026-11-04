using System.Text.Json.Serialization;
using IconChest.Api.Security;
using IconChest.Core.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed record BackupRequest(
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("overwrite")] bool? Overwrite = null);

public sealed record RestoreInspectRequest(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("path")] string? Path = null);

public sealed record RestoreRequest(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("path")] string? Path = null,
    [property: JsonPropertyName("confirm")] bool Confirm = false);

public sealed record MoveDbRequest(
    [property: JsonPropertyName("folder")] string Folder,
    [property: JsonPropertyName("deleteOld")] bool? DeleteOld = null);

public static class DataEndpoints
{
    public static void MapDataEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/data").WithTags("Data Maintenance");

        // 1. GET /api/data/info
        group.MapGet("/info", (IDataMaintenanceService maintenanceService) =>
        {
            var info = maintenanceService.GetInfo();
            return Results.Ok(info);
        })
        .WithName("GetDataInfo")
        .WithSummary("Gets database metrics, schema version, record counts, and list of backups");

        // 2. POST /api/data/backup
        group.MapPost("/backup", async (
            HttpContext context,
            BackupRequest? request,
            IDataMaintenanceService maintenanceService,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(request?.Path))
            {
                if (!context.IsLoopback())
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Custom backup paths are only permitted from local loopback requests.");
                }
            }

            try
            {
                var result = await maintenanceService.BackupAsync(request?.Path, request?.Overwrite ?? false, ct);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Backup Failed",
                    ex.Message);
            }
        })
        .WithName("CreateDatabaseBackup")
        .WithSummary("Creates an online SQLite database backup");

        // 3. GET /api/data/backups/{name}/download
        group.MapGet("/backups/{name}/download", (
            HttpContext context,
            string name,
            IDataMaintenanceService maintenanceService) =>
        {
            if (string.IsNullOrWhiteSpace(name) ||
                name != Path.GetFileName(name) ||
                name.Contains("..") ||
                (!name.EndsWith(".db", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".sqlite", StringComparison.OrdinalIgnoreCase)))
            {
                return EndpointHelpers.NotFound(context, "Backup file name is invalid.");
            }

            var info = maintenanceService.GetInfo();
            var fullPath = Path.Combine(info.BackupFolder, name);

            if (!File.Exists(fullPath))
            {
                return EndpointHelpers.NotFound(context, $"Backup '{name}' not found.");
            }

            return Results.File(fullPath, "application/x-sqlite3", name);
        })
        .WithName("DownloadBackup")
        .WithSummary("Streams a named database backup file from the backup folder");

        // 4. POST /api/data/restore/inspect
        group.MapPost("/restore/inspect", (
            HttpContext context,
            RestoreInspectRequest? request,
            IDataMaintenanceService maintenanceService) =>
        {
            string? targetPath = null;
            if (!string.IsNullOrWhiteSpace(request?.Path))
            {
                if (!context.IsLoopback())
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Arbitrary backup file paths can only be inspected from local loopback requests.");
                }
                targetPath = request.Path;
            }
            else if (!string.IsNullOrWhiteSpace(request?.Name))
            {
                var name = request.Name;
                if (name != Path.GetFileName(name) || name.Contains(".."))
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Bad Request",
                        "Invalid backup name.");
                }
                var info = maintenanceService.GetInfo();
                targetPath = Path.Combine(info.BackupFolder, name);
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Either 'name' or 'path' must be provided.");
            }

            try
            {
                var report = maintenanceService.InspectBackup(targetPath);
                return Results.Ok(report);
            }
            catch (Exception ex)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Inspection Failed",
                    ex.Message);
            }
        })
        .WithName("InspectBackupForRestore")
        .WithSummary("Validates a backup database and reports schema version, record counts, and library folder presence");

        // 5. POST /api/data/restore
        group.MapPost("/restore", async (
            HttpContext context,
            RestoreRequest? request,
            IDataMaintenanceService maintenanceService,
            CancellationToken ct) =>
        {
            if (request == null || !request.Confirm)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Database restore requires explicit 'confirm: true'.");
            }

            string? targetPath = null;
            if (!string.IsNullOrWhiteSpace(request.Path))
            {
                if (!context.IsLoopback())
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status403Forbidden,
                        "Forbidden",
                        "Restoring from arbitrary file paths is only permitted from local loopback requests.");
                }
                targetPath = request.Path;
            }
            else if (!string.IsNullOrWhiteSpace(request.Name))
            {
                var name = request.Name;
                if (name != Path.GetFileName(name) || name.Contains(".."))
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Bad Request",
                        "Invalid backup name.");
                }
                var info = maintenanceService.GetInfo();
                targetPath = Path.Combine(info.BackupFolder, name);
            }

            if (string.IsNullOrWhiteSpace(targetPath))
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Either 'name' or 'path' must be provided.");
            }

            try
            {
                var summary = await maintenanceService.RestoreAsync(targetPath, confirm: true, ct);
                return Results.Ok(summary);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("scan", StringComparison.OrdinalIgnoreCase))
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
            catch (Exception ex)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Restore Failed",
                    ex.Message);
            }
        })
        .WithName("RestoreDatabase")
        .WithSummary("Restores database from backup, keeping existing database as .pre-restore copy");

        // 6. POST /api/data/move
        group.MapPost("/move", async (
            HttpContext context,
            MoveDbRequest? request,
            IDataMaintenanceService maintenanceService,
            CancellationToken ct) =>
        {
            if (!context.IsLoopback())
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status403Forbidden,
                    "Forbidden",
                    "Moving the database is only permitted from local loopback requests.");
            }

            if (request == null || string.IsNullOrWhiteSpace(request.Folder))
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Bad Request",
                    "Target folder must be specified.");
            }

            try
            {
                var result = await maintenanceService.MoveAsync(request.Folder, request.DeleteOld ?? false, ct);
                return Results.Ok(result);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("scan", StringComparison.OrdinalIgnoreCase))
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
            catch (Exception ex)
            {
                return EndpointHelpers.ProblemDetails(
                    context,
                    StatusCodes.Status400BadRequest,
                    "Move Failed",
                    ex.Message);
            }
        })
        .WithName("MoveDatabase")
        .WithSummary("Relocates database file to a new folder, verifies counts, updates settings, and renames old file (loopback only)");
    }
}

