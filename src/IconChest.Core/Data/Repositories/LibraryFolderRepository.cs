using Dapper;
using IconChest.Core.Data.Models;
using Microsoft.Data.Sqlite;

namespace IconChest.Core.Data.Repositories;

public sealed class LibraryFolderRepository : ILibraryFolderRepository
{
    private readonly IDatabaseConnectionFactory _connectionFactory;

    /// <summary>
    /// Phase 3 hook: Invoked when a library folder is removed so disk thumbnail cleanup can run.
    /// </summary>
    public static event Action<long>? OnFolderRemoved;

    public LibraryFolderRepository(IDatabaseConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public long Add(string path, string? includePatterns = null)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.QuerySingle<long>(@"
            INSERT INTO library_folder (path, enabled, include_patterns)
            VALUES (@path, 1, @includePatterns)
            RETURNING id;", new { path, includePatterns });
    }

    public LibraryFolder? GetById(long id)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.QuerySingleOrDefault<LibraryFolder>(@"
            SELECT id, path, enabled, last_scan_utc AS LastScanUtc, include_patterns AS IncludePatterns
            FROM library_folder
            WHERE id = @id;", new { id });
    }

    public LibraryFolder? GetByPath(string path)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.QuerySingleOrDefault<LibraryFolder>(@"
            SELECT id, path, enabled, last_scan_utc AS LastScanUtc, include_patterns AS IncludePatterns
            FROM library_folder
            WHERE path = @path;", new { path });
    }

    public IReadOnlyList<LibraryFolder> List()
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.Query<LibraryFolder>(@"
            SELECT id, path, enabled, last_scan_utc AS LastScanUtc, include_patterns AS IncludePatterns
            FROM library_folder
            ORDER BY id ASC;").ToList();
    }

    public void SetEnabled(long id, bool enabled)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        conn.Execute("UPDATE library_folder SET enabled = @enabled WHERE id = @id;", new { id, enabled = enabled ? 1 : 0 });
    }

    public void UpdateLastScan(long id, string lastScanUtc)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        conn.Execute("UPDATE library_folder SET last_scan_utc = @lastScanUtc WHERE id = @id;", new { id, lastScanUtc });
    }

    public void Remove(long id)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        using var tx = conn.BeginTransaction();
        try
        {
            conn.Execute("DELETE FROM library_folder WHERE id = @id;", new { id }, transaction: tx);
            tx.Commit();

            // Phase 3 thumbnail cleanup hook
            OnFolderRemoved?.Invoke(id);
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    public int GetImageCount(long folderId)
    {
        using var conn = _connectionFactory.CreateAndOpenConnection();
        return conn.ExecuteScalar<int>("SELECT COUNT(*) FROM image WHERE folder_id = @folderId;", new { folderId });
    }
}

