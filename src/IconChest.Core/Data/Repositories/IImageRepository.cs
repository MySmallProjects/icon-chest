using IconChest.Core.Data.Models;

namespace IconChest.Core.Data.Repositories;

public interface IImageRepository
{
    ImageRecord? GetById(long id);
    ImageRecord? GetByPathKey(string pathKey);
    Dictionary<string, ImageRecord> GetRecordsByFolder(long folderId);
    IReadOnlyList<long> InsertBatch(IEnumerable<ImageRecord> records);
    void UpdateTechnicalBatch(IEnumerable<ImageRecord> records);
    void UpdateMoveBatch(IEnumerable<ImageMoveUpdate> moves);
    void MarkMissingBatch(IEnumerable<long> imageIds, string missingSinceUtc);
    void RestoreMissingBatch(IEnumerable<long> imageIds);
    int DeleteMissingForFolder(long folderId);
    IReadOnlyList<ImageRecord> GetLibraryMoveCandidates();
    void RecomputeDuplicates(IEnumerable<string> hashes);
    void RecomputeAllDuplicates();
    void SetHidden(long id, bool isHidden);
    IReadOnlyList<ImageRecord> List(long? folderId = null, bool includeHidden = false, bool includeMissing = false, bool duplicatesOnly = false, int limit = 50, int offset = 0);
    int Count(long? folderId = null, bool includeHidden = false, bool includeMissing = false, bool duplicatesOnly = false);
    DatabaseStats GetStats();
}

