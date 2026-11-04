using System.Text.RegularExpressions;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data;
using IconChest.Core.Events;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Tags;

public interface ITagService
{
    IReadOnlyList<TagRecord> GetAll();
    TagRecord? GetById(long id);
    TagRecord Create(string name, string? fgColor = null, string? bgColor = null);
    TagRecord Update(long id, string? name = null, string? fgColor = null, string? bgColor = null);
    bool Delete(long id);
    TagExportDocument ExportTags();
    TagImportResult ImportTags(TagExportDocument document);
}

public sealed partial class TagService : ITagService
{
    private static readonly Regex ColorRegex = new(@"^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    private const string DefaultFgColor = "#FFFFFF";
    private const string DefaultBgColor = "#4A5568";

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly ILibraryEventEmitter? _eventEmitter;
    private readonly IClock _clock;
    private readonly ILogger<TagService> _logger;

    public TagService(
        IDatabaseConnectionFactory connectionFactory,
        ILibraryEventEmitter? eventEmitter = null,
        IClock? clock = null,
        ILogger<TagService>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _eventEmitter = eventEmitter;
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<TagService>.Instance;
    }

    public IReadOnlyList<TagRecord> GetAll()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        const string sql = @"
            SELECT t.id, t.name,
                   COALESCE(t.fg_color, @defaultFg) AS FgColor,
                   COALESCE(t.bg_color, @defaultBg) AS BgColor,
                   COUNT(it.image_id) AS ImageCount
            FROM tag t
            LEFT JOIN image_tag it ON t.id = it.tag_id
            GROUP BY t.id, t.name, t.fg_color, t.bg_color
            ORDER BY t.name COLLATE NOCASE ASC;";

        return conn.Query<TagRecord>(sql, new { defaultFg = DefaultFgColor, defaultBg = DefaultBgColor }).ToList();
    }

    public TagRecord? GetById(long id)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        const string sql = @"
            SELECT t.id, t.name,
                   COALESCE(t.fg_color, @defaultFg) AS FgColor,
                   COALESCE(t.bg_color, @defaultBg) AS BgColor,
                   COUNT(it.image_id) AS ImageCount
            FROM tag t
            LEFT JOIN image_tag it ON t.id = it.tag_id
            WHERE t.id = @id
            GROUP BY t.id, t.name, t.fg_color, t.bg_color;";

        return conn.QuerySingleOrDefault<TagRecord>(sql, new { id, defaultFg = DefaultFgColor, defaultBg = DefaultBgColor });
    }

    public TagRecord Create(string name, string? fgColor = null, string? bgColor = null)
    {
        var validatedName = ValidateTagName(name);
        var validatedFg = ValidateColor(fgColor, DefaultFgColor);
        var validatedBg = ValidateColor(bgColor, DefaultBgColor);

        using var conn = _connectionFactory.CreateAndOpenConnection();

        // Case-insensitive uniqueness check
        var existingCount = conn.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM tag WHERE name = @name COLLATE NOCASE;",
            new { name = validatedName });

        if (existingCount > 0)
        {
            throw new TagConflictException($"A tag with name '{validatedName}' already exists.");
        }

        const string insertSql = @"
            INSERT INTO tag (name, fg_color, bg_color)
            VALUES (@name, @fgColor, @bgColor)
            RETURNING id;";

        var id = conn.QuerySingle<long>(insertSql, new
        {
            name = validatedName,
            fgColor = validatedFg,
            bgColor = validatedBg
        });

        return new TagRecord(id, validatedName, validatedFg, validatedBg, ImageCount: 0);
    }

    public TagRecord Update(long id, string? name = null, string? fgColor = null, string? bgColor = null)
    {
        var existing = GetById(id) ?? throw new KeyNotFoundException($"Tag with ID {id} was not found.");

        string targetName = existing.Name;
        if (name != null)
        {
            targetName = ValidateTagName(name);
            if (!string.Equals(targetName, existing.Name, StringComparison.OrdinalIgnoreCase))
            {
                using var conn = _connectionFactory.CreateAndOpenConnection();
                var conflict = conn.ExecuteScalar<int>(
                    "SELECT COUNT(*) FROM tag WHERE name = @name COLLATE NOCASE AND id != @id;",
                    new { name = targetName, id });

                if (conflict > 0)
                {
                    throw new TagConflictException($"A tag with name '{targetName}' already exists.");
                }
            }
        }

        string targetFg = fgColor != null ? ValidateColor(fgColor, existing.FgColor) : existing.FgColor;
        string targetBg = bgColor != null ? ValidateColor(bgColor, existing.BgColor) : existing.BgColor;

        using (var conn = _connectionFactory.CreateAndOpenConnection())
        {
            conn.Execute(@"
                UPDATE tag
                SET name = @targetName,
                    fg_color = @targetFg,
                    bg_color = @targetBg
                WHERE id = @id;",
                new { targetName, targetFg, targetBg, id });
        }

        return new TagRecord(id, targetName, targetFg, targetBg, existing.ImageCount);
    }

    public bool Delete(long id)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            conn.Execute("DELETE FROM image_tag WHERE tag_id = @id;", new { id }, transaction: tx);
            int affected = conn.Execute("DELETE FROM tag WHERE id = @id;", new { id }, transaction: tx);
            tx.Commit();
            return affected > 0;
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public TagExportDocument ExportTags()
    {
        var allTags = GetAll();
        var items = allTags.Select(t => new TagExportItem(t.Name, t.FgColor, t.BgColor)).ToList();

        return new TagExportDocument(
            Format: "iconchest-tags",
            Version: 1,
            ExportedUtc: _clock.UtcNow.ToString("O"),
            Tags: items);
    }

    public TagImportResult ImportTags(TagExportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (!string.Equals(document.Format, "iconchest-tags", StringComparison.OrdinalIgnoreCase))
        {
            throw new TagValidationException($"Invalid tag document format '{document.Format}'. Expected 'iconchest-tags'.");
        }

        if (document.Version > 1)
        {
            throw new TagValidationException($"Unsupported tag file version: {document.Version}. Supported version is 1.");
        }

        if (document.Tags != null && document.Tags.Count > 10000)
        {
            throw new TagValidationException("Tag document contains more than the maximum allowed 10,000 tags.");
        }

        if (document.Tags == null || document.Tags.Count == 0)
        {
            return new TagImportResult(0, 0, 0, 0, Array.Empty<string>());
        }

        using var conn = _connectionFactory.CreateAndOpenConnection();

        // 1. Existing tags from DB (case-insensitive name match)
        var existingNames = conn.Query<string>("SELECT name FROM tag;")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var seenInFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var toInsert = new List<TagInsertItem>();
        var errors = new List<string>();

        int addedCount = 0;
        int skippedCount = 0;
        int duplicateInFileCount = 0;
        int invalidCount = 0;

        foreach (var item in document.Tags)
        {
            if (string.IsNullOrWhiteSpace(item.Name))
            {
                invalidCount++;
                if (errors.Count < 50) errors.Add("Tag name is empty or missing.");
                continue;
            }

            var trimmedName = item.Name.Trim();

            // Check duplicate in file
            if (!seenInFile.Add(trimmedName))
            {
                duplicateInFileCount++;
                continue;
            }

            // Validate name rules
            if (trimmedName.Length is < 1 or > 50 || trimmedName.Any(char.IsControl))
            {
                invalidCount++;
                if (errors.Count < 50) errors.Add($"Tag '{trimmedName}' has an invalid name (length {trimmedName.Length} or control characters).");
                continue;
            }

            // Validate color rules
            string fg;
            string bg;
            try
            {
                fg = ValidateColor(item.FgColor, DefaultFgColor);
                bg = ValidateColor(item.BgColor, DefaultBgColor);
            }
            catch (TagValidationException ex)
            {
                invalidCount++;
                if (errors.Count < 50) errors.Add($"Tag '{trimmedName}': {ex.Message}");
                continue;
            }

            // Check if existing tag wins
            if (existingNames.Contains(trimmedName))
            {
                skippedCount++;
                continue;
            }

            // Valid new tag
            toInsert.Add(new TagInsertItem(trimmedName, fg, bg));
            existingNames.Add(trimmedName);
            addedCount++;
        }

        if (toInsert.Count > 0)
        {
            using var tx = conn.BeginTransaction();
            try
            {
                const string insertSql = "INSERT INTO tag (name, fg_color, bg_color) VALUES (@Name, @FgColor, @BgColor);";
                conn.Execute(insertSql, toInsert, transaction: tx);
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }

            _eventEmitter?.EmitLibraryChanged();
        }

        _logger.LogInformation("Tag import finished. Added: {Added}, Skipped: {Skipped}, Duplicates in file: {Duplicates}, Invalid: {Invalid}.",
            addedCount, skippedCount, duplicateInFileCount, invalidCount);

        return new TagImportResult(addedCount, skippedCount, duplicateInFileCount, invalidCount, errors);
    }

    private static string ValidateTagName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new TagValidationException("Tag name cannot be empty.");
        }

        var trimmed = name.Trim();
        if (trimmed.Length is < 1 or > 50)
        {
            throw new TagValidationException($"Tag name length must be between 1 and 50 characters, but got {trimmed.Length}.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new TagValidationException("Tag name cannot contain control characters.");
        }

        return trimmed;
    }

    private static string ValidateColor(string? color, string fallback)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return fallback;
        }

        var trimmed = color.Trim();
        if (!ColorRegex.IsMatch(trimmed))
        {
            throw new TagValidationException($"Color '{trimmed}' must be a valid hex color in #RRGGBB format.");
        }

        return trimmed.ToUpperInvariant();
    }

    private sealed record TagInsertItem(string Name, string FgColor, string BgColor);
}

