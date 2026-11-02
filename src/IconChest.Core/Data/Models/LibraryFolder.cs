namespace IconChest.Core.Data.Models;

public sealed class LibraryFolder
{
    public long Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
    public string? LastScanUtc { get; set; }
    public string? IncludePatterns { get; set; }
}

