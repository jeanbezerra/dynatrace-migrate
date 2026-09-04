namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceAnomalyDetectorClient
{
    Task<IReadOnlyList<DynatraceAnomalyDetectorSnapshot>> GetAllAsync(
        DynatraceAnomalyDetectorSource source,
        CancellationToken cancellationToken = default);
}
