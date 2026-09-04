namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceProblemClient
{
    Task<DynatraceProblemQueryResult> QueryAsync(
        DynatraceProblemSource source,
        CancellationToken cancellationToken = default);
}
