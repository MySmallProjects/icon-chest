namespace IconChest.Core.Query;

public sealed record QueryExample(
    string Text,
    string Description,
    string Category,
    IReadOnlyList<long>? ExpectedIds = null);

public static class QueryExamples
{
    public static readonly IReadOnlyList<QueryExample> All = new List<QueryExample>
    {
        // 1. Format & Dimensions
        new("type == png", "Find all PNG images", "Format & Dimensions"),
        new("type in (jpg, png, webp)", "Find images in common web formats", "Format & Dimensions"),
        new("type like \"p*\"", "Find formats starting with letter 'p'", "Format & Dimensions"),
        new("width >= 256 and height >= 256", "High-resolution square or rectangular icons", "Format & Dimensions"),
        new("width == height", "Find perfectly square images", "Format & Dimensions"),
        new("width in (16, 32, 48, 64)", "Standard favicon or toolbar sizes", "Format & Dimensions"),
        new("width is empty", "Images where dimensions could not be read", "Format & Dimensions"),
        new("size < 50kb", "Small files under 50 kilobytes", "Format & Dimensions"),
        new("size >= 1mb", "Large image files at or above 1 megabyte", "Format & Dimensions"),

        // 2. Tags & Organization
        new("tag == \"ui\"", "Images tagged with 'ui'", "Tags & Organization"),
        new("tag != \"archived\"", "Images without the 'archived' tag", "Tags & Organization"),
        new("tag in (\"icon\", \"logo\", \"badge\")", "Images with any of icon, logo, or badge", "Tags & Organization"),
        new("tag like \"app*\"", "Tags starting with 'app' (e.g. apple, application)", "Tags & Organization"),
        new("tag contains \"nav\"", "Tags containing the substring 'nav'", "Tags & Organization"),
        new("tag is empty", "Untagged images needing organization", "Tags & Organization"),
        new("tag is not empty", "All images that have at least one tag", "Tags & Organization"),
        new("tag == \"ui\" and tag == \"dark\"", "Images that have both 'ui' and 'dark' tags", "Tags & Organization"),

        // 3. Ratings & Favorites
        new("favourite", "All favourite images using boolean shorthand", "Ratings & Favorites"),
        new("!favourite and rating >= 4", "High-rated images that are not yet marked favourite", "Ratings & Favorites"),
        new("rating == 5", "Five-star images", "Ratings & Favorites"),
        new("rating is empty", "Unrated images (rating 0)", "Ratings & Favorites"),
        new("rating >= 3 and rating <= 5", "Rated three stars or higher", "Ratings & Favorites"),

        // 4. Dates & Timestamps
        new("date == 2026", "Images with effective date in calendar year 2026", "Dates & Timestamps"),
        new("date == 2026-05", "Images with effective date in May 2026", "Dates & Timestamps"),
        new("date >= 2026-01-01 and date <= 2026-12-31", "Images from the year 2026 using explicit bounds", "Dates & Timestamps"),
        new("created >= 2026-06-01", "Files created in or after June 2026", "Dates & Timestamps"),
        new("modified < 2025-01-01", "Files not modified since before 2025", "Dates & Timestamps"),
        new("taken is not empty", "Photos having an EXIF or overridden taken date", "Dates & Timestamps"),
        new("added == 2026-11", "Images indexed in IconChest during November 2026", "Dates & Timestamps"),

        // 5. Folder & Paths
        new("folder == \"/home/user/icons/svg\"", "Exact directory match", "Folder & Paths"),
        new("folder under \"/home/user/icons\"", "All images inside folder or any subfolder", "Folder & Paths"),
        new("folder like \"*buttons*\"", "Folders matching pattern with 'buttons'", "Folder & Paths"),

        // 6. Text & Metadata
        new("title is empty", "Images without a custom title", "Text & Metadata"),
        new("title contains \"arrow\"", "Titles containing the literal word 'arrow'", "Text & Metadata"),
        new("filename like \"*.svg\"", "SVG files by filename extension", "Text & Metadata"),
        new("elephant", "Bare search term: matches 'elephant' in filename, title, or description", "Text & Metadata"),
        new("\"search icon*\"", "Search term with space and wildcard", "Text & Metadata"),

        // 7. Flags & System State
        new("hidden", "Images marked as hidden (overriding implicit hidden exclusion)", "Flags & System State"),
        new("missing == true", "Missing files on disk (overriding implicit missing exclusion)", "Flags & System State"),
        new("duplicate", "Duplicate content files", "Flags & System State"),
        new("transparent", "Images with transparency alpha channel", "Flags & System State"),

        // 8. Complex Combinations
        new("(width >= 256 and height >= 256) and size < 100kb", "Large dimensions with compact file size", "Complex Combinations"),
        new("tag is empty and rating >= 4", "Untagged top-rated images", "Complex Combinations"),
        new("(type == png or type == svg) and (tag == \"ui\" or tag == \"web\")", "Web icon assets in PNG or SVG", "Complex Combinations"),
        new("not (tag == \"draft\" or hidden)", "Exclude drafts and hidden images", "Complex Combinations")
    };
}

