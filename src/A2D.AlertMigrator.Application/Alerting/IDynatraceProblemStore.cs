namespace A2D.AlertMigrator.Application.Alerting;

public interface IDynatraceProblemStore
{
    IReadOnlyList<StoredDynatraceProblem> GetProblems(string tenantKey);

    DynatraceProblemSyncStatus? GetLatestProblemSync(string tenantKey);

    DynatraceProblemSyncResult SynchronizeProblems(
        DynatraceProblemSource source,
        DynatraceProblemQueryResult queryResult,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt);

    void RecordFailedProblemSync(
        DynatraceProblemSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage);
}
