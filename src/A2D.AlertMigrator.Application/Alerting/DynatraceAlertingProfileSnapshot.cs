namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceAlertingProfileSnapshot(
    string RemoteObjectId,
    string SchemaId,
    string SchemaVersion,
    string Scope,
    string Name,
    string ManagementZone,
    int SeverityRuleCount,
    int EventFilterCount,
    DateTimeOffset? RemoteCreatedAt,
    DateTimeOffset? RemoteModifiedAt,
    string ContentHash,
    string RawJson);
