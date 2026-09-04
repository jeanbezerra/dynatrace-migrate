using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Infrastructure.Remote;

public sealed class DynatraceDavisEventClient : IDynatraceDavisEventClient
{
    private readonly DynatraceDqlQueryExecutor _executor;

    public DynatraceDavisEventClient(IRemoteHttpClientFactory httpClientFactory)
    {
        _executor = new DynatraceDqlQueryExecutor(httpClientFactory);
    }

    public async Task<DynatraceDavisEventQueryResult> QueryAsync(
        DynatraceDavisEventSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();
        var records = await _executor.ExecuteAsync(
            source.BaseAddress,
            source.AccessKey,
            BuildQuery(source),
            cancellationToken).ConfigureAwait(false);

        var events = new Dictionary<string, DynatraceDavisEventSnapshot>(StringComparer.Ordinal);
        foreach (var rawJson in records)
        {
            using var document = JsonDocument.Parse(rawJson);
            var item = ParseEvent(document.RootElement, rawJson);
            if (!events.TryGetValue(item.EventId, out var current)
                || Nullable.Compare(item.Timestamp, current.Timestamp) > 0)
            {
                events[item.EventId] = item;
            }
        }

        var ordered = events.Values
            .OrderByDescending(static item => item.Start ?? item.Timestamp)
            .ThenBy(static item => item.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
        return new DynatraceDavisEventQueryResult(ordered, records.Count >= source.ResultLimit);
    }

    private static string BuildQuery(DynatraceDavisEventSource source) => $$"""
        fetch dt.davis.events, from: -{{source.LookbackHours}}h
        | fields timestamp, event.id, event.name, event.description, event.category,
          event.status, event.status_transition, event.severity, event.provider, event.type,
          event.start, event.end, dt.smartscape_source.id, dt.smartscape_source.type,
          dt.settings.object_id, dt.settings.schema_id, dt.alert_group, dt.query,
          dt.davis.is_frequent_event, dt.davis.is_merging_allowed,
          maintenance.is_under_maintenance
        | sort event.start desc
        | limit {{source.ResultLimit}}
        """;

    private static DynatraceDavisEventSnapshot ParseEvent(JsonElement record, string rawJson)
    {
        if (record.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("A DQL Query API retornou um evento em formato inválido.");
        }

        var eventId = ReadString(record, "event.id");
        if (eventId.Length == 0)
        {
            throw new InvalidDataException("Um Davis Event retornado não contém 'event.id'.");
        }

        return new DynatraceDavisEventSnapshot(
            eventId,
            DefaultIfEmpty(ReadString(record, "event.name"), "Evento sem nome"),
            ReadString(record, "event.description"),
            ReadString(record, "event.category"),
            ReadString(record, "event.status"),
            ReadString(record, "event.status_transition"),
            ReadNullableInt32(record, "event.severity"),
            ReadString(record, "event.provider"),
            ReadString(record, "event.type"),
            ReadString(record, "dt.smartscape_source.id"),
            ReadString(record, "dt.smartscape_source.type"),
            ReadString(record, "dt.settings.object_id"),
            ReadString(record, "dt.settings.schema_id"),
            ReadString(record, "dt.alert_group"),
            ReadString(record, "dt.query"),
            ReadBoolean(record, "dt.davis.is_frequent_event"),
            ReadBoolean(record, "dt.davis.is_merging_allowed"),
            ReadBoolean(record, "maintenance.is_under_maintenance"),
            ReadTimestamp(record, "timestamp"),
            ReadTimestamp(record, "event.start"),
            ReadTimestamp(record, "event.end"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson))),
            rawJson);
    }

    internal static string ReadString(JsonElement item, string propertyName)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(propertyName, out var value))
        {
            return string.Empty;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? string.Empty,
            JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => value.GetRawText(),
            _ => string.Empty
        };
    }

    internal static int? ReadNullableInt32(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && int.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : null;
    }

    internal static long ReadInt64(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return 0;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
                ? number
                : 0;
    }

    internal static bool ReadBoolean(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True
            || value.ValueKind == JsonValueKind.String
            && bool.TryParse(value.GetString(), out var parsed)
            && parsed;
    }

    internal static DateTimeOffset? ReadTimestamp(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString();
            if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            {
                return parsed;
            }

            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            {
                return FromUnixValue(numeric);
            }
        }

        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epoch)
            ? FromUnixValue(epoch)
            : null;
    }

    internal static IReadOnlyList<string> ReadStringList(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            var single = value.GetString();
            return string.IsNullOrWhiteSpace(single) ? [] : [single];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.String ? entry.GetString() : entry.GetRawText())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Select(static entry => entry!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static DateTimeOffset? FromUnixValue(long value)
    {
        try
        {
            if (Math.Abs(value) >= 50_000_000_000_000_000L)
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(value / 1_000_000L);
            }

            return Math.Abs(value) >= 50_000_000_000L
                ? DateTimeOffset.FromUnixTimeMilliseconds(value)
                : DateTimeOffset.FromUnixTimeSeconds(value);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static string DefaultIfEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
