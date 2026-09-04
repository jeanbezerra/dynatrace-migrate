namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceDavisEventSyncStatus(
    string RunId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string Status,
    int LookbackHours,
    int ResultLimit,
    int Received,
    int Inserted,
    int Updated,
    int Unchanged,
    bool LimitReached,
    string ErrorMessage);
