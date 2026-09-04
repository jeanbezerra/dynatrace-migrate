namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceAlertingProfileSyncResult(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int Received,
    int Inserted,
    int Updated,
    int Unchanged,
    int Missing,
    bool IsCompleteInventory);
