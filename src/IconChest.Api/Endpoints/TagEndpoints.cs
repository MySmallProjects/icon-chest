using System.Text.Json.Serialization;
using IconChest.Core.Events;
using IconChest.Core.Tags;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed record CreateTagRequest(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("fgColor")] string? FgColor = null,
    [property: JsonPropertyName("bgColor")] string? BgColor = null,
    [property: JsonPropertyName("color")] string? Color = null);

public sealed record UpdateTagRequest(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("fgColor")] string? FgColor = null,
    [property: JsonPropertyName("bgColor")] string? BgColor = null,
    [property: JsonPropertyName("color")] string? Color = null);

public static class TagEndpoints
{
    private static bool IsLightColor(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return false;
        var s = hex.Trim().TrimStart('#');
        if (s.Length == 3) s = $"{s[0]}{s[0]}{s[1]}{s[1]}{s[2]}{s[2]}";
        if (s.Length != 6) return false;
        if (!int.TryParse(s.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, null, out var r)) return false;
        if (!int.TryParse(s.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, null, out var g)) return false;
        if (!int.TryParse(s.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, null, out var b)) return false;
        double lum = (0.299 * r + 0.587 * g + 0.114 * b) / 255.0;
        return lum > 0.5;
    }

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
                color = t.BgColor,
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
                string? targetBg = request.BgColor ?? request.Color;
                string? targetFg = request.FgColor;
                if (targetFg == null && targetBg != null)
                {
                    targetFg = IsLightColor(targetBg) ? "#111827" : "#FFFFFF";
                }

                var created = tagService.Create(request.Name, targetFg, targetBg);
                eventEmitter.EmitLibraryChanged();

                return Results.Created($"/api/tags/{created.Id}", new
                {
                    id = created.Id,
                    name = created.Name,
                    color = created.BgColor,
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
                color = tag.BgColor,
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
                string? targetBg = request.BgColor ?? request.Color;
                string? targetFg = request.FgColor;
                if (targetFg == null && targetBg != null)
                {
                    targetFg = IsLightColor(targetBg) ? "#111827" : "#FFFFFF";
                }

                var updated = tagService.Update(id, request.Name, targetFg, targetBg);
                eventEmitter.EmitLibraryChanged();

                return Results.Ok(new
                {
                    id = updated.Id,
                    name = updated.Name,
                    color = updated.BgColor,
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

