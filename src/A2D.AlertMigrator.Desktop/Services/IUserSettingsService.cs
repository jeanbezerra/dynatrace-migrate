using A2D.AlertMigrator.Desktop.Configuration;

namespace A2D.AlertMigrator.Desktop.Services;

public interface IUserSettingsService
{
    event EventHandler? SettingsChanged;

    UserSettings Current { get; }

    string StoragePath { get; }

    void Save(UserSettings settings);

    void Reset();
}
