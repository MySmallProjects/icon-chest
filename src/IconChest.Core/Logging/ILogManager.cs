using Microsoft.Extensions.Logging;

namespace IconChest.Core.Logging;

public interface ILogManager : IDisposable
{
    LogComponent Component { get; }
    AppLogLevel MinimumLevel { get; set; }
    string LogDirectory { get; }
    string CurrentLogFilePath { get; }

    ILoggerFactory LoggerFactory { get; }
    ILogger<T> CreateLogger<T>();
    ILogger CreateLogger(string categoryName);

    void SetLevel(AppLogLevel level);
    void RunRetentionCleanup();
}

