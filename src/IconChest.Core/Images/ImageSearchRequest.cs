namespace IconChest.Core.Images;

public enum InclusionState
{
    Exclude,
    Include,
    Only
}

public enum TagFilterMode
{
    Any,
    All
}

public sealed class ImageSearchRequest
{
    public long? FolderId { get; set; }
    public string? Directory { get; set; }
    public bool IncludeSubfolders { get; set; }

    public string DateField { get; set; } = "effective"; // effective | created | modified | taken | added
    public int? Year { get; set; }
    public int? Month { get; set; }
    public string? DateFrom { get; set; } // yyyy-mm-dd
    public string? DateTo { get; set; }   // yyyy-mm-dd

    public List<string>? Tags { get; set; } // tag names or IDs
    public TagFilterMode TagMode { get; set; } = TagFilterMode.Any;

    public List<string>? Types { get; set; } // format or extension (png, svg, jpg, ...)

    public int? MinWidth { get; set; }
    public int? MaxWidth { get; set; }
    public int? MinHeight { get; set; }
    public int? MaxHeight { get; set; }

    public long? MinSize { get; set; }
    public long? MaxSize { get; set; }

    public int? RatingMin { get; set; }
    public bool? Favourite { get; set; }

    public string? Text { get; set; } // contains search on file_name, title, description

    public InclusionState Hidden { get; set; } = InclusionState.Exclude;
    public InclusionState Missing { get; set; } = InclusionState.Exclude;
    public InclusionState Duplicate { get; set; } = InclusionState.Include;

    public string Sort { get; set; } = "filename";
    public string Dir { get; set; } = "asc";

    public int Offset { get; set; } = 0;
    public int Limit { get; set; } = 50;

    /// <summary>
    /// Phase 5 hook: allows a pre-compiled WHERE clause fragment with its own parameters.
    /// </summary>
    public string? CustomWhereClause { get; set; }
    public Dictionary<string, object?>? CustomWhereParameters { get; set; }
}

public sealed class ImageSearchResult
{
    public int Total { get; set; }
    public int Offset { get; set; }
    public int Limit { get; set; }
    public IReadOnlyList<ImageSummary> Items { get; set; } = Array.Empty<ImageSummary>();
}

