using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Desktop;

public sealed class ApiProcessManager : IAsyncDisposable
{
    private readonly ILogger _logger;
    private readonly TimeSpan _readinessTimeout;
    private readonly TimeSpan _shutdownTimeout;

    private Process? _process;
    private IntPtr? _jobHandle;
    private Uri? _apiUri;
    private string? _secret;
    private readonly List<string> _stderrLines = new();
    private readonly object _stderrLock = new();
    private readonly SemaphoreSlim _stopLock = new(1, 1);

    public Uri? ApiUri => _apiUri;
    public string? Secret => _secret;
    public bool IsRunning => _process != null && !_process.HasExited;
    public int? ProcessId => _process?.Id;

    public IReadOnlyList<string> StderrLines
    {
        get
        {
            lock (_stderrLock) return _stderrLines.ToList();
        }
    }

    public ApiProcessManager(
        ILogger? logger = null,
        TimeSpan? readinessTimeout = null,
        TimeSpan? shutdownTimeout = null)
    {
        _logger = logger ?? NullLogger.Instance;
        _readinessTimeout = readinessTimeout ?? TimeSpan.FromSeconds(30);
        _shutdownTimeout = shutdownTimeout ?? TimeSpan.FromSeconds(5);
    }

    public static string ResolveApiPath()
    {
        // 1. Environment variable override
        var envPath = Environment.GetEnvironmentVariable("ICONCHEST_API_PATH");
        if (!string.IsNullOrWhiteSpace(envPath) && File.Exists(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        // 2. api/ subfolder beside shell
        var baseDir = AppContext.BaseDirectory;
        var directCandidates = new[]
        {
            Path.Combine(baseDir, "api", "IconChest.Api.dll"),
            Path.Combine(baseDir, "api", "IconChest.Api"),
            Path.Combine(baseDir, "api", "IconChest.Api.exe"),
        };

        foreach (var c in directCandidates)
        {
            if (File.Exists(c))
            {
                return Path.GetFullPath(c);
            }
        }

        // Development fallback: walk up looking for src/IconChest.Api
        var dir = new DirectoryInfo(baseDir);
        while (dir != null)
        {
            var apiDllDebug = Path.Combine(dir.FullName, "src", "IconChest.Api", "bin", "Debug", "net10.0", "IconChest.Api.dll");
            if (File.Exists(apiDllDebug))
            {
                return Path.GetFullPath(apiDllDebug);
            }

            var apiDllRelease = Path.Combine(dir.FullName, "src", "IconChest.Api", "bin", "Release", "net10.0", "IconChest.Api.dll");
            if (File.Exists(apiDllRelease))
            {
                return Path.GetFullPath(apiDllRelease);
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate IconChest.Api binary. Ensure it is placed in 'api/' or set ICONCHEST_API_PATH.");
    }

    public async Task<Uri> StartAsync(
        string? explicitApiPath = null,
        string? secret = null,
        IDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        var apiPath = explicitApiPath ?? ResolveApiPath();
        _secret = secret ?? SecretGenerator.GenerateSecret();

        var psi = new ProcessStartInfo();

        if (apiPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            psi.FileName = "dotnet";
            psi.Arguments = $"\"{apiPath}\" --port 0 --parent-pid {Environment.ProcessId}";
        }
        else
        {
            psi.FileName = apiPath;
            psi.Arguments = $"--port 0 --parent-pid {Environment.ProcessId}";
        }

        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WorkingDirectory = Path.GetDirectoryName(apiPath) ?? AppContext.BaseDirectory;

        // Pass secret in environment variable (never on command line)
        psi.EnvironmentVariables["ICONCHEST_API_SECRET"] = _secret;

        // Inherit path overrides if present
        var cfgDir = Environment.GetEnvironmentVariable("ICONCHEST_CONFIG_DIR");
        if (!string.IsNullOrEmpty(cfgDir)) psi.EnvironmentVariables["ICONCHEST_CONFIG_DIR"] = cfgDir;

        var dataDir = Environment.GetEnvironmentVariable("ICONCHEST_DATA_DIR");
        if (!string.IsNullOrEmpty(dataDir)) psi.EnvironmentVariables["ICONCHEST_DATA_DIR"] = dataDir;

        if (environmentVariables != null)
        {
            foreach (var kvp in environmentVariables)
            {
                psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        _process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
            {
                lock (_stderrLock) _stderrLines.Add(e.Data);
                _logger.LogDebug("[api-child] {Data}", e.Data);
            }
        };

        if (!_process.Start())
        {
            throw new InvalidOperationException($"Failed to start child process: {psi.FileName}");
        }

        _process.BeginErrorReadLine();

        // Assign to Windows Job Object if supported
        _jobHandle = JobObject.CreateKillOnCloseJob(_process);

        // Read stdout line by line until readiness line
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linkedCts.CancelAfter(_readinessTimeout);

        Uri? detectedUri = null;
        try
        {
            while (!linkedCts.Token.IsCancellationRequested)
            {
                var readLineTask = _process.StandardOutput.ReadLineAsync(linkedCts.Token).AsTask();
                var line = await readLineTask;

                if (line == null)
                {
                    // Process stdout closed early
                    break;
                }

                detectedUri = ReadinessLineParser.TryParse(line);
                if (detectedUri != null)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Timed out or cancelled
        }

        if (detectedUri == null)
        {
            // Terminate child if failed to start
            await StopAsync();
            var errSample = string.Join("\n", StderrLines.TakeLast(10));
            throw new TimeoutException($"API child process did not emit readiness signal within {_readinessTimeout.TotalSeconds}s.\nStderr:\n{errSample}");
        }

        // Verify GET /health
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var healthUrl = new Uri(detectedUri, "/health");
        try
        {
            var res = await httpClient.GetAsync(healthUrl, cancellationToken);
            if (!res.IsSuccessStatusCode)
            {
                await StopAsync();
                throw new InvalidOperationException($"API health check at {healthUrl} returned status {res.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            await StopAsync();
            throw new InvalidOperationException($"Failed to contact API health check at {healthUrl}: {ex.Message}", ex);
        }

        _apiUri = detectedUri;
        return _apiUri;
    }

    public async Task<Uri> RestartAsync(
        string? explicitApiPath = null,
        string? secret = null,
        IDictionary<string, string>? environmentVariables = null,
        CancellationToken cancellationToken = default)
    {
        await StopAsync().ConfigureAwait(false);
        return await StartAsync(explicitApiPath, secret, environmentVariables, cancellationToken).ConfigureAwait(false);
    }

    public async Task StopAsync()
    {
        await _stopLock.WaitAsync();
        try
        {
            var proc = _process;
            if (proc == null || proc.HasExited)
            {
                return;
            }

            // 1. Attempt graceful shutdown via POST /api/system/shutdown
            if (_apiUri != null && !string.IsNullOrEmpty(_secret))
            {
                try
                {
                    using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

                    // Log in to acquire session cookie
                    var loginRes = await httpClient.PostAsJsonAsync(
                        new Uri(_apiUri, "/api/session"),
                        new { secret = _secret });

                    if (loginRes.IsSuccessStatusCode)
                    {
                        // Call shutdown endpoint
                        await httpClient.PostAsync(new Uri(_apiUri, "/api/system/shutdown"), null);
                    }
                }
                catch
                {
                    // Ignore failure during shutdown attempt
                }
            }

            // 2. Wait up to shutdown timeout
            try
            {
                using var waitCts = new CancellationTokenSource(_shutdownTimeout);
                await proc.WaitForExitAsync(waitCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Process did not exit in time
            }

            // 3. Kill process tree if still running
            if (!proc.HasExited)
            {
                try
                {
                    proc.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Failed to kill API child process tree: {0}", ex.Message);
                }
            }

            if (_jobHandle.HasValue)
            {
                JobObject.CloseJob(_jobHandle.Value);
                _jobHandle = null;
            }

            proc.Dispose();
            _process = null;
        }
        finally
        {
            _stopLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
    }
}
