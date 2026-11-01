namespace IconChest.Core.Versioning;

/// <summary>
/// Provides application version, build timestamp, and executable location information.
/// </summary>
public interface IVersionInfo
{
    string Version { get; }
    DateTime? BuildDateUtc { get; }
    string ExecutablePath { get; }
    string ExecutableDirectory { get; }
}

