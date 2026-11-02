namespace IconChest.Core.Scanning;

public enum ScanStage
{
    Starting,
    Enumerating,
    Reading,
    Reconciling,
    Finalising,
    Completed,
    Failed,
    Cancelled
}

public sealed record ScanProgress(
    ScanStage Stage,
    int FilesFound,
    int FilesProcessed,
    int NewFiles,
    int ChangedFiles,
    int UnchangedFiles,
    int MovedFiles,
    int MissingFiles,
    int Duplicates,
    int Errors,
    string? CurrentPath);

public sealed class ScanResult
{
    public long FolderId { get; set; }
    public string FolderPath { get; set; } = string.Empty;
    public TimeSpan Duration { get; set; }
    public bool IsCancelled { get; set; }
    public bool IsIncomplete { get; set; }
    public bool IsFatalError { get; set; }
    public string? FatalErrorMessage { get; set; }

    public int FilesFound;
    public int FilesProcessed;
    public int NewFiles;
    public int ChangedFiles;
    public int UnchangedFiles;
    public int MovedFiles;
    public int MissingFiles;
    public int Duplicates;
    public int Errors;
    public int ThumbnailsEnqueued;

    public List<string> WarningsAndErrors { get; } = new();
}
