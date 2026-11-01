namespace IconChest.Core.Settings;

public interface ISettingsService
{
    AppSettings Current { get; }
    IAppPaths Paths { get; }

    event EventHandler<AppSettings>? SettingsChanged;

    AppSettings GetSettings();
    void Update(Action<AppSettings> updateAction);
    void Save();
    void Reload();
}

