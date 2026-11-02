using System.Text;

namespace IconChest.Core.Scanning;

public sealed class PathKeyProvider : IPathKeyProvider
{
    public static readonly PathKeyProvider Default = new(OperatingSystem.IsWindows());

    public bool IsWindows { get; }

    public PathKeyProvider(bool? isWindows = null)
    {
        IsWindows = isWindows ?? OperatingSystem.IsWindows();
    }

    public string NormalizePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        // Normalize slashes according to current/simulated OS
        var full = Path.GetFullPath(path);

        // Form C Unicode normalization
        var nfc = full.Normalize(NormalizationForm.FormC);

        // Remove trailing separators unless root path
        if (nfc.Length > 1)
        {
            var isRoot = Path.GetPathRoot(nfc) == nfc;
            if (!isRoot)
            {
                nfc = nfc.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        return nfc;
    }

    public string GetPathKey(string fullPath)
    {
        var normalized = NormalizePath(fullPath);
        return IsWindows ? normalized.ToUpperInvariant() : normalized;
    }

    public string GetDirectoryKey(string directoryPath)
    {
        return GetPathKey(directoryPath);
    }

    public (string PathKey, string DirectoryPath, string DirectoryKey, string FileName, string Extension) Analyze(string filePath)
    {
        var normalized = NormalizePath(filePath);
        var pathKey = GetPathKey(normalized);
        var dir = Path.GetDirectoryName(normalized) ?? string.Empty;
        var dirKey = GetDirectoryKey(dir);
        var fileName = Path.GetFileName(normalized);
        var ext = Path.GetExtension(normalized);

        return (pathKey, dir, dirKey, fileName, ext);
    }
}

