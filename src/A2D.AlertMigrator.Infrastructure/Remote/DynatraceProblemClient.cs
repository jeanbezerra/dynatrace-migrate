using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Infrastructure.Remote;

public sealed class DynatraceProblemClient : IDynatraceProblemClient
{
    private readonly DynatraceDqlQueryExecutor _executor;

    public DynatraceProblemClient(IRemoteHttpClientFactory httpClientFactory)
    {
        _executor = new DynatraceDqlQueryExecutor(httpClientFactory);
    }

    public async Task<DynatraceProblemQueryResult> QueryAsync(
        DynatraceProblemSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();
        var records = await _executor.ExecuteAsync(
            source.BaseAddress,
            source.AccessKey,
            BuildQuery(source),
            cancellationToken).ConfigureAwait(false);

        var problems = new Dictionary<string, DynatraceProblemSnapshot>(StringComparer.Ordinal);
        foreach (var rawJson in records)
        {
            using var document = JsonDocument.Parse(rawJson);
            var item = ParseProblem(document.RootElement, rawJson);
            if (!problems.TryGetValue(item.EventId, out var current)
                || Nullable.Compare(item.Timestamp, current.Timestamp) > 0)
            {
                problems[item.EventId] = item;
            }
        }

        var ordered = problems.Values
            .OrderByDescending(static item => item.Start ?? item.Timestamp)
            .ThenBy(static item => item.DisplayId, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new DynatraceProblemQueryResult(ordered, records.Count >= source.ResultLimit);
    }

    private static string BuildQuery(DynatraceProblemSource source) => $$"""
        fetch dt.davis.problems, from: -{{source.LookbackHours}}h
        | filter not(dt.davis.is_duplicate)
        | fields timestamp, event.id, display_id, event.name, event.description,
          event.category, event.status, event.severity, event.start, event.end,
          dt.davis.affected_users_count,
          affected_entity_ids = smartscape.affected_entities[][id],
          affected_entity_types = smartscape.affected_entities[][type],
          affected_service_ids = dt.smartscape.service,
          correlated_event_ids = dt.davis.event_ids,
          affected_entity_count = arraySize(smartscape.affected_entities),
          correlated_event_count = arraySize(dt.davis.event_ids),
          root_cause_entity_id, root_cause_entity_name,
          dt.davis.is_rootcause, maintenance.is_under_maintenance
        | sort event.start desc
        | limit {{source.ResultLimit}}
        """;

    private static DynatraceProblemSnapshot ParseProblem(JsonElement record, string rawJson)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A DQL Query API retornou um problema em formato inválido.");
        }

        var eventId = DynatraceDavisEventClient.ReadString(record, "event.id");
        if (eventId.Length == 0)
        {
            throw new InvalidDataException("Um problema retornado não contém 'event.id'.");
        }

        var affectedEntityIds = DynatraceDavisEventClient.ReadStringList(record, "affected_entity_ids");
        var affectedEntityTypes = DynatraceDavisEventClient.ReadStringList(record, "affected_entity_types");
        var affectedServiceIds = DynatraceDavisEventClient.ReadStringList(record, "affected_service_ids");
        var correlatedEventIds = DynatraceDavisEventClient.ReadStringList(record, "correlated_event_ids");
        var rootCauseId = DynatraceDavisEventClient.ReadString(record, "root_cause_entity_id");
        var displayId = DynatraceDavisEventClient.ReadString(record, "display_id");
        var affectedCount = DynatraceDavisEventClient.ReadInt64(record, "affected_entity_count");
        var correlatedCount = DynatraceDavisEventClient.ReadInt64(record, "correlated_event_count");

        return new DynatraceProblemSnapshot(
            eventId,
            string.IsNullOrWhiteSpace(displayId) ? eventId : displayId,
            DefaultIfEmpty(DynatraceDavisEventClient.ReadString(record, "event.name"), "Problema sem nome"),
            DynatraceDavisEventClient.ReadString(record, "event.description"),
            DynatraceDavisEventClient.ReadString(record, "event.category"),
            DynatraceDavisEventClient.ReadString(record, "event.status"),
            DynatraceDavisEventClient.ReadNullableInt32(record, "event.severity"),
            Math.Max(0, DynatraceDavisEventClient.ReadInt64(record, "dt.davis.affected_users_count")),
            ToSafeCount(affectedCount, affectedEntityIds.Count),
            ToSafeCount(correlatedCount, correlatedEventIds.Count),
            rootCauseId,
            DynatraceDavisEventClient.ReadString(record, "root_cause_entity_name"),
            ResolveEntityType(rootCauseId),
            affectedEntityIds,
            affectedEntityTypes,
            affectedServiceIds,
            correlatedEventIds,
            DynatraceDavisEventClient.ReadBoolean(record, "dt.davis.is_rootcause"),
            DynatraceDavisEventClient.ReadBoolean(record, "maintenance.is_under_maintenance"),
            DynatraceDavisEventClient.ReadTimestamp(record, "timestamp"),
            DynatraceDavisEventClient.ReadTimestamp(record, "event.start"),
            DynatraceDavisEventClient.ReadTimestamp(record, "event.end"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson))),
            rawJson);
    }

    private static int ToSafeCount(long value, int fallback) =>
        value <= 0 ? fallback : value > int.MaxValue ? int.MaxValue : (int)value;

    private static string ResolveEntityType(string entityId)
    {
        var separator = entityId.IndexOf('-');
        return separator > 0 ? entityId[..separator] : string.Empty;
    }

    private static string DefaultIfEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
