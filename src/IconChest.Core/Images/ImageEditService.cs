using Dapper;
using IconChest.Core.Data;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Events;
using IconChest.Core.Scanning;

namespace IconChest.Core.Images;

public sealed class ImageEditChanges
{
    public bool HasTitle { get; set; }
    public string? Title { get; set; }

    public bool HasDescription { get; set; }
    public string? Description { get; set; }

    public bool HasRating { get; set; }
    public int? Rating { get; set; }

    public bool HasIsFavourite { get; set; }
    public bool? IsFavourite { get; set; }

    public bool HasIsHidden { get; set; }
    public bool? IsHidden { get; set; }

    public bool HasTakenOverrideUtc { get; set; }
    public string? TakenOverrideUtc { get; set; }

    public List<long>? AddTagIds { get; set; }
    public List<long>? RemoveTagIds { get; set; }
}

public interface IImageEditService
{
    void EditImages(IReadOnlyList<long> ids, ImageEditChanges changes);
}

public sealed class ImageEditService : IImageEditService
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IImageRepository _imageRepository;
    private readonly IImageMetadataReader _metadataReader;
    private readonly ILibraryEventEmitter? _eventEmitter;

    public ImageEditService(
        IDatabaseConnectionFactory connectionFactory,
        IImageRepository imageRepository,
        IImageMetadataReader? metadataReader = null,
        ILibraryEventEmitter? eventEmitter = null)
    {
        _connectionFactory = connectionFactory;
        _imageRepository = imageRepository;
        _metadataReader = metadataReader ?? new ImageMetadataReader();
        _eventEmitter = eventEmitter;
    }

    public void EditImages(IReadOnlyList<long> ids, ImageEditChanges changes)
    {
        if (ids == null || ids.Count == 0) return;

        if (changes.HasRating && changes.Rating.HasValue)
        {
            if (changes.Rating.Value is < 0 or > 5)
            {
                throw new ArgumentOutOfRangeException(nameof(changes.Rating), "Rating must be between 0 and 5.");
            }
        }

        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            // 1. Verify all IDs exist and retrieve their current file names and hashes
            var existing = conn.Query<(long Id, string FileName, string Hash, bool IsHidden)>(
                "SELECT id, file_name AS FileName, hash AS Hash, is_hidden AS IsHidden FROM image WHERE id IN @ids;",
                new { ids }, transaction: tx).ToList();

            if (existing.Count != ids.Distinct().Count())
            {
                var existingIdSet = existing.Select(x => x.Id).ToHashSet();
                var missingIds = ids.Where(id => !existingIdSet.Contains(id)).ToList();
                throw new KeyNotFoundException($"Image IDs not found: {string.Join(", ", missingIds)}");
            }

            var affectedHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            bool hiddenChanged = changes.HasIsHidden && changes.IsHidden.HasValue;

            // 2. Perform updates per image (for title defaults) or batch
            foreach (var img in existing)
            {
                var updateClauses = new List<string>();
                var p = new DynamicParameters();
                p.Add("id", img.Id);

                if (changes.HasTitle)
                {
                    string targetTitle = string.IsNullOrWhiteSpace(changes.Title)
                        ? _metadataReader.DeriveDefaultTitle(img.FileName)
                        : changes.Title.Trim();

                    updateClauses.Add("title = @title");
                    p.Add("title", targetTitle);
                }

                if (changes.HasDescription)
                {
                    updateClauses.Add("description = @desc");
                    p.Add("desc", changes.Description ?? string.Empty);
                }

                if (changes.HasRating)
                {
                    updateClauses.Add("rating = @rating");
                    p.Add("rating", changes.Rating ?? 0);
                }

                if (changes.HasIsFavourite)
                {
                    updateClauses.Add("is_favourite = @fav");
                    p.Add("fav", changes.IsFavourite == true ? 1 : 0);
                }

                if (changes.HasIsHidden && changes.IsHidden.HasValue)
                {
                    updateClauses.Add("is_hidden = @hidden");
                    p.Add("hidden", changes.IsHidden.Value ? 1 : 0);
                    if (img.IsHidden != changes.IsHidden.Value)
                    {
                        affectedHashes.Add(img.Hash);
                    }
                }

                if (changes.HasTakenOverrideUtc)
                {
                    updateClauses.Add("taken_override_utc = @taken");
                    p.Add("taken", string.IsNullOrWhiteSpace(changes.TakenOverrideUtc) ? null : changes.TakenOverrideUtc);
                }

                if (updateClauses.Count > 0)
                {
                    string updateSql = $"UPDATE image SET {string.Join(", ", updateClauses)} WHERE id = @id;";
                    conn.Execute(updateSql, p, transaction: tx);
                }

                // 3. Tag additions
                if (changes.AddTagIds != null && changes.AddTagIds.Count > 0)
                {
                    foreach (var tagId in changes.AddTagIds.Distinct())
                    {
                        conn.Execute(
                            "INSERT OR IGNORE INTO image_tag (image_id, tag_id) VALUES (@imgId, @tagId);",
                            new { imgId = img.Id, tagId }, transaction: tx);
                    }
                }

                // 4. Tag removals
                if (changes.RemoveTagIds != null && changes.RemoveTagIds.Count > 0)
                {
                    conn.Execute(
                        "DELETE FROM image_tag WHERE image_id = @imgId AND tag_id IN @removeTagIds;",
                        new { imgId = img.Id, removeTagIds = changes.RemoveTagIds }, transaction: tx);
                }
            }

            tx.Commit();

            // 5. If hidden state changed, recompute duplicates for affected hashes
            if (affectedHashes.Count > 0)
            {
                _imageRepository.RecomputeDuplicates(affectedHashes);
            }

            // 6. Emit library.changed event
            _eventEmitter?.EmitLibraryChanged();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }
}
