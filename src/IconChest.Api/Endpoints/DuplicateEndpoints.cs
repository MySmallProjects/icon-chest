using IconChest.Core.Duplicates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public static class DuplicateEndpoints
{
    public static void MapDuplicateEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/duplicates").WithTags("Duplicates");

        // 1. GET /api/duplicates/summary
        group.MapGet("/summary", (
            IDuplicateService duplicateService,
            string? sort,
            string? dir) =>
        {
            var summary = duplicateService.GetSummary(sort, dir);
            return Results.Ok(new
            {
                groupCount = summary.GroupCount,
                imageCount = summary.ImageCount,
                reclaimableBytes = summary.ReclaimableBytes,
                groupSizes = summary.GroupSizes
            });
        })
        .WithName("GetDuplicatesSummary")
        .WithSummary("Returns duplicate group count, total files, reclaimable bytes, and group sizes in requested sort order");

        // 2. GET /api/duplicates
        group.MapGet("/", (
            IDuplicateService duplicateService,
            int? offset,
            int? limit,
            string? sort,
            string? dir) =>
        {
            var result = duplicateService.GetDuplicates(offset ?? 0, limit ?? 50, sort, dir);
            return Results.Ok(new
            {
                totalGroups = result.TotalGroups,
                offset = result.Offset,
                limit = result.Limit,
                groups = result.Groups
            });
        })
        .WithName("GetDuplicates")
        .WithSummary("Lists duplicate image groups excluding hidden and missing files");

        // 3. GET /api/duplicates/ids
        group.MapGet("/ids", (
            IDuplicateService duplicateService,
            string? sort,
            string? dir,
            int? limit) =>
        {
            var ids = duplicateService.GetDuplicateImageIds(sort, dir, limit ?? 200_000);
            return Results.Ok(ids);
        })
        .WithName("GetDuplicateIds")
        .WithSummary("Lists image IDs of duplicate copies in display order");
    }
}
