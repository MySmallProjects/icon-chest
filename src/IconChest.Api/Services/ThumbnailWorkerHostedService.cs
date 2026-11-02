using IconChest.Core.Settings;
using IconChest.Core.Thumbnails;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IconChest.Api.Services;

public sealed class ThumbnailWorkerHostedService : IHostedService, IDisposable
{
    private readonly IThumbnailWorker _worker;
    private readonly IThumbnailProvider _provider;
    private readonly ISettingsService _settingsService;
    private readonly ILogger<ThumbnailWorkerHostedService> _logger;
    private Timer? _flushTimer;

    public ThumbnailWorkerHostedService(
        IThumbnailWorker worker,
        IThumbnailProvider provider,
        ISettingsService settingsService,
        ILogger<ThumbnailWorkerHostedService> logger)
    {
        _worker = worker;
        _provider = provider;
        _settingsService = settingsService;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting ThumbnailWorker hosted service...");
        _worker.Start();

        // Flush access logs and run cache eviction check periodically (every 30 seconds)
        _flushTimer = new Timer(_ =>
        {
            try
            {
                _provider.FlushAccessLog();
                _provider.RunEviction();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error in periodic thumbnail access log / eviction flush.");
            }
        }, null, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30));

        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping ThumbnailWorker hosted service...");
        _flushTimer?.Change(Timeout.Infinite, 0);

        try
        {
            await _worker.StopAsync();
            _provider.FlushAccessLog();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping thumbnail worker.");
        }
    }

    public void Dispose()
    {
        _flushTimer?.Dispose();
    }
}

