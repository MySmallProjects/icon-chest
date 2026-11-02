namespace IconChest.Core.Scanning;

public interface IPathKeyProvider
{
    bool IsWindows { get; }
    string NormalizePath(string path);
    string GetPathKey(string fullPath);
    string GetDirectoryKey(string directoryPath);
    (string PathKey, string DirectoryPath, string DirectoryKey, string FileName, string Extension) Analyze(string filePath);
}

