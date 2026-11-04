namespace IconChest.Core.Thumbnails;

public interface IThumbnailProvider : IDisposable
{
    Task<ThumbnailResult> GetAsync(long imageId, int size, CancellationToken cancellationToken = default);
    void FlushAccessLog();
    int RunEviction(long? maxBytesOverride = null);
    int CleanupOrphans();
    Task ClearCacheAsync(bool reEnqueueAll = false, CancellationToken cancellationToken = default);
    Task<CacheVerificationReport> VerifyCacheAsync(CancellationToken cancellationToken = default);
}

