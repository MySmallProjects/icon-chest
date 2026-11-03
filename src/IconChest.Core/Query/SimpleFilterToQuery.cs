using System.Text;
using IconChest.Core.Images;

namespace IconChest.Core.Query;

public sealed record ConvertFilterResult(
    bool Success,
    string? QueryText,
    string? UnsupportedReason = null);

public static class SimpleFilterToQuery
{
    public static ConvertFilterResult Convert(ImageSearchRequest request, Func<long, string?>? folderResolver = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        // 1. Check for filters that cannot be expressed
        if (request.FolderId.HasValue && string.IsNullOrWhiteSpace(request.Directory))
        {
            return new ConvertFilterResult(
                false,
                null,
                "Folder ID filter without directory path cannot be expressed in query language.");
        }

        if (request.Month.HasValue && !request.Year.HasValue)
        {
            return new ConvertFilterResult(
                false,
                null,
                "Month filter without a year cannot be expressed in query language.");
        }

        var clauses = new List<string>();

        // 2. Directory & IncludeSubfolders (Scope)
        if (!string.IsNullOrWhiteSpace(request.Directory))
        {
            var cleanDir = QueryValidator.NormalizeFolderPath(request.Directory);
            if (request.IncludeSubfolders)
            {
                clauses.Add($"folder under \"{Escape(cleanDir)}\"");
            }
            else
            {
                clauses.Add($"folder == \"{Escape(cleanDir)}\"");
            }
        }

        // 2b. FilterFolderId (Filter bar)
        if (request.FilterFolderId.HasValue)
        {
            string? folderPath = null;
            if (folderResolver != null)
            {
                folderPath = folderResolver(request.FilterFolderId.Value);
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return new ConvertFilterResult(
                    false,
                    null,
                    $"Library folder with ID {request.FilterFolderId.Value} could not be resolved to a path.");
            }

            var cleanPath = QueryValidator.NormalizeFolderPath(folderPath);
            clauses.Add($"folder under \"{Escape(cleanPath)}\"");
        }

        // 3. Date field & Date filters (Year / Month from scope)
        string dateField = ResolveDateField(request.DateField);

        if (request.YearUnknown)
        {
            clauses.Add($"{dateField} is empty");
        }
        else if (request.Year.HasValue && request.Month.HasValue)
        {
            clauses.Add($"{dateField} == {request.Year.Value:D4}-{request.Month.Value:D2}");
        }
        else if (request.Year.HasValue)
        {
            clauses.Add($"{dateField} == {request.Year.Value:D4}");
        }

        // 3b. RangeField (for DateFrom / DateTo from filter bar)
        string rangeField = ResolveDateField(request.RangeField);
        if (!string.IsNullOrWhiteSpace(request.DateFrom))
        {
            clauses.Add($"{rangeField} >= {request.DateFrom.Trim()}");
        }
        if (!string.IsNullOrWhiteSpace(request.DateTo))
        {
            clauses.Add($"{rangeField} <= {request.DateTo.Trim()}");
        }

        // 4. Untagged
        if (request.Untagged == true)
        {
            clauses.Add("tag is empty");
        }

        // 4b. Scope Tags
        if (request.Tags != null && request.Tags.Count > 0)
        {
            var validTags = request.Tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToList();

            if (validTags.Count > 0)
            {
                if (request.TagMode == TagFilterMode.All)
                {
                    foreach (var tag in validTags)
                    {
                        clauses.Add($"tag == \"{Escape(tag)}\"");
                    }
                }
                else
                {
                    if (validTags.Count == 1)
                    {
                        clauses.Add($"tag == \"{Escape(validTags[0])}\"");
                    }
                    else
                    {
                        var inList = string.Join(", ", validTags.Select(t => $"\"{Escape(t)}\""));
                        clauses.Add($"tag in ({inList})");
                    }
                }
            }
        }

        // 4c. FilterTags (from filter bar)
        if (request.FilterTags != null && request.FilterTags.Count > 0)
        {
            var validFilterTags = request.FilterTags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToList();

            if (validFilterTags.Count > 0)
            {
                if (request.FilterTagMode == TagFilterMode.All)
                {
                    foreach (var tag in validFilterTags)
                    {
                        clauses.Add($"tag == \"{Escape(tag)}\"");
                    }
                }
                else
                {
                    if (validFilterTags.Count == 1)
                    {
                        clauses.Add($"tag == \"{Escape(validFilterTags[0])}\"");
                    }
                    else
                    {
                        var inList = string.Join(", ", validFilterTags.Select(t => $"\"{Escape(t)}\""));
                        clauses.Add($"tag in ({inList})");
                    }
                }
            }
        }

        // 5. Types
        if (request.Types != null && request.Types.Count > 0)
        {
            var validTypes = request.Types
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().TrimStart('.').ToLowerInvariant())
                .ToList();

            if (validTypes.Count > 0)
            {
                if (validTypes.Count == 1)
                {
                    clauses.Add($"type == {validTypes[0]}");
                }
                else
                {
                    var inList = string.Join(", ", validTypes);
                    clauses.Add($"type in ({inList})");
                }
            }
        }

        // 6. Dimensions
        if (request.MinWidth.HasValue) clauses.Add($"width >= {request.MinWidth.Value}");
        if (request.MaxWidth.HasValue) clauses.Add($"width <= {request.MaxWidth.Value}");
        if (request.MinHeight.HasValue) clauses.Add($"height >= {request.MinHeight.Value}");
        if (request.MaxHeight.HasValue) clauses.Add($"height <= {request.MaxHeight.Value}");

        // 7. Size
        if (request.MinSize.HasValue) clauses.Add($"size >= {request.MinSize.Value}");
        if (request.MaxSize.HasValue) clauses.Add($"size <= {request.MaxSize.Value}");

        // 8. Rating & Favourite
        if (request.RatingMin.HasValue && request.RatingMax.HasValue)
        {
            if (request.RatingMin.Value == request.RatingMax.Value)
            {
                clauses.Add($"rating == {request.RatingMin.Value}");
            }
            else
            {
                clauses.Add($"rating >= {request.RatingMin.Value}");
                if (request.RatingMax.Value == 0)
                {
                    clauses.Add("rating == 0");
                }
                else
                {
                    clauses.Add($"rating <= {request.RatingMax.Value}");
                }
            }
        }
        else if (request.RatingMin.HasValue)
        {
            clauses.Add($"rating >= {request.RatingMin.Value}");
        }
        else if (request.RatingMax.HasValue)
        {
            if (request.RatingMax.Value == 0)
            {
                clauses.Add("rating == 0");
            }
            else
            {
                clauses.Add($"rating <= {request.RatingMax.Value}");
            }
        }

        if (request.Favourite.HasValue)
        {
            clauses.Add(request.Favourite.Value ? "favourite" : "favourite == false");
        }

        // 9. Text search (bare terms ANDed, quoted)
        if (!string.IsNullOrWhiteSpace(request.Text))
        {
            var terms = request.Text.Trim()
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (terms.Length == 1)
            {
                clauses.Add($"\"{Escape(terms[0])}\"");
            }
            else if (terms.Length > 1)
            {
                var andTerms = string.Join(" and ", terms.Select(t => $"\"{Escape(t)}\""));
                clauses.Add(andTerms);
            }
        }

        // 10. Inclusion flags
        switch (request.Hidden)
        {
            case InclusionState.Only:
                clauses.Add("hidden == true");
                break;
            case InclusionState.Include:
                clauses.Add("(hidden == true or hidden == false)");
                break;
            case InclusionState.Exclude:
                // exclude adds nothing (implicit in query engine)
                break;
        }

        switch (request.Missing)
        {
            case InclusionState.Only:
                clauses.Add("missing == true");
                break;
            case InclusionState.Include:
                clauses.Add("(missing == true or missing == false)");
                break;
            case InclusionState.Exclude:
                // exclude adds nothing (implicit in query engine)
                break;
        }

        switch (request.Duplicate)
        {
            case InclusionState.Only:
                clauses.Add("duplicate == true");
                break;
            case InclusionState.Exclude:
                clauses.Add("duplicate == false");
                break;
            case InclusionState.Include:
                // include adds nothing (default in query engine)
                break;
        }

        string queryText = clauses.Count > 0 ? string.Join(" and ", clauses) : "";
        return new ConvertFilterResult(true, queryText);
    }

    private static string ResolveDateField(string? field)
    {
        return (field ?? "effective").ToLowerInvariant() switch
        {
            "created" => "created",
            "modified" => "modified",
            "taken" => "taken",
            "added" => "added",
            _ => "date"
        };
    }

    private static string Escape(string text)
    {
        return text.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

