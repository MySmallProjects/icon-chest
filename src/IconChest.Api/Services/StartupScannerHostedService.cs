using IconChest.Core.Data.Repositories;
using IconChest.Core.Scanning;
using IconChest.Core.Settings;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IconChest.Api.Services;

public sealed class StartupScannerHostedService : IHostedService
{
    private readonly ISettingsService _settingsService;
    private readonly ILibraryFolderRepository _folderRepository;
    private readonly IScanJobManager _scanJobManager;
    private readonly ILogger<StartupScannerHostedService> _logger;

    public StartupScannerHostedService(
        ISettingsService settingsService,
        ILibraryFolderRepository folderRepository,
        IScanJobManager scanJobManager,
        ILogger<StartupScannerHostedService> logger)
    {
        _settingsService = settingsService;
        _folderRepository = folderRepository;
        _scanJobManager = scanJobManager;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_settingsService.Current.ScanOnStartup)
        {
            _ = Task.Run(async () =>
            {
                // Small delay to allow host binding to complete
                await Task.Delay(500, cancellationToken);
                _logger.LogInformation("ScanOnStartup is enabled. Triggering startup scans for enabled folders...");

                var enabledFolders = _folderRepository.List().Where(f => f.Enabled);
                foreach (var folder in enabledFolders)
                {
                    try
                    {
                        _scanJobManager.StartScan(folder.Id);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to start startup scan for folder {FolderId}: {Path}", folder.Id, folder.Path);
                    }
                }
            }, cancellationToken);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
