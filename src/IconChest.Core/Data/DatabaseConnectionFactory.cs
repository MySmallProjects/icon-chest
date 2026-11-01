using Microsoft.Data.Sqlite;

namespace IconChest.Core.Data;

public interface IDatabaseConnectionFactory
{
    string DatabasePath { get; }
    SqliteConnection CreateConnection();
    SqliteConnection CreateAndOpenConnection();
}

public sealed class DatabaseConnectionFactory : IDatabaseConnectionFactory
{
    private readonly string _connectionString;
    private readonly bool _isMemory;

    public string DatabasePath { get; }

    public DatabaseConnectionFactory(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = databasePath;

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
        return new SqliteConnection(_connectionString);
    }

    public SqliteConnection CreateAndOpenConnection()
    {
        var connection = CreateConnection();
        connection.Open();

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
}

