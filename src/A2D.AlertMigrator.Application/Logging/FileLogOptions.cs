using System.IO;

namespace A2D.AlertMigrator.Application.Logging;

public sealed record FileLogOptions(
    string DirectoryPath,
    ApplicationLogLevel MinimumLevel,
    bool RotationEnabled,
    long RotationSizeBytes,
    int RetainedFileCount)
{
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(DirectoryPath) || !Path.IsPathFullyQualified(DirectoryPath))
        {
            throw new ArgumentException("A pasta de logs deve possuir um caminho absoluto.", nameof(DirectoryPath));
        }

        if (!Enum.IsDefined(MinimumLevel))
        {
            throw new ArgumentOutOfRangeException(nameof(MinimumLevel), "Nível de log inválido.");
        }

        if (RotationSizeBytes is < 1_048_576 or > 1_073_741_824)
        {
            throw new ArgumentOutOfRangeException(nameof(RotationSizeBytes), "O tamanho de rotação deve estar entre 1 MiB e 1 GiB.");
        }

        if (RetainedFileCount is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(nameof(RetainedFileCount), "A retenção deve estar entre 1 e 1.000 arquivos.");
        }
    }
}
