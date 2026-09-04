namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceDavisEventStore
{
    IReadOnlyList<StoredDynatraceDavisEvent> GetDavisEvents(string tenantKey);

    DynatraceDavisEventSyncStatus? GetLatestDavisEventSync(string tenantKey);

    DynatraceDavisEventSyncResult SynchronizeDavisEvents(
        DynatraceDavisEventSource source,
        DynatraceDavisEventQueryResult queryResult,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt);

    void RecordFailedDavisEventSync(
        DynatraceDavisEventSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage);
}
