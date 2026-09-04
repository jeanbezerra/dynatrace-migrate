namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceAlertingProfileClient
{
    Task<IReadOnlyList<DynatraceAlertingProfileSnapshot>> GetAllAsync(
        DynatraceAlertingProfileSource source,
        CancellationToken cancellationToken = default);
}
