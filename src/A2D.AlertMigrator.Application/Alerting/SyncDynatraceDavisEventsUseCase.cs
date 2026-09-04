namespace A2D.AlertMigrator.Application.Alerting;

public sealed class SyncDynatraceDavisEventsUseCase
{
    private readonly IDynatraceDavisEventClient _client;
    private readonly IDynatraceDavisEventStore _store;

    public SyncDynatraceDavisEventsUseCase(
        IDynatraceDavisEventClient client,
        IDynatraceDavisEventStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<DynatraceDavisEventSyncResult> ExecuteAsync(
        DynatraceDavisEventSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var queryResult = await _client.QueryAsync(source, cancellationToken).ConfigureAwait(false);
            return _store.SynchronizeDavisEvents(
                source,
                queryResult,
                runId,
                startedAt,
                DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _store.RecordFailedDavisEventSync(
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
