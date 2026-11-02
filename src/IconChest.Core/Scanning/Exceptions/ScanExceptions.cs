namespace IconChest.Core.Scanning.Exceptions;

public class ScanException : Exception
{
    public ScanException(string message) : base(message) { }
    public ScanException(string message, Exception inner) : base(message, inner) { }
}

public sealed class ScanAlreadyRunningException : ScanException
{
    public long FolderId { get; }
    public ScanAlreadyRunningException(long folderId)
        : base($"A scan is already actively running for library folder {folderId}.")
    {
        FolderId = folderId;
    }
}

