using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IconChest.Api.Services;

public sealed class ParentProcessMonitorService : IHostedService, IDisposable
{
    private readonly int? _parentPid;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ParentProcessMonitorService> _logger;
    private Timer? _timer;

    public ParentProcessMonitorService(
        int? parentPid,
        IHostApplicationLifetime lifetime,
        ILogger<ParentProcessMonitorService> logger)
    {
        _parentPid = parentPid;
        _lifetime = lifetime;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_parentPid.HasValue && _parentPid.Value > 0)
        {
            _logger.LogInformation("Parent process monitoring active for PID {ParentPid}.", _parentPid.Value);
            _timer = new Timer(CheckParentProcess, null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
        }

        return Task.CompletedTask;
    }

    private void CheckParentProcess(object? state)
    {
        if (!_parentPid.HasValue) return;

        try
        {
            var process = Process.GetProcessById(_parentPid.Value);
            if (process.HasExited)
            {
                _logger.LogWarning("Parent process {ParentPid} has exited. Initiating API shutdown...", _parentPid.Value);
                _lifetime.StopApplication();
            }
        }
        catch (ArgumentException)
        {
            // Process no longer exists
            _logger.LogWarning("Parent process {ParentPid} is not found. Initiating API shutdown...", _parentPid.Value);
            _lifetime.StopApplication();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Exception checking parent process status.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}

