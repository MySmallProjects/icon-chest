namespace IconChest.Core.Data.Exceptions;

public sealed class SchemaVersionException : Exception
{
    public int DatabaseVersion { get; }
    public int SupportedVersion { get; }

    public SchemaVersionException(int databaseVersion, int supportedVersion)
        : base($"Database schema version {databaseVersion} is newer than the supported version {supportedVersion}. Update IconChest to open this database.")
    {
        DatabaseVersion = databaseVersion;
        SupportedVersion = supportedVersion;
    }
}

