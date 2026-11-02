using IconChest.Core.Data.Models;
using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Scanning;

public sealed class LibraryFolderService : ILibraryFolderService
{
    private readonly ILibraryFolderRepository _repository;
    private readonly IPathKeyProvider _pathKeyProvider;
    private readonly Thumbnails.IThumbnailProvider? _thumbnailProvider;
    private readonly ILogger<LibraryFolderService> _logger;

    public LibraryFolderService(
        ILibraryFolderRepository repository,
        IPathKeyProvider? pathKeyProvider = null,
        Thumbnails.IThumbnailProvider? thumbnailProvider = null,
        ILogger<LibraryFolderService>? logger = null)
    {
        _repository = repository;
        _pathKeyProvider = pathKeyProvider ?? PathKeyProvider.Default;
        _thumbnailProvider = thumbnailProvider;
        _logger = logger ?? NullLogger<LibraryFolderService>.Instance;
    }

    public long AddFolder(string folderPath, string? includePatterns = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var normalizedPath = _pathKeyProvider.NormalizePath(folderPath);

        if (!Directory.Exists(normalizedPath))
        {
            if (File.Exists(normalizedPath))
            {
                throw new NotADirectoryException(normalizedPath);
            }
            throw new FolderNotFoundException(normalizedPath);
        }

        // Test readability
        try
        {
            _ = Directory.EnumerateFileSystemEntries(normalizedPath).Take(1).ToList();
        }
        catch (Exception ex)
        {
            throw new FolderUnreadableException(normalizedPath, ex);
        }

        var candidateKey = _pathKeyProvider.GetDirectoryKey(normalizedPath);
        var existingFolders = _repository.List();

        var sep = _pathKeyProvider.IsWindows ? "\\" : "/";

        foreach (var existing in existingFolders)
        {
            var existingNorm = _pathKeyProvider.NormalizePath(existing.Path);
            var existingKey = _pathKeyProvider.GetDirectoryKey(existingNorm);

            if (candidateKey.Equals(existingKey, StringComparison.Ordinal))
            {
                throw new DuplicateFolderException(normalizedPath);
            }

            // Candidate is inside existing folder
            if (candidateKey.StartsWith(existingKey + sep, StringComparison.Ordinal))
            {
                throw new NestedFolderException(normalizedPath, existing.Path, "it is inside an existing library folder");
            }

            // Existing folder is inside candidate
            if (existingKey.StartsWith(candidateKey + sep, StringComparison.Ordinal))
            {
                throw new NestedFolderException(normalizedPath, existing.Path, "it contains an existing library folder");
            }
        }

        var id = _repository.Add(normalizedPath, includePatterns);
        _logger.LogInformation("Added library folder {Id}: {Path}", id, normalizedPath);
        return id;
    }

    public void RemoveFolder(long id)
    {
        var folder = _repository.GetById(id);
        if (folder == null)
        {
            return;
        }

        _repository.Remove(id);
        _thumbnailProvider?.CleanupOrphans();
        _logger.LogInformation("Removed library folder {Id}: {Path}", id, folder.Path);
    }

    public LibraryFolder? GetById(long id) => _repository.GetById(id);

    public LibraryFolder? GetByPath(string path)
    {
        var normalized = _pathKeyProvider.NormalizePath(path);
        return _repository.GetByPath(normalized);
    }

    public IReadOnlyList<LibraryFolder> ListFolders() => _repository.List();

    public void SetEnabled(long id, bool enabled) => _repository.SetEnabled(id, enabled);

    public int GetImageCount(long id) => _repository.GetImageCount(id);
}

