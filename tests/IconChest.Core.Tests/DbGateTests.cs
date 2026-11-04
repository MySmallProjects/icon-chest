using IconChest.Core.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace IconChest.Core.Tests;

public sealed class DbGateTests
{
    [Fact]
    public void SharedReaders_CanEnterConcurrently()
    {
        var gate = new DbGate();

        using var lease1 = gate.EnterRead();
        using var lease2 = gate.EnterRead();

        Assert.Equal(2, gate.ActiveReaders);
        Assert.False(gate.IsMaintenanceActive);
    }

    [Fact]
    public async Task Maintenance_WaitsForReadersToDrain()
    {
        var gate = new DbGate();

        var lease = gate.EnterRead();
        Assert.Equal(1, gate.ActiveReaders);

        var maintenanceStarted = new TaskCompletionSource<bool>();
        var maintenanceAcquired = new TaskCompletionSource<bool>();

        var maintenanceTask = Task.Run(async () =>
        {
            maintenanceStarted.SetResult(true);
            using var maintLease = await gate.EnterMaintenanceAsync(TimeSpan.FromSeconds(5));
            maintenanceAcquired.SetResult(true);
        });

        await maintenanceStarted.Task;
        await Task.Delay(50);

        // While maintenance is waiting, IsMaintenanceActive is true and new readers are rejected
        Assert.True(gate.IsMaintenanceActive);
        Assert.False(gate.TryEnterRead(out _));
        Assert.Throws<DbMaintenanceException>(() => gate.EnterRead());

        // Release the active reader
        lease.Dispose();

        // Maintenance now completes
        await maintenanceAcquired.Task;
        await maintenanceTask;

        // After maintenance lease finishes, gate is open again
        Assert.False(gate.IsMaintenanceActive);
        using var newLease = gate.EnterRead();
        Assert.NotNull(newLease);
    }

    [Fact]
    public async Task Maintenance_TimesOutIfReadersDoNotDrain()
    {
        var gate = new DbGate();

        using var lease = gate.EnterRead();

        await Assert.ThrowsAsync<TimeoutException>(async () =>
        {
            await gate.EnterMaintenanceAsync(TimeSpan.FromMilliseconds(50));
        });

        // Maintenance flag was cleared on timeout
        Assert.False(gate.IsMaintenanceActive);
    }

    [Fact]
    public async Task StressTest_ConcurrentReadsWritesAndBackup()
    {
        var tempDb = Path.Combine(Path.GetTempPath(), $"iconchest-gate-stress-{Guid.NewGuid():N}.db");
        var backupPath = Path.Combine(Path.GetTempPath(), $"iconchest-gate-backup-{Guid.NewGuid():N}.db");

        var gate = new DbGate();
        var factory = new DatabaseConnectionFactory(tempDb, gate);

        // Initialize DB table
        using (var initConn = factory.CreateAndOpenConnection())
        {
            using var cmd = initConn.CreateCommand();
            cmd.CommandText = "CREATE TABLE counter (id INTEGER PRIMARY KEY, val INTEGER); INSERT INTO counter VALUES (1, 0);";
            cmd.ExecuteNonQuery();
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var token = cts.Token;

        var readCount = 0;
        var writeCount = 0;

        // Task 1: Continuous writers
        var writer = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var conn = factory.CreateAndOpenConnection();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE counter SET val = val + 1 WHERE id = 1;";
                    cmd.ExecuteNonQuery();
                    Interlocked.Increment(ref writeCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Expected during maintenance or concurrent SQLite busy
                }
                await Task.Delay(1, token).ConfigureAwait(false);
            }
        }, token);

        // Task 2: Continuous readers
        var reader = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var conn = factory.CreateAndOpenConnection();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT val FROM counter WHERE id = 1;";
                    var val = cmd.ExecuteScalar();
                    Assert.NotNull(val);
                    Interlocked.Increment(ref readCount);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Expected during maintenance or concurrent SQLite busy
                }
                await Task.Delay(1, token).ConfigureAwait(false);
            }
        }, token);

        // Task 3: Perform an online backup concurrently
        await Task.Delay(100);
        using (var readLease = gate.EnterRead())
        using (var source = factory.CreateConnection())
        {
            source.Open();
            using var dest = new SqliteConnection($"Data Source={backupPath};Mode=ReadWriteCreate;");
            dest.Open();
            source.BackupDatabase(dest);
        }

        Assert.True(File.Exists(backupPath));

        // Task 4: Perform brief maintenance
        using (var maint = await gate.EnterMaintenanceAsync(TimeSpan.FromSeconds(2)))
        {
            Assert.True(gate.IsMaintenanceActive);
            await Task.Delay(50);
        }

        cts.Cancel();
        try { await Task.WhenAll(writer, reader); } catch (OperationCanceledException) { }

        Assert.True(writeCount > 0);
        Assert.True(readCount > 0);

        try { File.Delete(tempDb); } catch { }
        try { File.Delete(tempDb + "-wal"); } catch { }
        try { File.Delete(tempDb + "-shm"); } catch { }
        try { File.Delete(backupPath); } catch { }
    }
}

