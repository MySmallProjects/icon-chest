using System.Net.Mime;
using System.Text.Json;
using System.Text.Json.Serialization;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Images;
using IconChest.Core.Tags;
using IconChest.Core.Thumbnails;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace IconChest.Api.Endpoints;

public sealed class BulkEditPayload
{
    [JsonPropertyName("ids")]
    public List<long> Ids { get; set; } = new();

    [JsonPropertyName("changes")]
    public JsonElement Changes { get; set; }
}

public static class ImageEndpoints
{
    private static readonly HashSet<int> AllowedThumbnailSizes = new() { 64, 128, 256 };

    public static void MapImageEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/images").WithTags("Images");

        // 1. GET /api/images
        group.MapGet("/", (
            HttpContext context,
            IImageSearchService searchService,
            long? folderId,
            string? directory,
            bool? includeSubfolders,
            string? dateField,
            int? year,
            int? month,
            string? dateFrom,
            string? dateTo,
            string? tagMode,
            int? minWidth,
            int? maxWidth,
            int? minHeight,
            int? maxHeight,
            long? minSize,
            long? maxSize,
            int? ratingMin,
            bool? favourite,
            string? text,
            string? hidden,
            string? missing,
            string? duplicate,
            string? sort,
            string? dir,
            int? offset,
            int? limit) =>
        {
            var query = context.Request.Query;

            // Check reserved parameter 'q'
            if (query.ContainsKey("q"))
            {
                var qVal = query["q"].ToString();
                // If any other filter was also passed
                bool hasOtherFilters = query.Keys.Any(k =>
                    !k.Equals("q", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("limit", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("offset", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("sort", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("dir", StringComparison.OrdinalIgnoreCase));

                if (hasOtherFilters)
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Bad Request",
                        "The 'q' parameter cannot be combined with structured filter parameters.");
                }

                var compResult = IconChest.Core.Query.QueryEngine.ParseAndCompile(qVal);
                if (!compResult.IsValid)
                {
                    return EndpointHelpers.QueryProblemDetails(context, compResult.Errors);
                }

                var qRequest = new ImageSearchRequest
                {
                    Sort = sort ?? "filename",
                    Dir = dir ?? "asc",
                    Offset = offset ?? 0,
                    Limit = limit ?? 50,
                    CustomWhereClause = compResult.Query!.WhereSql,
                    CustomWhereParameters = compResult.Query.Parameters.ToDictionary(k => k.Key, v => v.Value),
                    Hidden = InclusionState.Include,
                    Missing = InclusionState.Include,
                    Duplicate = InclusionState.Include
                };

                var qResult = searchService.Search(qRequest);
                return Results.Ok(qResult);
            }

            var tagsList = query["tag"].Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
            var typesList = query["type"].Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();

            var request = new ImageSearchRequest
            {
                FolderId = folderId,
                Directory = directory,
                IncludeSubfolders = includeSubfolders ?? false,
                DateField = dateField ?? "effective",
                Year = year,
                Month = month,
                DateFrom = dateFrom,
                DateTo = dateTo,
                Tags = tagsList.Count > 0 ? tagsList : null,
                TagMode = ParseTagMode(tagMode),
                Types = typesList.Count > 0 ? typesList : null,
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                MinHeight = minHeight,
                MaxHeight = maxHeight,
                MinSize = minSize,
                MaxSize = maxSize,
                RatingMin = ratingMin,
                Favourite = favourite,
                Text = text,
                Hidden = ParseInclusion(hidden, InclusionState.Exclude),
                Missing = ParseInclusion(missing, InclusionState.Exclude),
                Duplicate = ParseInclusion(duplicate, InclusionState.Include),
                Sort = sort ?? "filename",
                Dir = dir ?? "asc",
                Offset = offset ?? 0,
                Limit = limit ?? 50
            };

            var searchResult = searchService.Search(request);
            return Results.Ok(new
            {
                total = searchResult.Total,
                offset = searchResult.Offset,
                limit = searchResult.Limit,
                items = searchResult.Items
            });
        })
        .WithName("SearchImages")
        .WithSummary("Lists images with structured filters, sorting and paging");

        // 2. GET /api/images/ids
        group.MapGet("/ids", (
            HttpContext context,
            IImageSearchService searchService,
            long? folderId,
            string? directory,
            bool? includeSubfolders,
            string? dateField,
            int? year,
            int? month,
            string? dateFrom,
            string? dateTo,
            string? tagMode,
            int? minWidth,
            int? maxWidth,
            int? minHeight,
            int? maxHeight,
            long? minSize,
            long? maxSize,
            int? ratingMin,
            bool? favourite,
            string? text,
            string? hidden,
            string? missing,
            string? duplicate,
            string? sort,
            string? dir,
            int? offset,
            int? limit) =>
        {
            var query = context.Request.Query;

            if (query.ContainsKey("q"))
            {
                var qVal = query["q"].ToString();
                bool hasOtherFilters = query.Keys.Any(k =>
                    !k.Equals("q", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("limit", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("offset", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("sort", StringComparison.OrdinalIgnoreCase) &&
                    !k.Equals("dir", StringComparison.OrdinalIgnoreCase));

                if (hasOtherFilters)
                {
                    return EndpointHelpers.ProblemDetails(
                        context,
                        StatusCodes.Status400BadRequest,
                        "Bad Request",
                        "The 'q' parameter cannot be combined with structured filter parameters.");
                }

                var compResult = IconChest.Core.Query.QueryEngine.ParseAndCompile(qVal);
                if (!compResult.IsValid)
                {
                    return EndpointHelpers.QueryProblemDetails(context, compResult.Errors);
                }

                int qMaxIds = limit ?? 200_000;
                var qRequest = new ImageSearchRequest
                {
                    Sort = sort ?? "filename",
                    Dir = dir ?? "asc",
                    Offset = offset ?? 0,
                    Limit = qMaxIds,
                    CustomWhereClause = compResult.Query!.WhereSql,
                    CustomWhereParameters = compResult.Query.Parameters.ToDictionary(k => k.Key, v => v.Value),
                    Hidden = InclusionState.Include,
                    Missing = InclusionState.Include,
                    Duplicate = InclusionState.Include
                };

                var qIds = searchService.GetIds(qRequest, qMaxIds);
                return Results.Ok(qIds);
            }

            var tagsList = query["tag"].Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();
            var typesList = query["type"].Where(s => !string.IsNullOrEmpty(s)).Select(s => s!).ToList();

            var request = new ImageSearchRequest
            {
                FolderId = folderId,
                Directory = directory,
                IncludeSubfolders = includeSubfolders ?? false,
                DateField = dateField ?? "effective",
                Year = year,
                Month = month,
                DateFrom = dateFrom,
                DateTo = dateTo,
                Tags = tagsList.Count > 0 ? tagsList : null,
                TagMode = ParseTagMode(tagMode),
                Types = typesList.Count > 0 ? typesList : null,
                MinWidth = minWidth,
                MaxWidth = maxWidth,
                MinHeight = minHeight,
                MaxHeight = maxHeight,
                MinSize = minSize,
                MaxSize = maxSize,
                RatingMin = ratingMin,
                Favourite = favourite,
                Text = text,
                Hidden = ParseInclusion(hidden, InclusionState.Exclude),
                Missing = ParseInclusion(missing, InclusionState.Exclude),
                Duplicate = ParseInclusion(duplicate, InclusionState.Include),
                Sort = sort ?? "filename",
                Dir = dir ?? "asc",
                Offset = offset ?? 0
            };

            int maxIds = limit ?? 200_000;
            var ids = searchService.GetIds(request, maxIds);
            return Results.Ok(ids);
        })
        .WithName("GetImageIds")
        .WithSummary("Returns only matching image IDs for select-all / shift-range operations (capped at 200,000)");

        // 3. GET /api/images/{id}
        group.MapGet("/{id:long}", (
            HttpContext context,
            long id,
            IImageRepository imageRepository,
            ITagService tagService) =>
        {
            var img = imageRepository.GetById(id);
            if (img == null)
            {
                return EndpointHelpers.NotFound(context, $"Image with ID {id} was not found.");
            }

            // Fetch tags for this image
            var allTags = tagService.GetAll();
            var imgTagIds = imageRepository.GetTagIdsForImage(id);
            var imageTags = allTags.Where(t => imgTagIds.Contains(t.Id)).Select(t => new
            {
                id = t.Id,
                name = t.Name,
                fgColor = t.FgColor,
                bgColor = t.BgColor
            }).ToList();

            return Results.Ok(new
            {
                id = img.Id,
                folderId = img.FolderId,
                path = img.Path,
                pathKey = img.PathKey,
                directory = img.DirectoryPath,
                directoryKey = img.DirectoryKey,
                fileName = img.FileName,
                extension = img.Extension,
                format = img.Format,
                width = img.Width,
                height = img.Height,
                sizeBytes = img.SizeBytes,
                hash = img.Hash,
                hashAlgo = img.HashAlgo,
                createdUtc = img.CreatedUtc,
                modifiedUtc = img.ModifiedUtc,
                exifDateUtc = img.ExifDateUtc,
                takenOverrideUtc = img.TakenOverrideUtc,
                effectiveDateUtc = img.EffectiveDateUtc,
                addedUtc = img.AddedUtc,
                title = img.Title,
                description = img.Description,
                rating = img.Rating,
                isFavourite = img.IsFavourite,
                isHidden = img.IsHidden,
                isMissing = img.IsMissing,
                missingSinceUtc = img.MissingSinceUtc,
                isDuplicate = img.IsDuplicate,
                hasTransparency = img.HasTransparency,
                status = img.Status,
                exifJson = img.ExifJson,
                tags = imageTags
            });
        })
        .WithName("GetImageById")
        .WithSummary("Gets full metadata and details for a single image");

        // 4. PATCH /api/images (Bulk edit)
        group.MapPatch("/", (
            HttpContext context,
            BulkEditPayload payload,
            IImageEditService editService) =>
        {
            if (payload.Ids == null || payload.Ids.Count == 0)
            {
                return EndpointHelpers.ValidationError(context, "ids", "At least one image ID must be provided.");
            }

            if (payload.Ids.Count > 10_000)
            {
                return EndpointHelpers.ValidationError(context, "ids", "Cannot edit more than 10,000 images in a single request.");
            }

            var changes = ParseChanges(payload.Changes);

            try
            {
                editService.EditImages(payload.Ids, changes);
                return Results.Ok(new
                {
                    status = "updated",
                    count = payload.Ids.Count
                });
            }
            catch (KeyNotFoundException ex)
            {
                return EndpointHelpers.NotFound(context, ex.Message);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return EndpointHelpers.ValidationError(context, "rating", ex.Message);
            }
        })
        .WithName("BulkEditImages")
        .WithSummary("Applies bulk metadata edits across specified images with presence-vs-null semantics");

        // 5. GET /api/images/{id}/thumbnail?size=64|128|256
        group.MapGet("/{id:long}/thumbnail", async (
            HttpContext context,
            long id,
            int? size,
            IThumbnailProvider thumbnailProvider,
            IThumbnailQueue thumbnailQueue,
            IThumbnailWorker thumbnailWorker,
            CancellationToken ct) =>
        {
            int targetSize = size ?? 128;
            if (!AllowedThumbnailSizes.Contains(targetSize))
            {
                return EndpointHelpers.ValidationError(context, "size", "Size must be one of 64, 128, or 256.");
            }

            var thumbResult = await thumbnailProvider.GetAsync(id, targetSize, ct);

            return thumbResult.Status switch
            {
                ThumbnailResultStatus.NotFound => EndpointHelpers.NotFound(context, $"Image with ID {id} was not found."),

                ThumbnailResultStatus.Ready => HandleThumbnailReady(context, thumbResult),

                ThumbnailResultStatus.Pending => HandleThumbnailPending(context, id, targetSize, thumbnailQueue, thumbnailWorker),

                ThumbnailResultStatus.Failed => HandleThumbnailFailed(context, thumbResult),

                _ => EndpointHelpers.NotFound(context, "Thumbnail not available.")
            };
        })
        .WithName("GetImageThumbnail")
        .WithSummary("Serves an image thumbnail with ETag/304 caching or reports pending/failed status");

        // 6. GET /api/images/{id}/file
        group.MapGet("/{id:long}/file", async (
            HttpContext context,
            long id,
            IImageRepository imageRepository) =>
        {
            var img = imageRepository.GetById(id);
            if (img == null || img.IsMissing || !File.Exists(img.Path))
            {
                return EndpointHelpers.NotFound(context, $"File for image ID {id} was not found.");
            }

            var fileInfo = new FileInfo(img.Path);
            string contentType = ResolveContentType(img.Format, img.Extension);

            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers.ContentDisposition = "inline";

            // If SVG: apply strict sandbox CSP
            if (img.Format.Equals("svg", StringComparison.OrdinalIgnoreCase) ||
                img.Extension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.Headers["Content-Security-Policy"] =
                    "default-src 'none'; style-src 'unsafe-inline'; img-src data:; sandbox";
            }

            // Results.File automatically handles Range requests, ETag, Last-Modified
            return Results.File(
                img.Path,
                contentType: contentType,
                enableRangeProcessing: true,
                lastModified: fileInfo.LastWriteTimeUtc,
                entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{img.Hash}\""));
        })
        .WithName("GetOriginalFile")
        .WithSummary("Streams the original image file with HTTP range and ETag support");
    }

    private static IResult HandleThumbnailReady(HttpContext context, ThumbnailResult result)
    {
        string rawEtag = result.ETag ?? string.Empty;
        var entityTag = rawEtag.StartsWith('"')
            ? new Microsoft.Net.Http.Headers.EntityTagHeaderValue(rawEtag)
            : new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{rawEtag}\"");

        context.Response.Headers.CacheControl = "private, max-age=86400";
        return Results.File(result.FilePath!, "image/webp", entityTag: entityTag);
    }

    private static IResult HandleThumbnailPending(
        HttpContext context,
        long imageId,
        int size,
        IThumbnailQueue queue,
        IThumbnailWorker worker)
    {
        // Raise priority for requested thumbnail
        var records = queue.GetRecordsForImage(imageId);
        var rec = records.FirstOrDefault(r => r.Size == size);
        if (rec != null)
        {
            queue.Prioritize(rec.Hash, size);
            worker.NotifyWorkAvailable();
        }

        context.Response.Headers["X-Thumbnail-State"] = "pending";
        context.Response.Headers.RetryAfter = "2";
        context.Response.Headers.CacheControl = "no-store";

        return Results.StatusCode(StatusCodes.Status202Accepted);
    }

    private static IResult HandleThumbnailFailed(HttpContext context, ThumbnailResult result)
    {
        context.Response.Headers["X-Thumbnail-State"] = "failed";
        if (!string.IsNullOrWhiteSpace(result.Error))
        {
            context.Response.Headers["X-Thumbnail-Error"] = result.Error;
        }

        return EndpointHelpers.ProblemDetails(
            context,
            StatusCodes.Status404NotFound,
            "Thumbnail Generation Failed",
            result.Error ?? "Thumbnail failed to generate.");
    }

    private static string ResolveContentType(string format, string extension)
    {
        var ext = extension.TrimStart('.').ToLowerInvariant();
        return ext switch
        {
            "png" => "image/png",
            "jpg" or "jpeg" => "image/jpeg",
            "webp" => "image/webp",
            "svg" => "image/svg+xml",
            "gif" => "image/gif",
            "bmp" => "image/bmp",
            "ico" => "image/x-icon",
            _ => MediaTypeNames.Application.Octet
        };
    }

    private static InclusionState ParseInclusion(string? val, InclusionState fallback)
    {
        if (string.IsNullOrWhiteSpace(val)) return fallback;
        return val.ToLowerInvariant() switch
        {
            "include" => InclusionState.Include,
            "exclude" => InclusionState.Exclude,
            "only" => InclusionState.Only,
            _ => fallback
        };
    }

    private static TagFilterMode ParseTagMode(string? val)
    {
        if (string.IsNullOrWhiteSpace(val)) return TagFilterMode.Any;
        return val.Equals("all", StringComparison.OrdinalIgnoreCase) ? TagFilterMode.All : TagFilterMode.Any;
    }

    private static ImageEditChanges ParseChanges(JsonElement element)
    {
        var changes = new ImageEditChanges();
        if (element.ValueKind != JsonValueKind.Object) return changes;

        if (element.TryGetProperty("title", out var titleProp))
        {
            changes.HasTitle = true;
            changes.Title = titleProp.ValueKind == JsonValueKind.Null ? null : titleProp.GetString();
        }

        if (element.TryGetProperty("description", out var descProp))
        {
            changes.HasDescription = true;
            changes.Description = descProp.ValueKind == JsonValueKind.Null ? null : descProp.GetString();
        }

        if (element.TryGetProperty("rating", out var ratingProp))
        {
            changes.HasRating = true;
            changes.Rating = ratingProp.ValueKind == JsonValueKind.Null ? null : ratingProp.GetInt32();
        }

        if (element.TryGetProperty("isFavourite", out var favProp))
        {
            changes.HasIsFavourite = true;
            changes.IsFavourite = favProp.ValueKind == JsonValueKind.Null ? null : favProp.GetBoolean();
        }

        if (element.TryGetProperty("isHidden", out var hiddenProp))
        {
            changes.HasIsHidden = true;
            changes.IsHidden = hiddenProp.ValueKind == JsonValueKind.Null ? null : hiddenProp.GetBoolean();
        }

        if (element.TryGetProperty("takenOverrideUtc", out var takenProp))
        {
            changes.HasTakenOverrideUtc = true;
            changes.TakenOverrideUtc = takenProp.ValueKind == JsonValueKind.Null ? null : takenProp.GetString();
        }

        if (element.TryGetProperty("addTagIds", out var addTagsProp) && addTagsProp.ValueKind == JsonValueKind.Array)
        {
            changes.AddTagIds = addTagsProp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number)
                .Select(x => x.GetInt64())
                .ToList();
        }

        if (element.TryGetProperty("removeTagIds", out var removeTagsProp) && removeTagsProp.ValueKind == JsonValueKind.Array)
        {
            changes.RemoveTagIds = removeTagsProp.EnumerateArray()
                .Where(x => x.ValueKind == JsonValueKind.Number)
                .Select(x => x.GetInt64())
                .ToList();
        }

        return changes;
    }
}
