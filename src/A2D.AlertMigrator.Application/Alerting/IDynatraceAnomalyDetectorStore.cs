namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceAnomalyDetectorStore
{
    IReadOnlyList<StoredDynatraceAnomalyDetector> GetAnomalyDetectors(
        string tenantKey,
        bool includeMissing = false);

    DynatraceAnomalyDetectorSyncStatus? GetLatestAnomalyDetectorSync(string tenantKey);

    DynatraceAnomalyDetectorSyncResult SynchronizeAnomalyDetectors(
        DynatraceAnomalyDetectorSource source,
        IReadOnlyList<DynatraceAnomalyDetectorSnapshot> detectors,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt);

    void RecordFailedAnomalyDetectorSync(
        DynatraceAnomalyDetectorSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage);
}
