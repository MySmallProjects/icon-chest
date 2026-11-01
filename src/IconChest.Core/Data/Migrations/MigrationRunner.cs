using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Dapper;
using IconChest.Core.Common;
using IconChest.Core.Data.Exceptions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Data.Migrations;

public sealed record MigrationScript(int Version, string Description, string ResourceName, string Sql);

public sealed class MigrationRunner
{
    private static readonly Regex ScriptPattern = new(@"^.*?(\d{4})_([^.]+)\.sql$", RegexOptions.IgnoreCase);

    private readonly IDatabaseConnectionFactory _connectionFactory;
    private readonly IClock _clock;
    private readonly ILogger<MigrationRunner> _logger;
    private readonly List<MigrationScript> _scripts;

    public int SupportedVersion => _scripts.Count > 0 ? _scripts[^1].Version : 0;
    public IReadOnlyList<MigrationScript> Scripts => _scripts;

    public MigrationRunner(
        IDatabaseConnectionFactory connectionFactory,
        IClock? clock = null,
        ILogger<MigrationRunner>? logger = null)
    {
        _connectionFactory = connectionFactory;
        _clock = clock ?? SystemClock.Instance;
        _logger = logger ?? NullLogger<MigrationRunner>.Instance;
        _scripts = LoadEmbeddedScripts();
    }

    public int GetCurrentVersion(SqliteConnection connection)
    {
        // Check if schema_version table exists
        var tableExists = connection.ExecuteScalar<int>(
            "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'schema_version';") > 0;

        if (!tableExists)
        {
            return 0;
        }

        var currentVersion = connection.ExecuteScalar<int?>(
            "SELECT MAX(version) FROM schema_version;");

        return currentVersion ?? 0;
    }

    public int Migrate()
    {
        using var connection = _connectionFactory.CreateAndOpenConnection();
        return Migrate(connection);
    }

    public int Migrate(SqliteConnection connection)
    {
        var currentVersion = GetCurrentVersion(connection);

        if (currentVersion > SupportedVersion)
        {
            throw new SchemaVersionException(currentVersion, SupportedVersion);
        }

        var pending = _scripts.Where(s => s.Version > currentVersion).OrderBy(s => s.Version).ToList();
        if (pending.Count == 0)
        {
            _logger.LogDebug("Database is already up to date at schema version {Version}", currentVersion);
            return currentVersion;
        }

        foreach (var script in pending)
        {
            _logger.LogInformation("Applying migration {Version}: {Description}", script.Version, script.Description);
            using var transaction = connection.BeginTransaction();
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.Transaction = transaction;
                cmd.CommandText = script.Sql;
                cmd.ExecuteNonQuery();

                using var recordCmd = connection.CreateCommand();
                recordCmd.Transaction = transaction;
                recordCmd.CommandText = @"
                    INSERT INTO schema_version (version, applied_utc, description)
                    VALUES (@version, @appliedUtc, @description);";
                recordCmd.Parameters.AddWithValue("@version", script.Version);
                recordCmd.Parameters.AddWithValue("@appliedUtc", _clock.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                recordCmd.Parameters.AddWithValue("@description", script.Description);
                recordCmd.ExecuteNonQuery();

                transaction.Commit();
                currentVersion = script.Version;
            }
            catch (Exception ex)
            {
                transaction.Rollback();
                _logger.LogError(ex, "Failed applying migration {Version}: {Description}. Rolling back transaction.", script.Version, script.Description);
                throw;
            }
        }

        return currentVersion;
    }

    private static List<MigrationScript> LoadEmbeddedScripts()
    {
        var assembly = typeof(MigrationRunner).Assembly;
        var names = assembly.GetManifestResourceNames();
        var scripts = new List<MigrationScript>();

        foreach (var name in names)
        {
            var match = ScriptPattern.Match(name);
            if (!match.Success)
            {
                continue;
            }

            var version = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            var description = match.Groups[2].Value.Replace('_', ' ');

            using var stream = assembly.GetManifestResourceStream(name);
            if (stream == null) continue;
            using var reader = new StreamReader(stream);
            var sql = reader.ReadToEnd();

            scripts.Add(new MigrationScript(version, description, name, sql));
        }

        scripts.Sort((a, b) => a.Version.CompareTo(b.Version));
        return scripts;
    }
}

