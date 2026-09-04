using System.IO;
using A2D.AlertMigrator.Application.Logging;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record ApplicationLogSettings(
    string DirectoryPath,
    ApplicationLogLevel MinimumLevel = ApplicationLogLevel.Information,
    bool RotationEnabled = true,
    int RotationSizeMb = 25,
    int RetainedFileCount = 10)
{
    public static ApplicationLogSettings CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new ApplicationLogSettings(Path.Combine(localData, "A2DAlertMigrator", "logs"));
    }

    public ApplicationLogSettings Normalize()
    {
        var rawDirectory = string.IsNullOrWhiteSpace(DirectoryPath)
            ? CreateDefault().DirectoryPath
            : DirectoryPath.Trim();

        if (rawDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("A pasta de logs contém caracteres inválidos.", nameof(DirectoryPath));
        }

        var configuredDirectory = Environment.ExpandEnvironmentVariables(rawDirectory);
        if (configuredDirectory.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("A pasta de logs contém caracteres inválidos.", nameof(DirectoryPath));
        }

        var directory = Path.GetFullPath(configuredDirectory);

        var normalized = this with { DirectoryPath = directory };
        normalized.ToFileLogOptions().EnsureValid();
        return normalized;
    }

    public FileLogOptions ToFileLogOptions() => new(
        DirectoryPath,
        MinimumLevel,
        RotationEnabled,
        checked((long)RotationSizeMb * 1024 * 1024),
        RetainedFileCount);
}
