using Microsoft.Data.Sqlite;

namespace IconChest.Core.Data;

public interface IIconChestDatabase
{
    string DatabasePath { get; }
    int GetSchemaVersion();
    int Migrate();
    SqliteConnection OpenConnection();
}

