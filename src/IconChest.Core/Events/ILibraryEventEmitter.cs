using IconChest.Core.Thumbnails;

namespace IconChest.Core.Events;

public interface ILibraryEventEmitter
{
    event Action? LibraryChanged;
    event Action? DataReloaded;
    event Action<object>? ScanProgress;
    event Action<object>? ScanCompleted;
    event Action<ThumbnailQueueStatus>? ThumbnailStatus;

    void EmitLibraryChanged();
    void EmitDataReloaded();
    void EmitScanProgress(object progress);
    void EmitScanCompleted(object result);
    void EmitThumbnailStatus(ThumbnailQueueStatus status);
}

public sealed class LibraryEventEmitter : ILibraryEventEmitter
{
    public event Action? LibraryChanged;
    public event Action? DataReloaded;
    public event Action<object>? ScanProgress;
    public event Action<object>? ScanCompleted;
    public event Action<ThumbnailQueueStatus>? ThumbnailStatus;

    public void EmitLibraryChanged()
    {
        LibraryChanged?.Invoke();
    }

    public void EmitDataReloaded()
    {
        DataReloaded?.Invoke();
    }

    public void EmitScanProgress(object progress)
    {
        ScanProgress?.Invoke(progress);
    }

    public void EmitScanCompleted(object result)
    {
        ScanCompleted?.Invoke(result);
    }

    public void EmitThumbnailStatus(ThumbnailQueueStatus status)
    {
        ThumbnailStatus?.Invoke(status);
    }
}

