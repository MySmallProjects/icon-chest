using IconChest.Core.Data.Models;

namespace IconChest.Core.Scanning;

public interface ILibraryFolderService
{
    long AddFolder(string folderPath, string? includePatterns = null);
    void RemoveFolder(long id);
    LibraryFolder? GetById(long id);
    LibraryFolder? GetByPath(string path);
    IReadOnlyList<LibraryFolder> ListFolders();
    void SetEnabled(long id, bool enabled);
    int GetImageCount(long id);
}

