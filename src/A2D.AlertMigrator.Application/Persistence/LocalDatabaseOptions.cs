using System.IO;

namespace A2D.AlertMigrator.Application.Persistence;

public sealed record LocalDatabaseOptions(
    string FilePath,
    int BusyTimeoutSeconds,
    bool UseWriteAheadLogging)
{
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(FilePath) || !Path.IsPathFullyQualified(FilePath))
        {
            throw new ArgumentException("O arquivo SQLite deve possuir um caminho absoluto.", nameof(FilePath));
        }

        if (BusyTimeoutSeconds is < 1 or > 300)
        {
            throw new ArgumentOutOfRangeException(nameof(BusyTimeoutSeconds), "O timeout do SQLite deve estar entre 1 e 300 segundos.");
        }
    }
}
