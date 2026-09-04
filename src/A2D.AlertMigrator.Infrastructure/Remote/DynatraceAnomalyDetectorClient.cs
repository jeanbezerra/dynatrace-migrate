using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Infrastructure.Remote;

public sealed class DynatraceAnomalyDetectorClient : IDynatraceAnomalyDetectorClient
{
    private const string SchemaId = "builtin:davis.anomaly-detectors";
    private const int MaximumPages = 10_000;
    private readonly IRemoteHttpClientFactory _httpClientFactory;

    public DynatraceAnomalyDetectorClient(IRemoteHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<IReadOnlyList<DynatraceAnomalyDetectorSnapshot>> GetAllAsync(
        DynatraceAnomalyDetectorSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();

        using var client = _httpClientFactory.CreateClient();
        var results = new Dictionary<string, DynatraceAnomalyDetectorSnapshot>(StringComparer.Ordinal);
        var seenPageKeys = new HashSet<string>(StringComparer.Ordinal);
        Uri? address = BuildFirstPageAddress(source);

        for (var page = 1; address is not null && page <= MaximumPages; page++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, address);
            ApplyAuthentication(request, source);
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if ((int)response.StatusCode is >= 300 and <= 399)
            {
                throw new HttpRequestException(
                    $"A Settings API respondeu HTTP {(int)response.StatusCode} e solicitou redirecionamento. " +
                    "Use diretamente a URL final do ambiente para proteger a credencial.",
                    inner: null,
                    response.StatusCode);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw await CreateApiExceptionAsync(response, source.RequestAdminAccess, cancellationToken)
                    .ConfigureAwait(false);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (!document.RootElement.TryGetProperty("items", out var items)
                || items.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidDataException("A resposta da Settings API não contém a coleção 'items'.");
            }

            foreach (var item in items.EnumerateArray())
            {
                var detector = ParseDetector(item);
                if (!results.TryAdd(detector.RemoteObjectId, detector))
                {
                    throw new InvalidDataException(
                        $"A Settings API retornou o detector '{detector.RemoteObjectId}' mais de uma vez.");
                }
            }

            var nextPageKey = ReadString(document.RootElement, "nextPageKey");
            if (string.IsNullOrWhiteSpace(nextPageKey))
            {
                address = null;
                continue;
            }

            if (!seenPageKeys.Add(nextPageKey))
            {
                throw new InvalidDataException("A Settings API repetiu a chave de paginação.");
            }

            address = BuildSettingsAddress(
                source.BaseAddress,
                $"nextPageKey={Uri.EscapeDataString(nextPageKey)}");
        }

        if (address is not null)
        {
            throw new InvalidDataException($"A Settings API excedeu o limite de {MaximumPages:N0} páginas.");
        }

        return results.Values
            .OrderBy(static detector => detector.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static DynatraceAnomalyDetectorSnapshot ParseDetector(JsonElement item)
    {
        var objectId = ReadRequiredString(item, "objectId");
        var value = item.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Object
                ? valueElement
                : default;
        var analyzer = value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("analyzer", out var analyzerElement)
            && analyzerElement.ValueKind == JsonValueKind.Object
                ? analyzerElement
                : default;
        var analyzerName = ReadString(analyzer, "name");
        var inputs = ReadKeyValueCollection(analyzer, "input");
        var eventTemplate = value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("eventTemplate", out var eventElement)
            && eventElement.ValueKind == JsonValueKind.Object
                ? eventElement
                : default;
        var eventProperties = ReadKeyValueCollection(eventTemplate, "properties");
        var executionSettings = value.ValueKind == JsonValueKind.Object
            && value.TryGetProperty("executionSettings", out var executionElement)
            && executionElement.ValueKind == JsonValueKind.Object
                ? executionElement
                : default;
        var query = GetValue(inputs, "query.expression", "query");
        var rawJson = item.GetRawText();

        return new DynatraceAnomalyDetectorSnapshot(
            objectId,
            DefaultIfEmpty(ReadString(item, "schemaId"), SchemaId),
            ReadString(item, "schemaVersion"),
            ReadString(item, "scope"),
            DefaultIfEmpty(ReadString(value, "title"), "Detector sem título"),
            ReadString(value, "description"),
            ReadString(value, "source"),
            ReadBoolean(value, "enabled"),
            analyzerName,
            ResolveModel(analyzerName),
            query,
            StartsWithTimeseries(query),
            GetValue(eventProperties, "event.type"),
            GetValue(eventProperties, "event.name"),
            GetValue(eventProperties, "dt.alert_group"),
            ReadString(executionSettings, "actor"),
            inputs.Count,
            eventProperties.Count,
            ReadTimestamp(item, "created"),
            ReadTimestamp(item, "modified"),
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson))),
            rawJson);
    }

    private static IReadOnlyDictionary<string, string> ReadKeyValueCollection(
        JsonElement parent,
        string propertyName)
    {
        if (parent.ValueKind != JsonValueKind.Object
            || !parent.TryGetProperty(propertyName, out var collection)
            || collection.ValueKind != JsonValueKind.Array)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in collection.EnumerateArray())
        {
            var key = ReadString(item, "key");
            if (key.Length > 0)
            {
                result[key] = ReadString(item, "value");
            }
        }

        return result;
    }

    private static string GetValue(IReadOnlyDictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(key, out var value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static string ResolveModel(string analyzerName)
    {
        if (analyzerName.Contains("StaticThreshold", StringComparison.OrdinalIgnoreCase))
        {
            return "Estático";
        }

        if (analyzerName.Contains("AutoAdaptive", StringComparison.OrdinalIgnoreCase))
        {
            return "Adaptativo";
        }

        if (analyzerName.Contains("SeasonalBaseline", StringComparison.OrdinalIgnoreCase))
        {
            return "Sazonal";
        }

        if (analyzerName.Contains("RecordAnomaly", StringComparison.OrdinalIgnoreCase))
        {
            return "Registros";
        }

        return string.IsNullOrWhiteSpace(analyzerName) ? "Não informado" : "Outro";
    }

    private static bool StartsWithTimeseries(string query)
    {
        foreach (var line in query.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var trimmed = line.TrimStart();
            if (trimmed.Length == 0 || trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            return trimmed.StartsWith("timeseries", StringComparison.OrdinalIgnoreCase)
                && (trimmed.Length == "timeseries".Length || char.IsWhiteSpace(trimmed["timeseries".Length]));
        }

        return false;
    }

    private static bool ReadBoolean(JsonElement item, string propertyName) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.True;

    private static DateTimeOffset? ReadTimestamp(JsonElement item, string propertyName)
    {
        if (!item.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var epochMilliseconds))
        {
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds(epochMilliseconds);
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        return value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp)
            ? timestamp
            : null;
    }

    private static string ReadRequiredString(JsonElement item, string propertyName)
    {
        var value = ReadString(item, propertyName);
        return value.Length > 0
            ? value
            : throw new InvalidDataException($"Um detector retornado não contém '{propertyName}'.");
    }

    private static string ReadString(JsonElement item, string propertyName) =>
        item.ValueKind == JsonValueKind.Object
        && item.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static string DefaultIfEmpty(string value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static Uri BuildFirstPageAddress(DynatraceAnomalyDetectorSource source)
    {
        var query = $"schemaIds={Uri.EscapeDataString(SchemaId)}&pageSize=500";
        if (source.RequestAdminAccess)
        {
            query += "&adminAccess=true";
        }

        return BuildSettingsAddress(source.BaseAddress, query);
    }

    private static Uri BuildSettingsAddress(Uri baseAddress, string query)
    {
        var endpoint = new Uri(new Uri(baseAddress.AbsoluteUri.TrimEnd('/') + "/"), "api/v2/settings/objects");
        return new UriBuilder(endpoint) { Query = query }.Uri;
    }

    private static void ApplyAuthentication(
        HttpRequestMessage request,
        DynatraceAnomalyDetectorSource source)
    {
        request.Headers.Authorization = source.AuthenticationMode switch
        {
            RemoteAuthenticationMode.BearerToken => new AuthenticationHeaderValue("Bearer", source.AccessKey),
            RemoteAuthenticationMode.DynatraceApiToken => new AuthenticationHeaderValue("Api-Token", source.AccessKey),
            _ => throw new ArgumentOutOfRangeException(nameof(source), "Tipo de autenticação incompatível com o Dynatrace.")
        };
    }

    private static async Task<HttpRequestException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        bool requestedAdminAccess,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var detail = ExtractErrorMessage(body);
        var prefix = response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "A autenticação foi rejeitada. Confira o tipo e o valor do token.",
            HttpStatusCode.Forbidden when requestedAdminAccess =>
                "A API negou o inventário administrativo. Conceda 'settings:objects:admin' ou desative a visão administrativa.",
            HttpStatusCode.Forbidden =>
                "A API negou a leitura. Conceda 'settings:objects:read' e revise as políticas IAM do token.",
            _ => $"A Settings API respondeu HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "sem descrição"})."
        };
        return new HttpRequestException(
            detail.Length > 0 ? $"{prefix} Detalhe: {detail}" : prefix,
            inner: null,
            response.StatusCode);
    }

    private static string ExtractErrorMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            var root = document.RootElement;
            var message = ReadString(root, "message");
            if (message.Length == 0
                && root.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                message = ReadString(error, "message");
            }

            return Truncate(message);
        }
        catch (JsonException)
        {
            return Truncate(body.Replace('\r', ' ').Replace('\n', ' ').Trim());
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
