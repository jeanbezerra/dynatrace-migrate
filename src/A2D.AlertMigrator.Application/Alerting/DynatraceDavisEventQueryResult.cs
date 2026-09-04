namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceDavisEventQueryResult(
    IReadOnlyList<DynatraceDavisEventSnapshot> Events,
    bool LimitReached);
