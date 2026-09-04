namespace A2D.AlertMigrator.Application.Alerting;

public sealed record StoredDynatraceAlertingProfile(
    string TenantKey,
    string Environment,
    string TenantAlias,
    Uri TenantBaseAddress,
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
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool IsPresent,
    string RawJson);
