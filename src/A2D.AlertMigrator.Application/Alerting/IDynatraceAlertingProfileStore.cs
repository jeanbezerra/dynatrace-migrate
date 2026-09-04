namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceAlertingProfileStore
{
    IReadOnlyList<StoredDynatraceAlertingProfile> GetProfiles(string tenantKey, bool includeMissing = false);

    DynatraceAlertingProfileSyncStatus? GetLatestSync(string tenantKey);

    DynatraceAlertingProfileSyncResult Synchronize(
        DynatraceAlertingProfileSource source,
        IReadOnlyList<DynatraceAlertingProfileSnapshot> profiles,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt);

    void RecordFailedSync(
        DynatraceAlertingProfileSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage);
}
