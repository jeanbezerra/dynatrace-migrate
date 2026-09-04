using System.IO;
using A2D.AlertMigrator.Application.Persistence;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record LocalDatabaseSettings(
    string FilePath,
    int BusyTimeoutSeconds = 30,
    bool UseWriteAheadLogging = true)
{
    public static LocalDatabaseSettings CreateDefault()
    {
        var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return new LocalDatabaseSettings(
            Path.Combine(localData, "A2DAlertMigrator", "data", "a2d-alert-migrator.db"));
    }

    public LocalDatabaseSettings Normalize()
    {
        var rawPath = string.IsNullOrWhiteSpace(FilePath)
            ? CreateDefault().FilePath
            : FilePath.Trim();

        if (rawPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("O caminho do banco SQLite contém caracteres inválidos.", nameof(FilePath));
        }

        var expandedPath = Environment.ExpandEnvironmentVariables(rawPath);
        if (expandedPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
        {
            throw new ArgumentException("O caminho do banco SQLite contém caracteres inválidos.", nameof(FilePath));
        }

        var normalized = this with { FilePath = Path.GetFullPath(expandedPath) };
        normalized.ToOptions().EnsureValid();
        return normalized;
    }

    public LocalDatabaseOptions ToOptions() => new(
        FilePath,
        BusyTimeoutSeconds,
        UseWriteAheadLogging);
}
