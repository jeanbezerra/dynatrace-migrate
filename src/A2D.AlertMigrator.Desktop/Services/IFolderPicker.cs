namespace A2D.AlertMigrator.Desktop.Services;

public interface IFolderPicker
{
    string? PickFolder(string? initialPath, string? title = null);
}
