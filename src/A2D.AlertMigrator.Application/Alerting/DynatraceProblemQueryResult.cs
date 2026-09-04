namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceProblemQueryResult(
    IReadOnlyList<DynatraceProblemSnapshot> Problems,
    bool LimitReached);
