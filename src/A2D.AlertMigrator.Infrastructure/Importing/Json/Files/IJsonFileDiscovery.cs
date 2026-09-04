using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Files;

internal interface IJsonFileDiscovery
{
    JsonFileDiscoveryResult Discover(
        JsonFolderImportOptions source,
        ImportLimits limits,
        CancellationToken cancellationToken);
}

internal sealed record JsonFileDiscoveryResult(
    string? RootPath,
    IReadOnlyList<string> Files,
    IReadOnlyList<ImportDiagnostic> Diagnostics);
