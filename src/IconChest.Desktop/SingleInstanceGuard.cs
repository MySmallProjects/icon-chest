using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace IconChest.Desktop;

public sealed class SingleInstanceGuard : IDisposable
{
    private const string FocusMessage = "focus\n";
    private readonly string _appName;
    private readonly string _baseDir;

    // Linux resources
    private FileStream? _lockStream;
    private Socket? _unixSocket;
    private string? _socketPath;
    private string? _lockPath;

    // Windows resources
    private Mutex? _windowsMutex;
    private NamedPipeServerStream? _pipeServer;
    private CancellationTokenSource? _ipcTokenSource;

    private bool _isPrimary;
    private bool _disposed;

    public event Action? FocusRequested;

    public bool IsPrimary => _isPrimary;

    public SingleInstanceGuard(string appName = "IconChest", string? baseDirectory = null)
    {
        _appName = appName;
        _baseDir = baseDirectory ?? ResolveRuntimeDirectory();
    }

    private static string ResolveRuntimeDirectory()
    {
        var xdgRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrWhiteSpace(xdgRuntime) && Directory.Exists(xdgRuntime))
        {
            return xdgRuntime;
        }

        var temp = Path.GetTempPath();
        return temp;
    }

    public async Task<bool> TryAcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(SingleInstanceGuard));

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return TryAcquireWindows();
        }
        else
        {
            return await TryAcquireUnixAsync(cancellationToken);
        }
    }

    private bool TryAcquireWindows()
    {
        var userName = Environment.UserName.Replace('\\', '_');
        var mutexName = $"Global\\{_appName}_{userName}";

        try
        {
            _windowsMutex = new Mutex(true, mutexName, out bool createdNew);
            if (!createdNew)
            {
                // Another instance holds the mutex; notify it to focus
                SignalWindowsFocus(userName);
                _windowsMutex.Dispose();
                _windowsMutex = null;
                return false;
            }

            _isPrimary = true;
            StartWindowsPipeListener(userName);
            return true;
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed and abandoned the mutex; we acquire it now
            _isPrimary = true;
            StartWindowsPipeListener(userName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void SignalWindowsFocus(string userName)
    {
        var pipeName = $"{_appName}_Focus_{userName}";
        try
        {
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out);
            client.Connect(2000);
            var bytes = Encoding.UTF8.GetBytes(FocusMessage);
            client.Write(bytes, 0, bytes.Length);
            client.Flush();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to signal existing Windows instance: {0}", ex.Message);
        }
    }

    private void StartWindowsPipeListener(string userName)
    {
        _ipcTokenSource = new CancellationTokenSource();
        var token = _ipcTokenSource.Token;
        var pipeName = $"{_appName}_Focus_{userName}";

        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    _pipeServer = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await _pipeServer.WaitForConnectionAsync(token);

                    var buffer = new byte[64];
                    var bytesRead = await _pipeServer.ReadAsync(buffer, token);
                    var msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (msg.StartsWith("focus", StringComparison.OrdinalIgnoreCase))
                    {
                        FocusRequested?.Invoke();
                    }

                    _pipeServer.Disconnect();
                    _pipeServer.Dispose();
                    _pipeServer = null;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning("Error in Windows named pipe listener: {0}", ex.Message);
                }
            }
        }, token);
    }

    private async Task<bool> TryAcquireUnixAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(_baseDir);
        var cleanAppName = _appName.ToLowerInvariant().Replace(' ', '_');
        _lockPath = Path.Combine(_baseDir, $"{cleanAppName}.lock");
        _socketPath = Path.Combine(_baseDir, $"{cleanAppName}.sock");

        try
        {
            // Open file and acquire exclusive lock
            _lockStream = new FileStream(
                _lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None);

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                _lockStream.Lock(0, 0);
            }
        }
        catch (IOException)
        {
            // Lock held by another active process; signal it to focus
            await SignalUnixFocusAsync(_socketPath, cancellationToken);
            _lockStream?.Dispose();
            _lockStream = null;
            return false;
        }

        _isPrimary = true;

        // Clean up stale socket file if left over from a previous crash
        if (File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch {}
        }

        StartUnixSocketListener(_socketPath);
        return true;
    }

    private static async Task SignalUnixFocusAsync(string socketPath, CancellationToken cancellationToken)
    {
        if (!File.Exists(socketPath)) return;

        try
        {
            using var client = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            var endpoint = new UnixDomainSocketEndPoint(socketPath);
            await client.ConnectAsync(endpoint, linked.Token);

            var bytes = Encoding.UTF8.GetBytes(FocusMessage);
            await client.SendAsync(bytes, SocketFlags.None, linked.Token);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to signal existing Unix instance: {0}", ex.Message);
        }
    }

    private void StartUnixSocketListener(string socketPath)
    {
        _ipcTokenSource = new CancellationTokenSource();
        var token = _ipcTokenSource.Token;

        try
        {
            _unixSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            _unixSocket.Bind(new UnixDomainSocketEndPoint(socketPath));
            _unixSocket.Listen(5);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Failed to bind Unix domain socket for IPC: {0}", ex.Message);
            return;
        }

        _ = Task.Run(async () =>
        {
            var buffer = new byte[64];
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var handler = await _unixSocket.AcceptAsync(token);
                    var bytesRead = await handler.ReceiveAsync(buffer, SocketFlags.None, token);
                    var msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    if (msg.StartsWith("focus", StringComparison.OrdinalIgnoreCase))
                    {
                        FocusRequested?.Invoke();
                    }

                    handler.Shutdown(SocketShutdown.Both);
                    handler.Dispose();
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    Trace.TraceWarning("Error in Unix domain socket listener: {0}", ex.Message);
                }
            }
        }, token);
    }

    public void Release()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _ipcTokenSource?.Cancel();
        _ipcTokenSource?.Dispose();
        _ipcTokenSource = null;

        // Windows cleanup
        _pipeServer?.Dispose();
        _pipeServer = null;

        if (_windowsMutex != null)
        {
            try { _windowsMutex.ReleaseMutex(); } catch {}
            _windowsMutex.Dispose();
            _windowsMutex = null;
        }

        // Unix cleanup
        if (_unixSocket != null)
        {
            try { _unixSocket.Close(); } catch {}
            _unixSocket.Dispose();
            _unixSocket = null;
        }

        if (!string.IsNullOrEmpty(_socketPath) && File.Exists(_socketPath))
        {
            try { File.Delete(_socketPath); } catch {}
        }

        if (_lockStream != null)
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    _lockStream.Unlock(0, 0);
                }
            }
            catch {}
            _lockStream.Dispose();
            _lockStream = null;
        }

        _isPrimary = false;
    }
}
