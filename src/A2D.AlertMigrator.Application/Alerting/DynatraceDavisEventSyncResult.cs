namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceDavisEventSyncResult(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    int LookbackHours,
    int ResultLimit,
    int Received,
    int Inserted,
    int Updated,
    int Unchanged,
    bool LimitReached);
