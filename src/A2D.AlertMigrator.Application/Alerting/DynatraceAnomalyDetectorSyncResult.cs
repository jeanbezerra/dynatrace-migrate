namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceAnomalyDetectorSyncResult(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int Received,
    int Inserted,
    int Updated,
    int Unchanged,
    int Missing,
    bool IsCompleteInventory);
