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

        group.MapGet("/", (
            IDuplicateService duplicateService,
            int? offset,
            int? limit) =>
        {
            var result = duplicateService.GetDuplicates(offset ?? 0, limit ?? 50);
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
    }
}

