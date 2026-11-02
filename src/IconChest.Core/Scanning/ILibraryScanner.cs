namespace IconChest.Core.Scanning;

public interface ILibraryScanner
{
    Task<ScanResult> ScanAsync(long folderId, IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ScanResult>> ScanAllAsync(IProgress<ScanProgress>? progress = null, CancellationToken cancellationToken = default);
}

