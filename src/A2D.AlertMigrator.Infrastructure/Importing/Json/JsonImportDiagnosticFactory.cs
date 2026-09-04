using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json;

internal static class JsonImportDiagnosticFactory
{
    public static ImportDiagnostic Error(
        string code,
        string message,
        string? relativePath = null,
        string? applicationId = null) =>
        new(code, ImportDiagnosticSeverity.Error, message, relativePath, ApplicationId: applicationId);
}
