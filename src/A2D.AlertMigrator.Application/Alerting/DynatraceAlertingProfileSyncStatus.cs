namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceAlertingProfileSyncStatus(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int Received,
    int Inserted,
    int Updated,
    int Unchanged,
    int Missing,
    bool IsCompleteInventory,
    string ErrorMessage);
