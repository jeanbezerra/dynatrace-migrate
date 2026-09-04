namespace A2D.AlertMigrator.Application.Alerting;

public sealed class SyncDynatraceAnomalyDetectorsUseCase
{
    private readonly IDynatraceAnomalyDetectorClient _client;
    private readonly IDynatraceAnomalyDetectorStore _store;

    public SyncDynatraceAnomalyDetectorsUseCase(
        IDynatraceAnomalyDetectorClient client,
        IDynatraceAnomalyDetectorStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<DynatraceAnomalyDetectorSyncResult> ExecuteAsync(
        DynatraceAnomalyDetectorSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();
        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;

        try
        {
            var detectors = await _client.GetAllAsync(source, cancellationToken).ConfigureAwait(false);
            return _store.SynchronizeAnomalyDetectors(
                source,
                detectors,
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
            _store.RecordFailedAnomalyDetectorSync(
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
