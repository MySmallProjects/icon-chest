namespace IconChest.Core.Thumbnails;

public static class ThumbnailSizes
{
    public const int Small = 64;
    public const int Medium = 128;
    public const int Large = 256;

    public static readonly int[] All = [Small, Medium, Large];

    /// <summary>
    /// Returns the smallest of 64, 128, 256 that is at least the requested size, capped at 256.
    /// If requested size is <= 64, returns 64.
    /// </summary>
    public static int PickFor(int displayPixels)
    {
        if (displayPixels <= Small) return Small;
        if (displayPixels <= Medium) return Medium;
        return Large;
    }
}

