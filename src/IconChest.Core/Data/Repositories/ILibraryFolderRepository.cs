using IconChest.Core.Data.Models;

namespace IconChest.Core.Data.Repositories;

public interface ILibraryFolderRepository
{
    long Add(string path, string? includePatterns = null);
    LibraryFolder? GetById(long id);
    LibraryFolder? GetByPath(string path);
    IReadOnlyList<LibraryFolder> List();
    void SetEnabled(long id, bool enabled);
    void UpdateLastScan(long id, string lastScanUtc);
    void Remove(long id);
    int GetImageCount(long folderId);
}

