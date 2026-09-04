namespace A2D.AlertMigrator.Desktop.Services;

public interface IFileSavePicker
{
    string? PickFile(
        string title,
        string filter,
        string defaultExtension,
        string suggestedFileName,
        string? initialDirectory = null);
}
