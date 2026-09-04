namespace A2D.AlertMigrator.Domain.Importing;

public sealed record ImportBatch(
    IReadOnlyList<ImportedApplication> Applications,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool IsValid =>
        Diagnostics.All(static diagnostic => diagnostic.Severity != ImportDiagnosticSeverity.Error)
        && Applications.All(static application => application.IsValid);

    public int RuleCount => Applications.Sum(static application => application.Document?.Rules.Count ?? 0);
}

public sealed record ImportedApplication(
    SourceSnapshot Source,
    ApplicationImportDocument? Document,
    IReadOnlyList<ImportDiagnostic> Diagnostics)
{
    public bool IsValid =>
        Document is not null
        && Diagnostics.All(static diagnostic => diagnostic.Severity != ImportDiagnosticSeverity.Error);
}
