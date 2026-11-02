namespace IconChest.Core.Thumbnails;

public interface IThumbnailCache
{
    string CacheDirectory { get; }
    string GetThumbnailPath(string hash, int size);
    bool Exists(string hash, int size);
    Task<long> WriteAsync(string hash, int size, byte[] bytes, CancellationToken cancellationToken = default);
    bool Delete(string hash, int size);
    long GetTotalBytes();
    int CleanStaleTempFiles();
    void ClearAll();
}

