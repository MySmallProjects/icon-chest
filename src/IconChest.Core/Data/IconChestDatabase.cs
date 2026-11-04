using IconChest.Core.Common;
using IconChest.Core.Data.Migrations;
using IconChest.Core.Settings;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Data;

public sealed class IconChestDatabase : IIconChestDatabase
{
    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly MigrationRunner _migrationRunner;
    private readonly ILogger<IconChestDatabase> _logger;

    public string DatabasePath => _connectionFactory.DatabasePath;
    public int SupportedSchemaVersion => _migrationRunner.SupportedVersion;

    public IconChestDatabase(
        string databasePath,
        IClock? clock = null,
        ILogger<IconChestDatabase>? logger = null,
        IDbGate? dbGate = null)
    {
        _logger = logger ?? NullLogger<IconChestDatabase>.Instance;

        var directory = Path.GetDirectoryName(databasePath);
        if (!string.IsNullOrWhiteSpace(directory) && !databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionFactory = new DatabaseConnectionFactory(databasePath, dbGate);
        _migrationRunner = new MigrationRunner(_connectionFactory, clock);
    }

    public IconChestDatabase(
        ISettingsService settingsService,
        IClock? clock = null,
        ILogger<IconChestDatabase>? logger = null,
        IDbGate? dbGate = null)
        : this(ResolveDatabasePath(settingsService), clock, logger, dbGate)
    {
    }

    private static string ResolveDatabasePath(ISettingsService settingsService)
    {
        var settings = settingsService.Current;
        return !string.IsNullOrWhiteSpace(settings.DbPath)
            ? settings.DbPath
            : settingsService.Paths.DefaultDatabasePath;
    }

    public int GetSchemaVersion()
    {
        using var connection = _connectionFactory.CreateAndOpenConnection();
        return _migrationRunner.GetCurrentVersion(connection);
    }

    public int Migrate()
    {
        return _migrationRunner.Migrate();
    }

    public SqliteConnection OpenConnection()
    {
        return _connectionFactory.CreateAndOpenConnection();
    }
}

