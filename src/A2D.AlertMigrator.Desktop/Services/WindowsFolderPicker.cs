using System.IO;
using Microsoft.Win32;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsFolderPicker : IFolderPicker
{
    public string? PickFolder(string? initialPath, string? title = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = string.IsNullOrWhiteSpace(title)
                ? "Selecione uma pasta"
                : title,
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(initialPath) && Directory.Exists(initialPath))
        {
            dialog.InitialDirectory = initialPath;
        }

        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
