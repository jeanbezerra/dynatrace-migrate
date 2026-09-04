namespace A2D.AlertMigrator.Domain.Importing;

public sealed record EventDefinition(
    string Name,
    string? Description,
    string Type,
    string? AlertGroup);

public sealed record ProfileDefinition(
    string Name,
    string Severity,
    int DelayMinutes,
    IReadOnlyList<string> TagFilters);
