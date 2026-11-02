using System.Text.Json.Serialization;
using IconChest.Core.Events;
using IconChest.Core.Scanning;
using IconChest.Core.Scanning.Exceptions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed record AddFolderRequest(
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("includePatterns")] string? IncludePatterns);

public sealed record PatchFolderRequest(
    [property: JsonPropertyName("enabled")] bool Enabled);

public sealed record ScansRequest(
    [property: JsonPropertyName("folderIds")] List<long>? FolderIds);

public static class FolderEndpoints
{
    public static void MapFolderEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api").WithTags("Folders and Scans");

        // 1. GET /api/folders
        group.MapGet("/folders", (ILibraryFolderService folderService) =>
        {
            var folders = folderService.ListFolders();
            var result = folders.Select(f => new
            {
                id = f.Id,
                path = f.Path,
                enabled = f.Enabled,
                lastScanUtc = f.LastScanUtc,
                includePatterns = f.IncludePatterns,
                imageCount = folderService.GetImageCount(f.Id)
            });

            return Results.Ok(result);
        })
        .WithName("GetFolders")
        .WithSummary("Lists library folders with image counts and last scan time");

        // 2. POST /api/folders
        group.MapPost("/folders", (
            HttpContext context,
            AddFolderRequest request,
            ILibraryFolderService folderService) =>
        {
            if (string.IsNullOrWhiteSpace(request.Path))
            {
                return EndpointHelpers.ValidationError(context, "path", "Folder path cannot be empty.");
            }

            try
            {
                var id = folderService.AddFolder(request.Path, request.IncludePatterns);
                var folder = folderService.GetById(id);
                return Results.Created($"/api/folders/{id}", new
                {
                    id = folder!.Id,
                    path = folder.Path,
                    enabled = folder.Enabled,
                    lastScanUtc = folder.LastScanUtc,
                    includePatterns = folder.IncludePatterns,
                    imageCount = 0
                });
            }
            catch (DuplicateFolderException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
            catch (NestedFolderException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
            catch (Exception ex) when (ex is FolderNotFoundException or NotADirectoryException or FolderUnreadableException)
            {
                return EndpointHelpers.ValidationError(context, "path", ex.Message);
            }
        })
        .WithName("AddFolder")
        .WithSummary("Adds a new folder to the library");

        // 3. PATCH /api/folders/{id}
        group.MapPatch("/folders/{id:long}", (
            HttpContext context,
            long id,
            PatchFolderRequest request,
            ILibraryFolderService folderService) =>
        {
            var folder = folderService.GetById(id);
            if (folder == null)
            {
                return EndpointHelpers.NotFound(context, $"Library folder with ID {id} was not found.");
            }

            folderService.SetEnabled(id, request.Enabled);
            var updated = folderService.GetById(id);

            return Results.Ok(new
            {
                id = updated!.Id,
                path = updated.Path,
                enabled = updated.Enabled,
                lastScanUtc = updated.LastScanUtc,
                includePatterns = updated.IncludePatterns,
                imageCount = folderService.GetImageCount(id)
            });
        })
        .WithName("PatchFolder")
        .WithSummary("Enables or disables scanning for a library folder");

        // 4. DELETE /api/folders/{id}
        group.MapDelete("/folders/{id:long}", (
            HttpContext context,
            long id,
            ILibraryFolderService folderService,
            ILibraryEventEmitter eventEmitter) =>
        {
            var folder = folderService.GetById(id);
            if (folder == null)
            {
                return EndpointHelpers.NotFound(context, $"Library folder with ID {id} was not found.");
            }

            folderService.RemoveFolder(id);
            eventEmitter.EmitLibraryChanged();

            return Results.Ok(new { status = "removed", id });
        })
        .WithName("DeleteFolder")
        .WithSummary("Removes a folder and its records from the database without touching disk files");

        // 5. POST /api/folders/{id}/scan
        group.MapPost("/folders/{id:long}/scan", (
            HttpContext context,
            long id,
            ILibraryFolderService folderService,
            IScanJobManager scanJobManager) =>
        {
            var folder = folderService.GetById(id);
            if (folder == null)
            {
                return EndpointHelpers.NotFound(context, $"Library folder with ID {id} was not found.");
            }

            try
            {
                var job = scanJobManager.StartScan(id);
                return Results.Accepted($"/api/scans/{job.JobId}", new
                {
                    jobId = job.JobId,
                    folderId = job.FolderId,
                    status = job.Status.ToString().ToLowerInvariant(),
                    createdUtc = job.CreatedUtc
                });
            }
            catch (ScanAlreadyRunningException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
        })
        .WithName("ScanFolder")
        .WithSummary("Starts a background scan job for a library folder");

        // 6. POST /api/scans
        group.MapPost("/scans", (
            HttpContext context,
            ScansRequest? request,
            ILibraryFolderService folderService,
            IScanJobManager scanJobManager) =>
        {
            List<long> targetFolderIds;
            if (request?.FolderIds != null && request.FolderIds.Count > 0)
            {
                targetFolderIds = request.FolderIds;
            }
            else
            {
                targetFolderIds = folderService.ListFolders().Where(f => f.Enabled).Select(f => f.Id).ToList();
            }

            var jobs = new List<object>();
            foreach (var fid in targetFolderIds)
            {
                try
                {
                    var job = scanJobManager.StartScan(fid);
                    jobs.Add(new
                    {
                        jobId = job.JobId,
                        folderId = job.FolderId,
                        status = job.Status.ToString().ToLowerInvariant()
                    });
                }
                catch (ScanAlreadyRunningException)
                {
                    // If scanning all folders, skip already-running or report conflict if specific folder
                    if (targetFolderIds.Count == 1)
                    {
                        return EndpointHelpers.Conflict(context, $"A scan is already actively running for library folder {fid}.");
                    }
                }
            }

            return Results.Accepted("/api/scans", new { jobs });
        })
        .WithName("StartScans")
        .WithSummary("Starts background scans for specified or all enabled folders");

        // 7. GET /api/scans
        group.MapGet("/scans", (IScanJobManager scanJobManager) =>
        {
            var recent = scanJobManager.GetRecentJobs();
            return Results.Ok(recent.Select(MapJobToDto));
        })
        .WithName("GetScans")
        .WithSummary("Lists recent scan jobs (last 20)");

        // 8. GET /api/scans/{jobId}
        group.MapGet("/scans/{jobId}", (
            HttpContext context,
            string jobId,
            IScanJobManager scanJobManager) =>
        {
            var job = scanJobManager.GetJob(jobId);
            if (job == null)
            {
                return EndpointHelpers.NotFound(context, $"Scan job with ID '{jobId}' was not found.");
            }

            return Results.Ok(MapJobToDto(job));
        })
        .WithName("GetScanJob")
        .WithSummary("Gets details and status of a specific scan job");

        // 9. POST /api/scans/{jobId}/cancel
        group.MapPost("/scans/{jobId}/cancel", (
            HttpContext context,
            string jobId,
            IScanJobManager scanJobManager) =>
        {
            var job = scanJobManager.GetJob(jobId);
            if (job == null)
            {
                return EndpointHelpers.NotFound(context, $"Scan job with ID '{jobId}' was not found.");
            }

            bool cancelled = scanJobManager.CancelJob(jobId);
            return Results.Ok(new
            {
                jobId,
                cancelled,
                status = job.Status.ToString().ToLowerInvariant()
            });
        })
        .WithName("CancelScanJob")
        .WithSummary("Cancels an active scan job");
    }

    private static object MapJobToDto(ScanJobInfo job) => new
    {
        jobId = job.JobId,
        folderId = job.FolderId,
        folderPath = job.FolderPath,
        status = job.Status.ToString().ToLowerInvariant(),
        createdUtc = job.CreatedUtc,
        startedUtc = job.StartedUtc,
        finishedUtc = job.FinishedUtc,
        errorMessage = job.ErrorMessage,
        progress = job.Progress == null ? null : new
        {
            stage = job.Progress.Stage.ToString(),
            filesFound = job.Progress.FilesFound,
            filesProcessed = job.Progress.FilesProcessed,
            newFiles = job.Progress.NewFiles,
            changedFiles = job.Progress.ChangedFiles,
            movedFiles = job.Progress.MovedFiles,
            missingFiles = job.Progress.MissingFiles,
            errorCount = job.Progress.Errors,
            currentFile = job.Progress.CurrentPath
        },
        result = job.Result == null ? null : new
        {
            folderId = job.Result.FolderId,
            folderPath = job.Result.FolderPath,
            durationMs = (long)job.Result.Duration.TotalMilliseconds,
            filesFound = job.Result.FilesFound,
            unchangedFiles = job.Result.UnchangedFiles,
            newFiles = job.Result.NewFiles,
            changedFiles = job.Result.ChangedFiles,
            movedFiles = job.Result.MovedFiles,
            missingFiles = job.Result.MissingFiles,
            duplicateFiles = job.Result.Duplicates,
            errorCount = job.Result.Errors,
            thumbnailsEnqueued = job.Result.ThumbnailsEnqueued,
            isFatalError = job.Result.IsFatalError,
            fatalErrorMessage = job.Result.FatalErrorMessage
        }
    };
}
