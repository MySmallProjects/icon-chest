using Xunit;

namespace IconChest.Desktop.Tests;

public class SingleInstanceGuardTests : IDisposable
{
    private readonly string _tempDir;

    public SingleInstanceGuardTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "single_instance_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch {}
        }
    }

    [Fact]
    public async Task TryAcquireAsync_FirstInstanceSucceeds_SecondFails()
    {
        var appName = "TestApp_" + Guid.NewGuid().ToString("N");

        using var guard1 = new SingleInstanceGuard(appName, _tempDir);
        using var guard2 = new SingleInstanceGuard(appName, _tempDir);

        var firstResult = await guard1.TryAcquireAsync();
        var secondResult = await guard2.TryAcquireAsync();

        Assert.True(firstResult);
        Assert.False(secondResult);
    }

    [Fact]
    public async Task TryAcquireAsync_ReleaseThenReacquire_Succeeds()
    {
        var appName = "TestApp_" + Guid.NewGuid().ToString("N");

        var guard1 = new SingleInstanceGuard(appName, _tempDir);
        var firstResult = await guard1.TryAcquireAsync();
        Assert.True(firstResult);

        guard1.Release();

        using var guard2 = new SingleInstanceGuard(appName, _tempDir);
        var secondResult = await guard2.TryAcquireAsync();
        Assert.True(secondResult);
    }

    [Fact]
    public async Task TryAcquireAsync_ConcurrentAttempts_ExactlyOneWinner()
    {
        var appName = "TestApp_" + Guid.NewGuid().ToString("N");
        const int concurrentCount = 10;
        var guards = Enumerable.Range(0, concurrentCount)
            .Select(_ => new SingleInstanceGuard(appName, _tempDir))
            .ToList();

        try
        {
            var tasks = guards.Select(g => g.TryAcquireAsync());
            var results = await Task.WhenAll(tasks);

            int winnerCount = results.Count(r => r == true);
            Assert.Equal(1, winnerCount);
        }
        finally
        {
            foreach (var g in guards) g.Dispose();
        }
    }

    [Fact]
    public async Task TryAcquireAsync_FocusSignal_IsDeliveredToPrimary()
    {
        var appName = "TestApp_" + Guid.NewGuid().ToString("N");

        using var guard1 = new SingleInstanceGuard(appName, _tempDir);
        bool focusSignaled = false;
        var focusTcs = new TaskCompletionSource<bool>();

        guard1.FocusRequested += () =>
        {
            focusSignaled = true;
            focusTcs.TrySetResult(true);
        };

        var firstAcquired = await guard1.TryAcquireAsync();
        Assert.True(firstAcquired);

        // Second instance attempts acquire and signals focus
        using var guard2 = new SingleInstanceGuard(appName, _tempDir);
        var secondAcquired = await guard2.TryAcquireAsync();
        Assert.False(secondAcquired);

        // Wait for IPC signal
        var completed = await Task.WhenAny(focusTcs.Task, Task.Delay(3000));
        Assert.Same(focusTcs.Task, completed);
        Assert.True(focusSignaled);
    }

    [Fact]
    public async Task TryAcquireAsync_StaleSocketFileFromCrash_DoesNotBlock()
    {
        var appName = "TestApp_" + Guid.NewGuid().ToString("N");
        var socketPath = Path.Combine(_tempDir, $"{appName.ToLowerInvariant()}.sock");

        // Simulate stale orphaned socket file left behind by killed process
        await File.WriteAllTextAsync(socketPath, "stale_socket_data");

        using var guard = new SingleInstanceGuard(appName, _tempDir);
        var acquired = await guard.TryAcquireAsync();

        Assert.True(acquired);
    }
}

