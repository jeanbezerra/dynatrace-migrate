namespace A2D.AlertMigrator.Domain.Importing;

public sealed record ApplicationImportDocument(
    string SchemaVersion,
    ApplicationIdentity Application,
    IReadOnlyList<CanonicalAlertRule> Rules);

public sealed record ApplicationIdentity(
    string Id,
    string Name,
    string? Description,
    IReadOnlyList<string> Owners,
    IReadOnlyDictionary<string, string> Labels);
