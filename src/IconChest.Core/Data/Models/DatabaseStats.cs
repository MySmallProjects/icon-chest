namespace IconChest.Core.Data.Models;

public sealed record DatabaseStats(
    int FoldersCount,
    int ImagesCount,
    int MissingCount,
    int HiddenCount,
    int DuplicatesCount,
    int TagsCount);

