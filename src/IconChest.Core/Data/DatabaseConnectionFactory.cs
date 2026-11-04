using System.Text;
using IconChest.Core.Common;
using Microsoft.Data.Sqlite;

namespace IconChest.Core.Data;

public interface IDatabaseConnectionFactory
{
    string DatabasePath { get; }
    IDbGate? DbGate { get; }
    SqliteConnection CreateConnection();
    SqliteConnection CreateAndOpenConnection();
}

public sealed class GatedSqliteConnection : SqliteConnection
{
    private IDisposable? _lease;

    public GatedSqliteConnection(string connectionString, IDisposable? lease) : base(connectionString)
    {
        _lease = lease;
    }

    protected override void Dispose(bool disposing)
    {
        try
        {
            base.Dispose(disposing);
        }
        finally
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
        }
    }

    public override async ValueTask DisposeAsync()
    {
        try
        {
            await base.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Exchange(ref _lease, null)?.Dispose();
        }
    }
}

public sealed class DatabaseConnectionFactory : IDatabaseConnectionFactory
{
    private readonly string _connectionString;
    private readonly bool _isMemory;
    private readonly IDbGate? _dbGate;

    public string DatabasePath { get; }
    public IDbGate? DbGate => _dbGate;

    public DatabaseConnectionFactory(string databasePath, IDbGate? dbGate = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;
        _dbGate = dbGate;

        _isMemory = databasePath.Equals(":memory:", StringComparison.OrdinalIgnoreCase) ||
                    databasePath.StartsWith("Data Source=:memory:", StringComparison.OrdinalIgnoreCase);

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = _isMemory ? SqliteOpenMode.Memory : SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Default
        };

        _connectionString = builder.ConnectionString;
    }

    public SqliteConnection CreateConnection()
    {
        IDisposable? lease = null;
        if (_dbGate != null)
        {
            lease = _dbGate.EnterRead();
        }

        return new GatedSqliteConnection(_connectionString, lease);
    }

    public SqliteConnection CreateAndOpenConnection()
    {
        var connection = CreateConnection();
        try
        {
            connection.Open();

            // Register custom deterministic SQLite functions for query language
            RegisterFunctions(connection);

            using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = "PRAGMA foreign_keys = ON;";
                cmd.ExecuteNonQuery();

                if (!_isMemory)
                {
                    cmd.CommandText = "PRAGMA journal_mode = WAL;";
                    cmd.ExecuteNonQuery();
                }
            }

            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    public static void RegisterFunctions(SqliteConnection connection)
    {
        connection.CreateFunction("iq_eq", (string? a, string? b) =>
        {
            if (a is null && b is null) return true;
            if (a is null || b is null) return false;
            var normA = a.Normalize(NormalizationForm.FormC);
            var normB = b.Normalize(NormalizationForm.FormC);
            return string.Equals(normA, normB, StringComparison.InvariantCultureIgnoreCase);
        }, isDeterministic: true);

        connection.CreateFunction("iq_contains", (string? target, string? substr) =>
        {
            if (target is null || substr is null) return false;
            return target.Normalize(NormalizationForm.FormC)
                         .Contains(substr.Normalize(NormalizationForm.FormC), StringComparison.InvariantCultureIgnoreCase);
        }, isDeterministic: true);

        connection.CreateFunction("iq_like", (string? value, string? pattern) =>
        {
            return WildcardMatcher.IsMatchWithEscapes(value, pattern);
        }, isDeterministic: true);
    }
}
