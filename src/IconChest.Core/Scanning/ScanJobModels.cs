namespace IconChest.Core.Scanning;

public enum ScanJobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

public sealed class ScanJobInfo
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public long FolderId { get; set; }
    public string? FolderPath { get; set; }
    public ScanJobStatus Status { get; set; } = ScanJobStatus.Pending;
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? FinishedUtc { get; set; }
    public ScanProgress? Progress { get; set; }
    public ScanResult? Result { get; set; }
    public string? ErrorMessage { get; set; }
}

public interface IScanJobManager
{
    bool HasActiveScans { get; }
    ScanJobInfo StartScan(long folderId);
    IReadOnlyList<ScanJobInfo> GetRecentJobs(int limit = 20);
    ScanJobInfo? GetJob(string jobId);
    bool CancelJob(string jobId);
}

