namespace IconChest.Core.Data;

public class DbMaintenanceException : Exception
{
    public DbMaintenanceException(string message) : base(message)
    {
    }

    public DbMaintenanceException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public interface IDbGate
{
    bool IsMaintenanceActive { get; }

    /// <summary>
    /// Enters a shared read lease. Throws DbMaintenanceException if maintenance is active or pending.
    /// </summary>
    IDisposable EnterRead();

    /// <summary>
    /// Asynchronously enters a shared read lease. Throws DbMaintenanceException if maintenance is active or pending.
    /// </summary>
    Task<IDisposable> EnterReadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tries to acquire a shared read lease without blocking.
    /// </summary>
    bool TryEnterRead(out IDisposable? lease);

    /// <summary>
    /// Enters exclusive maintenance mode. Blocks new readers and waits for existing readers to reach 0 up to timeout.
    /// </summary>
    Task<IDisposable> EnterMaintenanceAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
}

public sealed class DbGate : IDbGate
{
    private readonly object _syncRoot = new();
    private readonly SemaphoreSlim _maintenanceMutex = new(1, 1);
    private int _activeReaders;
    private bool _maintenanceRequested;
    private bool _isMaintenanceActive;
    private TaskCompletionSource<bool>? _readersDrainedTcs;

    public bool IsMaintenanceActive
    {
        get
        {
            lock (_syncRoot)
            {
                return _maintenanceRequested || _isMaintenanceActive;
            }
        }
    }

    public int ActiveReaders
    {
        get
        {
            lock (_syncRoot)
            {
                return _activeReaders;
            }
        }
    }

    public bool TryEnterRead(out IDisposable? lease)
    {
        lock (_syncRoot)
        {
            if (_maintenanceRequested || _isMaintenanceActive)
            {
                lease = null;
                return false;
            }

            _activeReaders++;
            lease = new ReadLease(this);
            return true;
        }
    }

    public IDisposable EnterRead()
    {
        if (TryEnterRead(out var lease) && lease != null)
        {
            return lease;
        }

        throw new DbMaintenanceException("Database is undergoing maintenance.");
    }

    public Task<IDisposable> EnterReadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EnterRead());
    }

    public async Task<IDisposable> EnterMaintenanceAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // 1. Ensure only one maintenance operation enters at a time
        var acquired = await _maintenanceMutex.WaitAsync(timeout, cancellationToken).ConfigureAwait(false);
        if (!acquired)
        {
            throw new TimeoutException("Timed out waiting to acquire database maintenance lock.");
        }

        Task waitTask;
        lock (_syncRoot)
        {
            _maintenanceRequested = true;
            _isMaintenanceActive = true;

            if (_activeReaders == 0)
            {
                return new MaintenanceLease(this);
            }

            _readersDrainedTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            waitTask = _readersDrainedTcs.Task;
        }

        // 2. Wait for active readers to drain
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(timeout);

        try
        {
            var completedTask = await Task.WhenAny(waitTask, Task.Delay(Timeout.Infinite, cts.Token)).ConfigureAwait(false);
            if (completedTask != waitTask)
            {
                throw new TimeoutException("Timed out waiting for in-flight database readers to finish.");
            }
        }
        catch (Exception)
        {
            lock (_syncRoot)
            {
                _maintenanceRequested = false;
                _isMaintenanceActive = false;
                _readersDrainedTcs = null;
            }
            _maintenanceMutex.Release();
            throw;
        }

        return new MaintenanceLease(this);
    }

    private void ReleaseRead()
    {
        lock (_syncRoot)
        {
            _activeReaders--;
            if (_activeReaders < 0)
            {
                _activeReaders = 0;
            }

            if (_activeReaders == 0 && _readersDrainedTcs != null)
            {
                _readersDrainedTcs.TrySetResult(true);
            }
        }
    }

    private void ReleaseMaintenance()
    {
        lock (_syncRoot)
        {
            _maintenanceRequested = false;
            _isMaintenanceActive = false;
            _readersDrainedTcs = null;
        }
        _maintenanceMutex.Release();
    }

    private sealed class ReadLease : IDisposable
    {
        private DbGate? _gate;

        public ReadLease(DbGate gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.ReleaseRead();
        }
    }

    private sealed class MaintenanceLease : IDisposable
    {
        private DbGate? _gate;

        public MaintenanceLease(DbGate gate)
        {
            _gate = gate;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _gate, null)?.ReleaseMaintenance();
        }
    }
}

