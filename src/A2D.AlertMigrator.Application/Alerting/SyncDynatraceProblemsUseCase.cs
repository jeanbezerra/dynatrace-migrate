namespace A2D.AlertMigrator.Application.Alerting;

public sealed class SyncDynatraceProblemsUseCase
{
    private readonly IDynatraceProblemClient _client;
    private readonly IDynatraceProblemStore _store;

    public SyncDynatraceProblemsUseCase(IDynatraceProblemClient client, IDynatraceProblemStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<DynatraceProblemSyncResult> ExecuteAsync(
        DynatraceProblemSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var result = await _client.QueryAsync(source, cancellationToken).ConfigureAwait(false);
            return _store.SynchronizeProblems(source, result, runId, startedAt, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _store.RecordFailedProblemSync(
                source,
                runId,
                startedAt,
                DateTimeOffset.UtcNow,
                SanitizeError(exception.Message));
            throw;
        }
    }

    private static string SanitizeError(string? message)
    {
        var value = string.IsNullOrWhiteSpace(message) ? "Falha não detalhada." : message.Trim();
        return value.Length <= 2_000 ? value : value[..2_000];
    }
}
