using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IconChest.Core.Settings;

public sealed class SettingsService : ISettingsService
{
    public const int CurrentSchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private readonly IAppPaths _paths;
    private readonly ILogger<SettingsService> _logger;
    private readonly object _lock = new();
    private AppSettings _current;

    public AppSettings Current
    {
        get
        {
            lock (_lock)
            {
                return _current.Clone();
            }
        }
    }

    public IAppPaths Paths => _paths;

    public event EventHandler<AppSettings>? SettingsChanged;

    public SettingsService(IAppPaths paths, ILogger<SettingsService>? logger = null)
    {
        _paths = paths;
        _logger = logger ?? NullLogger<SettingsService>.Instance;
        _current = LoadOrCreateInternal();
    }

    public AppSettings GetSettings() => Current;

    public void Update(Action<AppSettings> updateAction)
    {
        ArgumentNullException.ThrowIfNull(updateAction);

        AppSettings updated;
        lock (_lock)
        {
            var workingCopy = _current.Clone();
            updateAction(workingCopy);
            workingCopy.Validate();

            SaveInternal(workingCopy);
            _current = workingCopy;
            updated = _current.Clone();
        }

        SettingsChanged?.Invoke(this, updated);
    }

    public void Save()
    {
        lock (_lock)
        {
            _current.Validate();
            SaveInternal(_current);
        }
    }

    public void Reload()
    {
        AppSettings reloaded;
        lock (_lock)
        {
            _current = LoadOrCreateInternal();
            reloaded = _current.Clone();
        }

        SettingsChanged?.Invoke(this, reloaded);
    }

    private AppSettings LoadOrCreateInternal()
    {
        Directory.CreateDirectory(_paths.ConfigDirectory);
        var settingsPath = _paths.SettingsFilePath;
        var corruptPath = _paths.CorruptSettingsFilePath;

        if (!File.Exists(settingsPath))
        {
            var defaultSettings = new AppSettings();
            SaveInternal(defaultSettings);
            return defaultSettings;
        }

        try
        {
            var json = File.ReadAllText(settingsPath);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded == null)
            {
                throw new InvalidOperationException("Deserialized settings returned null.");
            }

            loaded.Validate();

            // Migration hook if older version
            if (loaded.Version < CurrentSchemaVersion)
            {
                loaded = MigrateSettings(loaded);
                SaveInternal(loaded);
            }

            return loaded;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings file at {SettingsPath} was corrupt or unreadable. Renaming to settings.json.corrupt and replacing with defaults.", settingsPath);

            try
            {
                if (File.Exists(corruptPath))
                {
                    File.Delete(corruptPath);
                }
                File.Move(settingsPath, corruptPath, overwrite: true);
            }
            catch (Exception moveEx)
            {
                _logger.LogWarning(moveEx, "Failed to rename corrupt settings file {SettingsPath} to {CorruptPath}", settingsPath, corruptPath);
            }

            var defaultSettings = new AppSettings();
            SaveInternal(defaultSettings);
            return defaultSettings;
        }
    }

    private AppSettings MigrateSettings(AppSettings settings)
    {
        // Simple versioned migration hook for future settings versions
        while (settings.Version < CurrentSchemaVersion)
        {
            switch (settings.Version)
            {
                default:
                    settings.Version = CurrentSchemaVersion;
                    break;
            }
        }

        return settings;
    }

    private void SaveInternal(AppSettings settings)
    {
        Directory.CreateDirectory(_paths.ConfigDirectory);
        var targetPath = _paths.SettingsFilePath;
        var tempPath = Path.Combine(_paths.ConfigDirectory, $"settings.{Guid.NewGuid():N}.tmp");

        try
        {
            var json = JsonSerializer.Serialize(settings, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, targetPath, overwrite: true);
        }
        catch
        {
            if (File.Exists(tempPath))
            {
                try { File.Delete(tempPath); } catch { /* ignore cleanup errors */ }
            }
            throw;
        }
    }
}

