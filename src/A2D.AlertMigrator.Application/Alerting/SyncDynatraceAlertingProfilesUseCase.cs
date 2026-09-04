namespace A2D.AlertMigrator.Application.Alerting;

public sealed class SyncDynatraceAlertingProfilesUseCase
{
    private readonly IDynatraceAlertingProfileClient _client;
    private readonly IDynatraceAlertingProfileStore _store;

    public SyncDynatraceAlertingProfilesUseCase(
        IDynatraceAlertingProfileClient client,
        IDynatraceAlertingProfileStore store)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public async Task<DynatraceAlertingProfileSyncResult> ExecuteAsync(
        DynatraceAlertingProfileSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();

        var runId = Guid.NewGuid().ToString("N");
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            var profiles = await _client.GetAllAsync(source, cancellationToken).ConfigureAwait(false);
            var completedAt = DateTimeOffset.UtcNow;
            return _store.Synchronize(source, profiles, runId, startedAt, completedAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _store.RecordFailedSync(
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
