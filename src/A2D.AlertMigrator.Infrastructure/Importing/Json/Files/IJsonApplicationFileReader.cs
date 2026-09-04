using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Files;

internal interface IJsonApplicationFileReader
{
    Task<ImportedApplication> ReadAsync(
        string rootPath,
        string filePath,
        ImportLimits limits,
        JsonEncodingOptions encoding,
        CancellationToken cancellationToken);
}
