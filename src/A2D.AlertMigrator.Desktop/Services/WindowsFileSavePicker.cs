using System.IO;
using Microsoft.Win32;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsFileSavePicker : IFileSavePicker
{
    public string? PickFile(
        string title,
        string filter,
        string defaultExtension,
        string suggestedFileName,
        string? initialDirectory = null)
    {
        var dialog = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            DefaultExt = defaultExtension,
            AddExtension = true,
            FileName = suggestedFileName,
            OverwritePrompt = true
        };

        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
