using System.Globalization;
using System.Text.RegularExpressions;
using IconChest.Core.Common;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace IconChest.Core.Logging;

public sealed class AppLogging : ILogManager
{
    private const string OutputTemplate = "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";
    private const int RetentionDays = 30;

    private readonly LoggingLevelSwitch _levelSwitch;
    private readonly Serilog.ILogger _serilogLogger;
    private readonly ILoggerFactory _loggerFactory;
    private readonly IClock _clock;
    private readonly string _prefix;
    private AppLogLevel _currentLevel;
    private bool _disposed;

    public LogComponent Component { get; }
    public string LogDirectory { get; }

    public AppLogLevel MinimumLevel
    {
        get => _currentLevel;
        set => SetLevel(value);
    }

    public string CurrentLogFilePath
    {
        get
        {
            var dateStr = _clock.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return Path.Combine(LogDirectory, $"{_prefix}-{dateStr}.log");
        }
    }

    public ILoggerFactory LoggerFactory => _loggerFactory;

    public AppLogging(
        LogComponent component,
        string logDirectory,
        AppLogLevel initialLevel = AppLogLevel.Information,
        IClock? clock = null)
    {
        Component = component;
        LogDirectory = logDirectory;
        _clock = clock ?? SystemClock.Instance;
        _prefix = component == LogComponent.Api ? "api" : "app";
        _currentLevel = initialLevel;

        Directory.CreateDirectory(LogDirectory);

        _levelSwitch = new LoggingLevelSwitch(MapLevel(initialLevel));

        var logFilePathTemplate = Path.Combine(LogDirectory, $"{_prefix}-.log");

        var config = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(_levelSwitch)
            .Filter.ByExcluding(_ => _currentLevel == AppLogLevel.Off)
            .WriteTo.File(
                path: logFilePathTemplate,
                rollingInterval: RollingInterval.Day,
                outputTemplate: OutputTemplate,
                shared: true);

        _serilogLogger = config.CreateLogger();
        _loggerFactory = new SerilogLoggerFactory(_serilogLogger, dispose: true);

        // Run retention cleanup on startup
        RunRetentionCleanup();
    }

    public void SetLevel(AppLogLevel level)
    {
        _currentLevel = level;
        _levelSwitch.MinimumLevel = MapLevel(level);
    }

    public void RunRetentionCleanup()
    {
        try
        {
            if (!Directory.Exists(LogDirectory))
            {
                return;
            }

            var pattern = new Regex($@"^{Regex.Escape(_prefix)}-(\d{{8}})\.log$", RegexOptions.IgnoreCase);
            var today = _clock.TodayUtc;

            foreach (var filePath in Directory.GetFiles(LogDirectory, $"{_prefix}-*.log"))
            {
                var fileName = Path.GetFileName(filePath);
                var match = pattern.Match(fileName);
                if (!match.Success)
                {
                    continue;
                }

                var datePart = match.Groups[1].Value;
                if (!DateTime.TryParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fileDate))
                {
                    continue;
                }

                var age = (today - fileDate.Date).TotalDays;
                if (age > RetentionDays)
                {
                    try
                    {
                        File.Delete(filePath);
                    }
                    catch (Exception ex)
                    {
                        // Never throw if file is locked or cannot be deleted; log a warning instead
                        _serilogLogger.Warning(ex, "Failed to delete expired log file {FilePath} during retention cleanup", filePath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _serilogLogger.Warning(ex, "Retention cleanup failed in {LogDirectory}", LogDirectory);
        }
    }

    public Microsoft.Extensions.Logging.ILogger<T> CreateLogger<T>() =>
        _loggerFactory.CreateLogger<T>();

    public Microsoft.Extensions.Logging.ILogger CreateLogger(string categoryName) =>
        _loggerFactory.CreateLogger(categoryName);

    private static LogEventLevel MapLevel(AppLogLevel level) => level switch
    {
        AppLogLevel.Off => (LogEventLevel)100, // Beyond Fatal so nothing logs
        AppLogLevel.Error => LogEventLevel.Error,
        AppLogLevel.Warning => LogEventLevel.Warning,
        AppLogLevel.Information => LogEventLevel.Information,
        AppLogLevel.Debug => LogEventLevel.Debug,
        AppLogLevel.Trace => LogEventLevel.Verbose,
        _ => LogEventLevel.Information
    };

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _loggerFactory.Dispose();
        if (_serilogLogger is IDisposable disposableSerilog)
        {
            disposableSerilog.Dispose();
        }
    }
}

