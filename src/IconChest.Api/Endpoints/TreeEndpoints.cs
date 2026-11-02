using IconChest.Core.Trees;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public static class TreeEndpoints
{
    public static void MapTreeEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tree").WithTags("Trees");

        group.MapGet("/folders", (ITreeService treeService) =>
        {
            var tree = treeService.GetFolderTree();
            return Results.Ok(tree);
        })
        .WithName("GetFolderTree")
        .WithSummary("Gets library folder hierarchy tree with direct and recursive file counts");

        group.MapGet("/years", (
            HttpContext context,
            string? field,
            ITreeService treeService) =>
        {
            string dateField = field ?? "effective";
            var tree = treeService.GetYearTree(dateField);
            return Results.Ok(tree);
        })
        .WithName("GetYearTree")
        .WithSummary("Gets year and month grouping tree for specified date field (effective|created|modified|taken|added)");
    }
}

