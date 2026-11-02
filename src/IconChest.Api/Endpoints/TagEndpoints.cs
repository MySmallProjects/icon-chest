using System.Text.Json.Serialization;
using IconChest.Core.Events;
using IconChest.Core.Tags;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed record CreateTagRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fgColor")] string? FgColor,
    [property: JsonPropertyName("bgColor")] string? BgColor);

public sealed record UpdateTagRequest(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("fgColor")] string? FgColor,
    [property: JsonPropertyName("bgColor")] string? BgColor);

public static class TagEndpoints
{
    public static void MapTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tags").WithTags("Tags");

        // 1. GET /api/tags
        group.MapGet("/", (ITagService tagService) =>
        {
            var tags = tagService.GetAll();
            return Results.Ok(tags.Select(t => new
            {
                id = t.Id,
                name = t.Name,
                fgColor = t.FgColor,
                bgColor = t.BgColor,
                count = t.ImageCount
            }));
        })
        .WithName("GetTags")
        .WithSummary("Lists all tags sorted alphabetically (case-insensitive) with image counts");

        // 2. POST /api/tags
        group.MapPost("/", (
            HttpContext context,
            CreateTagRequest request,
            ITagService tagService,
            ILibraryEventEmitter eventEmitter) =>
        {
            try
            {
                var created = tagService.Create(request.Name, request.FgColor, request.BgColor);
                eventEmitter.EmitLibraryChanged();

                return Results.Created($"/api/tags/{created.Id}", new
                {
                    id = created.Id,
                    name = created.Name,
                    fgColor = created.FgColor,
                    bgColor = created.BgColor,
                    count = 0
                });
            }
            catch (TagValidationException ex)
            {
                return EndpointHelpers.ValidationError(context, "name", ex.Message);
            }
            catch (TagConflictException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
        })
        .WithName("CreateTag")
        .WithSummary("Creates a new tag");

        // 3. GET /api/tags/{id}
        group.MapGet("/{id:long}", (
            HttpContext context,
            long id,
            ITagService tagService) =>
        {
            var tag = tagService.GetById(id);
            if (tag == null)
            {
                return EndpointHelpers.NotFound(context, $"Tag with ID {id} was not found.");
            }

            return Results.Ok(new
            {
                id = tag.Id,
                name = tag.Name,
                fgColor = tag.FgColor,
                bgColor = tag.BgColor,
                count = tag.ImageCount
            });
        })
        .WithName("GetTag")
        .WithSummary("Gets tag details and image count");

        // 4. PATCH /api/tags/{id}
        group.MapPatch("/{id:long}", (
            HttpContext context,
            long id,
            UpdateTagRequest request,
            ITagService tagService,
            ILibraryEventEmitter eventEmitter) =>
        {
            try
            {
                var updated = tagService.Update(id, request.Name, request.FgColor, request.BgColor);
                eventEmitter.EmitLibraryChanged();

                return Results.Ok(new
                {
                    id = updated.Id,
                    name = updated.Name,
                    fgColor = updated.FgColor,
                    bgColor = updated.BgColor,
                    count = updated.ImageCount
                });
            }
            catch (KeyNotFoundException)
            {
                return EndpointHelpers.NotFound(context, $"Tag with ID {id} was not found.");
            }
            catch (TagValidationException ex)
            {
                return EndpointHelpers.ValidationError(context, "tag", ex.Message);
            }
            catch (TagConflictException ex)
            {
                return EndpointHelpers.Conflict(context, ex.Message);
            }
        })
        .WithName("UpdateTag")
        .WithSummary("Updates tag name or colors");

        // 5. DELETE /api/tags/{id}
        group.MapDelete("/{id:long}", (
            HttpContext context,
            long id,
            ITagService tagService,
            ILibraryEventEmitter eventEmitter) =>
        {
            var tag = tagService.GetById(id);
            if (tag == null)
            {
                return EndpointHelpers.NotFound(context, $"Tag with ID {id} was not found.");
            }

            tagService.Delete(id);
            eventEmitter.EmitLibraryChanged();

            return Results.Ok(new { status = "deleted", id });
        })
        .WithName("DeleteTag")
        .WithSummary("Deletes a tag and unlinks it from all images");
    }
}

