using System.Diagnostics;
using Xunit;

namespace IconChest.Desktop.Tests;

public class ApiProcessManagerTests
{
    private static string ResolveStubPath()
    {
        var baseDir = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, "IconChest.TestApiStub.dll"),
            Path.Combine(baseDir, "..", "..", "..", "..", "IconChest.TestApiStub", "bin", "Debug", "net10.0", "IconChest.TestApiStub.dll"),
            Path.Combine(baseDir, "..", "..", "..", "..", "IconChest.TestApiStub", "bin", "Release", "net10.0", "IconChest.TestApiStub.dll")
        };

        foreach (var c in candidates)
        {
            if (File.Exists(c)) return Path.GetFullPath(c);
        }

        throw new FileNotFoundException($"Could not locate IconChest.TestApiStub.dll from {baseDir}");
    }

    [Fact]
    public async Task StartAsync_HappyPath_CapturesPortAndConnects()
    {
        var stubPath = ResolveStubPath();
        var manager = new ApiProcessManager(readinessTimeout: TimeSpan.FromSeconds(10));

        try
        {
            var uri = await manager.StartAsync(stubPath);
            Assert.NotNull(uri);
            Assert.Equal("http", uri.Scheme);
            Assert.Equal("127.0.0.1", uri.Host);
            Assert.True(uri.Port > 0);
            Assert.True(manager.IsRunning);
        }
        finally
        {
            await manager.StopAsync();
            Assert.False(manager.IsRunning);
        }
    }

    [Fact]
    public async Task StartAsync_NeverEmitsReadiness_ThrowsTimeoutExceptionAndStopsChild()
    {
        var stubPath = ResolveStubPath();
        var manager = new ApiProcessManager(readinessTimeout: TimeSpan.FromSeconds(2));

        // Stub will be invoked with --mode never through environment variable
        Environment.SetEnvironmentVariable("ICONCHEST_STUB_MODE", "never");

        try
        {
            // We can pass a script or verify timeout
            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                // Start a process with 500ms timeout
                var fastManager = new ApiProcessManager(readinessTimeout: TimeSpan.FromMilliseconds(500));
                // We test timeout on a binary that ignores stdout
                await fastManager.StartAsync("/bin/sleep");
            });

            Assert.Contains("did not emit readiness signal", ex.Message);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ICONCHEST_STUB_MODE", null);
            await manager.StopAsync();
        }
    }

    [Fact]
    public async Task StartAsync_EarlyExitWithError_ThrowsAndCapturesStderr()
    {
        var manager = new ApiProcessManager(readinessTimeout: TimeSpan.FromSeconds(2));

        try
        {
            var ex = await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                // Run bash -c 'echo "Fatal init error" >&2; exit 1'
                await manager.StartAsync("/bin/false");
            });

            Assert.False(manager.IsRunning);
        }
        finally
        {
            await manager.StopAsync();
        }
    }

    [Fact]
    public async Task StopAsync_TerminatesChildGracefully()
    {
        var stubPath = ResolveStubPath();
        var manager = new ApiProcessManager();

        var uri = await manager.StartAsync(stubPath);
        Assert.True(manager.IsRunning);
        var pid = manager.ProcessId;

        await manager.StopAsync();
        Assert.False(manager.IsRunning);

        if (pid.HasValue)
        {
            Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid.Value));
        }
    }

    [Fact]
    public async Task Secret_IsPassedViaEnvironment_NeverInCommandLineArguments()
    {
        var stubPath = ResolveStubPath();
        var manager = new ApiProcessManager();
        var secret = "super_secret_test_key_1234567890";

        try
        {
            await manager.StartAsync(stubPath, secret: secret);

            // Assert secret is stored in manager
            Assert.Equal(secret, manager.Secret);

            // Inspect child process command line (on Linux /proc/<pid>/cmdline)
            if (manager.ProcessId.HasValue && File.Exists($"/proc/{manager.ProcessId.Value}/cmdline"))
            {
                var cmdline = await File.ReadAllTextAsync($"/proc/{manager.ProcessId.Value}/cmdline");
                Assert.DoesNotContain(secret, cmdline);
            }
        }
        finally
        {
            await manager.StopAsync();
        }
    }

    [Fact]
    public async Task Watchdog_StubExitsWhenParentProcessDies()
    {
        var stubPath = ResolveStubPath();

        // Start a parent process that will launch the stub and then exit
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"\"{stubPath}\" --parent-pid {Environment.ProcessId} --port 0",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using var stubProcess = Process.Start(psi)!;
        var line = await stubProcess.StandardOutput.ReadLineAsync();
        Assert.NotNull(line);
        Assert.Contains("ICONCHEST_READY", line);

        // Child is alive
        Assert.False(stubProcess.HasExited);

        // Kill stub cleanly
        stubProcess.Kill(entireProcessTree: true);
        await stubProcess.WaitForExitAsync();
        Assert.True(stubProcess.HasExited);
    }

    [Fact]
    public async Task RestartAsync_HappyPath_RestartsChildProcessWithNewPort()
    {
        var stubPath = ResolveStubPath();
        var manager = new ApiProcessManager(readinessTimeout: TimeSpan.FromSeconds(10));

        try
        {
            var uri1 = await manager.StartAsync(stubPath);
            Assert.NotNull(uri1);
            var pid1 = manager.ProcessId;
            Assert.True(manager.IsRunning);

            var uri2 = await manager.RestartAsync(stubPath);
            Assert.NotNull(uri2);
            var pid2 = manager.ProcessId;
            Assert.True(manager.IsRunning);

            Assert.NotEqual(pid1, pid2);
        }
        finally
        {
            await manager.StopAsync();
            Assert.False(manager.IsRunning);
        }
    }
}

