namespace IconChest.Core.Query;

public static class FieldRegistry
{
    private static readonly HashSet<string> FormatOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!=", "in", "like"
    };

    private static readonly HashSet<string> TextOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!=", "in", "like", "contains", "is empty"
    };

    private static readonly HashSet<string> PathOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!=", "in", "under", "like", "contains"
    };

    private static readonly HashSet<string> MultiTextOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!=", "in", "like", "contains", "is empty"
    };

    private static readonly HashSet<string> NumericOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!=", "<", "<=", ">", ">=", "in", "is empty"
    };

    private static readonly HashSet<string> SizeOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!=", "<", "<=", ">", ">=", "in"
    };

    private static readonly HashSet<string> BooleanOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!="
    };

    private static readonly HashSet<string> DateOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "==", "=", "!=", "<", "<=", ">", ">=", "is empty"
    };

    public static readonly IReadOnlySet<string> KnownFormats = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "png", "jpg", "jpeg", "gif", "webp", "svg", "bmp", "ico"
    };

    public static readonly IReadOnlyList<FieldDefinition> Fields = new List<FieldDefinition>
    {
        new()
        {
            Name = "type",
            Type = FieldType.Format,
            AllowedOperators = FormatOperators,
            SqlColumn = "LOWER(format)",
            Description = "Image format (png, jpg, jpeg, gif, webp, svg, bmp, ico).",
            Examples = new[] { "type == png", "type in (jpg, png)", "type like \"p*\"" }
        },
        new()
        {
            Name = "filename",
            Type = FieldType.Text,
            AllowedOperators = TextOperators,
            SqlColumn = "file_name",
            Description = "File name with extension.",
            Examples = new[] { "filename == \"icon.svg\"", "filename like \"*.png\"", "filename contains \"logo\"" }
        },
        new()
        {
            Name = "title",
            Type = FieldType.Text,
            AllowedOperators = TextOperators,
            SqlColumn = "title",
            Description = "Editable image title.",
            Examples = new[] { "title is empty", "title contains \"arrow\"", "title like \"Search*\"" }
        },
        new()
        {
            Name = "description",
            Type = FieldType.Text,
            AllowedOperators = TextOperators,
            SqlColumn = "description",
            Description = "Editable image description.",
            Examples = new[] { "description is not empty", "description contains \"button\"" }
        },
        new()
        {
            Name = "folder",
            Type = FieldType.Path,
            AllowedOperators = PathOperators,
            SqlColumn = "directory_path",
            Description = "Directory path of the image file.",
            Examples = new[] { "folder under \"/home/user/icons\"", "folder == \"/home/user/icons/svg\"" }
        },
        new()
        {
            Name = "tag",
            Type = FieldType.MultiText,
            AllowedOperators = MultiTextOperators,
            SqlColumn = "tag",
            Description = "Image tags (multi-valued).",
            Examples = new[] { "tag == \"ui\"", "tag in (\"icon\", \"logo\")", "tag is empty", "tag like \"app*\"" }
        },
        new()
        {
            Name = "width",
            Type = FieldType.Integer,
            AllowedOperators = NumericOperators,
            SqlColumn = "width",
            Description = "Image width in pixels.",
            Examples = new[] { "width >= 256", "width in (16, 32, 64)", "width is empty" }
        },
        new()
        {
            Name = "height",
            Type = FieldType.Integer,
            AllowedOperators = NumericOperators,
            SqlColumn = "height",
            Description = "Image height in pixels.",
            Examples = new[] { "height == 128", "height < 500", "height is empty" }
        },
        new()
        {
            Name = "size",
            Type = FieldType.Size,
            AllowedOperators = SizeOperators,
            SqlColumn = "size_bytes",
            Description = "File size in bytes or formatted units (e.g., 100kb, 1.5mb).",
            Examples = new[] { "size < 50kb", "size >= 1mb", "size in (1024, 2048)" }
        },
        new()
        {
            Name = "rating",
            Type = FieldType.Rating,
            AllowedOperators = NumericOperators,
            SqlColumn = "rating",
            Description = "Rating from 0 to 5 (0 means unrated and is empty).",
            Examples = new[] { "rating >= 4", "rating == 5", "rating is empty" }
        },
        new()
        {
            Name = "favourite",
            Aliases = new[] { "favorite" },
            Type = FieldType.Boolean,
            AllowedOperators = BooleanOperators,
            SqlColumn = "is_favourite",
            Description = "Favourite flag.",
            Examples = new[] { "favourite", "favorite == true", "!favourite" }
        },
        new()
        {
            Name = "hidden",
            Type = FieldType.Boolean,
            AllowedOperators = BooleanOperators,
            SqlColumn = "is_hidden",
            Description = "Hidden flag. Mentioning hidden overrides default exclusion.",
            Examples = new[] { "hidden", "hidden == true", "hidden == false" }
        },
        new()
        {
            Name = "missing",
            Type = FieldType.Boolean,
            AllowedOperators = BooleanOperators,
            SqlColumn = "is_missing",
            Description = "Missing file flag. Mentioning missing overrides default exclusion.",
            Examples = new[] { "missing", "missing == false", "missing == true" }
        },
        new()
        {
            Name = "duplicate",
            Type = FieldType.Boolean,
            AllowedOperators = BooleanOperators,
            SqlColumn = "is_duplicate",
            Description = "Duplicate content flag.",
            Examples = new[] { "duplicate", "duplicate == true", "duplicate == false" }
        },
        new()
        {
            Name = "transparent",
            Type = FieldType.Boolean,
            AllowedOperators = BooleanOperators,
            SqlColumn = "has_transparency",
            Description = "Transparency flag (true, false, or null when unknown).",
            Examples = new[] { "transparent", "transparent == true", "transparent == false" }
        },
        new()
        {
            Name = "date",
            Type = FieldType.Date,
            AllowedOperators = DateOperators,
            SqlColumn = "effective_date_utc",
            Description = "Effective date (taken override, EXIF date, created, or modified).",
            Examples = new[] { "date >= 2026-01-01", "date == 2026", "date == 2026-05" }
        },
        new()
        {
            Name = "created",
            Type = FieldType.Date,
            AllowedOperators = DateOperators,
            SqlColumn = "created_utc",
            Description = "File creation date.",
            Examples = new[] { "created >= 2026-01-01", "created is empty" }
        },
        new()
        {
            Name = "modified",
            Type = FieldType.Date,
            AllowedOperators = DateOperators,
            SqlColumn = "modified_utc",
            Description = "File modification date.",
            Examples = new[] { "modified >= 2026-06-01", "modified < 2026-10-31" }
        },
        new()
        {
            Name = "taken",
            Type = FieldType.Date,
            AllowedOperators = DateOperators,
            SqlColumn = "COALESCE(taken_override_utc, exif_date_utc)",
            Description = "Date photo was taken (override or EXIF date).",
            Examples = new[] { "taken is not empty", "taken == 2025" }
        },
        new()
        {
            Name = "added",
            Type = FieldType.Date,
            AllowedOperators = DateOperators,
            SqlColumn = "added_utc",
            Description = "Date the image was indexed in IconChest.",
            Examples = new[] { "added >= 2026-01-01", "added == 2026-11" }
        }
    };

    private static readonly Dictionary<string, FieldDefinition> FieldLookup = new(StringComparer.OrdinalIgnoreCase);

    static FieldRegistry()
    {
        foreach (var field in Fields)
        {
            FieldLookup[field.Name] = field;
            foreach (var alias in field.Aliases)
            {
                FieldLookup[alias] = field;
            }
        }
    }

    public static bool TryGet(string name, out FieldDefinition? field)
    {
        return FieldLookup.TryGetValue(name, out field);
    }

    public static string? FindNearestField(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;

        var lowerInput = input.ToLowerInvariant();
        string? bestMatch = null;
        int bestDistance = int.MaxValue;

        foreach (var field in Fields)
        {
            var namesToTest = new List<string> { field.Name };
            namesToTest.AddRange(field.Aliases);

            foreach (var name in namesToTest)
            {
                var lowerName = name.ToLowerInvariant();

                // Shared prefix check (if prefix is >= 2 chars)
                if (lowerName.StartsWith(lowerInput) || lowerInput.StartsWith(lowerName))
                {
                    if (lowerInput.Length >= 2 || lowerName.Length >= 2)
                    {
                        return field.Name;
                    }
                }

                int dist = LevenshteinDistance(lowerInput, lowerName);
                if (dist <= 2 && dist < bestDistance)
                {
                    bestDistance = dist;
                    bestMatch = field.Name;
                }
            }
        }

        return bestMatch;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        int n = s.Length;
        int m = t.Length;
        if (n == 0) return m;
        if (m == 0) return n;

        var d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; i++) d[i, 0] = i;
        for (int j = 0; j <= m; j++) d[0, j] = j;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (s[i - 1] == t[j - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}

