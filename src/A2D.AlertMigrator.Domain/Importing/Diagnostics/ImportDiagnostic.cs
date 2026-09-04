namespace A2D.AlertMigrator.Domain.Importing;

public enum ImportDiagnosticSeverity
{
    Information,
    Warning,
    Error
}

public sealed record ImportDiagnostic(
    string Code,
    ImportDiagnosticSeverity Severity,
    string Message,
    string? RelativePath = null,
    string? JsonPointer = null,
    string? ApplicationId = null,
    string? RuleId = null,
    long? ByteOffset = null);
