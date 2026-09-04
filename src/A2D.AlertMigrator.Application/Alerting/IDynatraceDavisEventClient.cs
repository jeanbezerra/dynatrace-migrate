namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceDavisEventClient
{
    Task<DynatraceDavisEventQueryResult> QueryAsync(
        DynatraceDavisEventSource source,
        CancellationToken cancellationToken = default);
}
