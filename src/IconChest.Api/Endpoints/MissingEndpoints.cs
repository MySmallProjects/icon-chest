using System.Text.Json.Serialization;
using IconChest.Core.Missing;
using IconChest.Core.Scanning;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed record RemoveMissingRequest(
    [property: JsonPropertyName("ids")] IReadOnlyList<long>? Ids = null,
    [property: JsonPropertyName("folderId")] long? FolderId = null);

public static class MissingEndpoints
{
    public static void MapMissingEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/missing").WithTags("Missing Files");

        // 1. GET /api/missing
        group.MapGet("/", (
            IMissingService missingService,
            int? offset,
            int? limit,
            string? sort,
            string? dir) =>
        {
            var result = missingService.GetMissing(offset ?? 0, limit ?? 50, sort, dir);
            return Results.Ok(new
            {
                total = result.Total,
                offset = result.Offset,
                limit = result.Limit,
                items = result.Items
            });
        })
        .WithName("GetMissingImages")
        .WithSummary("Lists missing images with their last seen timestamp");

        // 2. POST /api/missing/remove
        group.MapPost("/remove", (
            RemoveMissingRequest? request,
            IMissingService missingService) =>
        {
            int count = missingService.RemoveMissing(request?.Ids, request?.FolderId);
            return Results.Ok(new
            {
                removedCount = count
            });
        })
        .WithName("RemoveMissingImages")
        .WithSummary("Deletes records for missing images and purges orphaned thumbnails");

        // 3. POST /api/missing/rescan
        group.MapPost("/rescan", (
            IMissingService missingService,
            IScanJobManager scanJobManager) =>
        {
            var folderIds = missingService.GetFolderIdsWithMissing();
            var startedJobs = new List<object>();

            foreach (var fid in folderIds)
            {
                try
                {
                    var job = scanJobManager.StartScan(fid);
                    startedJobs.Add(new
                    {
                        jobId = job.JobId,
                        folderId = job.FolderId,
                        status = job.Status.ToString().ToLowerInvariant()
                    });
                }
                catch (Exception)
                {
                    // Scan already running or folder error -> skip
                }
            }

            return Results.Ok(new
            {
                startedCount = startedJobs.Count,
                jobs = startedJobs
            });
        })
        .WithName("RescanMissingFolders")
        .WithSummary("Triggers rescanning for all library folders containing missing images");
    }
}
