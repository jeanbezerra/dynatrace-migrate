namespace A2D.AlertMigrator.Desktop.Services;

public interface IFileOpenPicker
{
    string? PickFile(string title, string filter, string? initialDirectory = null);
}
