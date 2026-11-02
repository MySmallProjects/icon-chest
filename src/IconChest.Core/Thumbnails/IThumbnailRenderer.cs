namespace IconChest.Core.Thumbnails;

public enum ThumbnailRenderStatus
{
    Success,
    Unreadable,
    TooLarge,
    Timeout,
    Unsupported
}

public sealed record ThumbnailRenderResult(
    ThumbnailRenderStatus Status,
    byte[]? Bytes = null,
    int Width = 0,
    int Height = 0,
    string? ErrorMessage = null)
{
    public static ThumbnailRenderResult Ok(byte[] bytes, int width, int height) =>
        new(ThumbnailRenderStatus.Success, bytes, width, height);

    public static ThumbnailRenderResult Failed(ThumbnailRenderStatus status, string? errorMessage = null) =>
        new(status, null, 0, 0, errorMessage);
}

public interface IThumbnailRenderer
{
    Task<ThumbnailRenderResult> RenderAsync(
        string filePath,
        int targetSize,
        int quality = 85,
        CancellationToken cancellationToken = default);
}

