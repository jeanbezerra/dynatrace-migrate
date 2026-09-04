using System.Diagnostics;
using System.IO;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsPathLauncher : IPathLauncher
{
    public void OpenFolder(string folderPath)
    {
        var fullPath = Path.GetFullPath(folderPath);
        if (!Directory.Exists(fullPath))
        {
            Directory.CreateDirectory(fullPath);
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = fullPath,
            UseShellExecute = true
        });
    }
}
