namespace IconChest.Core.Settings;

public interface IAppPaths
{
    string ConfigDirectory { get; }
    string DataDirectory { get; }
    string SettingsFilePath { get; }
    string CorruptSettingsFilePath { get; }
    string DefaultDatabasePath { get; }
    string DefaultThumbnailCachePath { get; }
    string DefaultLogDirectory { get; }
}

