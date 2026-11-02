namespace IconChest.Core.Thumbnails;

public interface IThumbnailQueue
{
    int EnqueueForImages(IEnumerable<long> imageIds);
    int EnqueueForFolder(long folderId);
    int EnqueueAll();
    void Prioritize(string hash, int size);
    ThumbnailRecord? ClaimNextJob();
    string? ResolveSourceFilePath(string hash);
    void CompleteJob(string hash, int size, long bytes, int width, int height);
    void RecordAttemptFailure(string hash, int size, string error, bool isDeterministic);
    void MarkOrphaned(string hash, int size);
    void ResetToPending(string hash, int size);
    int RecoverInterruptedJobs();
    int RetryFailed();
    ThumbnailQueueStatus GetStatus();
    ThumbnailRecord? GetRecord(string hash, int size);
    IReadOnlyList<ThumbnailRecord> GetRecordsForImage(long imageId);
    void MarkPending(string hash, int size, int priority = 10);
}

