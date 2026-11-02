using System.Text.Json.Serialization;
using IconChest.Core.Thumbnails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed record ClearThumbnailsRequest(
    [property: JsonPropertyName("confirm")] bool Confirm);

public static class ThumbnailEndpoints
{
    public static void MapThumbnailEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/thumbnails").WithTags("Thumbnails");

        // 1. GET /api/thumbnails/status
        group.MapGet("/status", (
            IThumbnailQueue queue,
            IThumbnailCache cache,
            Core.Settings.ISettingsService settingsService) =>
        {
            var st = queue.GetStatus();
            var maxMb = settingsService.Current.ThumbnailCacheMaxMb;
            var currentBytes = cache.GetTotalBytes();

            return Results.Ok(new
            {
                total = st.TotalJobs,
                pending = st.Pending,
                running = st.Running,
                done = st.Done,
                failed = st.Failed,
                evicted = st.Evicted,
                orphaned = st.Orphaned,
                count64 = st.CountsBySize.GetValueOrDefault(64),
                count128 = st.CountsBySize.GetValueOrDefault(128),
                count256 = st.CountsBySize.GetValueOrDefault(256),
                cacheCurrentBytes = currentBytes,
                cacheMaxMb = maxMb
            });
        })
        .WithName("GetThumbnailQueueStatus")
        .WithSummary("Gets the current status of thumbnail queue and cache disk usage");

        // 2. POST /api/thumbnails/retry-failed
        group.MapPost("/retry-failed", (
            IThumbnailQueue queue,
            IThumbnailWorker worker) =>
        {
            int retried = queue.RetryFailed();
            if (retried > 0)
            {
                worker.NotifyWorkAvailable();
            }

            return Results.Ok(new
            {
                retriedCount = retried
            });
        })
        .WithName("RetryFailedThumbnails")
        .WithSummary("Resets failed thumbnail jobs back to pending");

        // 3. POST /api/thumbnails/verify
        group.MapPost("/verify", async (
            IThumbnailProvider provider,
            CancellationToken ct) =>
        {
            var report = await provider.VerifyCacheAsync(ct);
            return Results.Ok(new
            {
                totalDoneChecked = report.TotalChecked,
                validOnDisk = report.ValidDone,
                restoredToPending = report.MissingFileRestoredToPending,
                staleDeleted = report.StaleFilesDeleted
            });
        })
        .WithName("VerifyThumbnailCache")
        .WithSummary("Verifies all database-marked done thumbnails against cache files on disk");

        // 4. POST /api/thumbnails/clear
        group.MapPost("/clear", async (
            HttpContext context,
            ClearThumbnailsRequest? request,
            IThumbnailProvider provider,
            CancellationToken ct) =>
        {
            if (request == null || !request.Confirm)
            {
                return EndpointHelpers.ValidationError(context, "confirm", "The confirm flag must be true to clear the thumbnail cache.");
            }

            await provider.ClearCacheAsync(reEnqueueAll: false, ct);
            return Results.Ok(new
            {
                status = "cache_cleared"
            });
        })
        .WithName("ClearThumbnailCache")
        .WithSummary("Clears all cached thumbnail files and resets queue records (requires confirmation)");
    }
}
