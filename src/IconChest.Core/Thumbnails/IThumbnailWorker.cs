namespace IconChest.Core.Thumbnails;

public interface IThumbnailWorker : IAsyncDisposable, IDisposable
{
    bool IsRunning { get; }
    bool IsPaused { get; }
    void Start();
    Task StopAsync();
    void Pause();
    void Resume();
    void NotifyWorkAvailable();
    event Action<ThumbnailRecord>? JobCompleted;
}

