namespace IconChest.Core.Scanning.Exceptions;

public class LibraryFolderException : Exception
{
    public LibraryFolderException(string message) : base(message) { }
    public LibraryFolderException(string message, Exception inner) : base(message, inner) { }
}

public sealed class FolderNotFoundException : LibraryFolderException
{
    public string Path { get; }
    public FolderNotFoundException(string path) : base($"Folder '{path}' does not exist.") => Path = path;
}

public sealed class NotADirectoryException : LibraryFolderException
{
    public string Path { get; }
    public NotADirectoryException(string path) : base($"Path '{path}' is not a directory.") => Path = path;
}

public sealed class FolderUnreadableException : LibraryFolderException
{
    public string Path { get; }
    public FolderUnreadableException(string path, Exception inner) : base($"Folder '{path}' is not readable.", inner) => Path = path;
}

public sealed class DuplicateFolderException : LibraryFolderException
{
    public string Path { get; }
    public DuplicateFolderException(string path) : base($"Folder '{path}' is already in the library.") => Path = path;
}

public sealed class NestedFolderException : LibraryFolderException
{
    public string Path { get; }
    public string ConflictingPath { get; }

    public NestedFolderException(string path, string conflictingPath, string reason)
        : base($"Cannot add '{path}': {reason} ('{conflictingPath}').")
    {
        Path = path;
        ConflictingPath = conflictingPath;
    }
}

