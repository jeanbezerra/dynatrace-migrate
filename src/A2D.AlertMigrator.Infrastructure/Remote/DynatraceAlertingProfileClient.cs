using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Infrastructure.Remote;

public sealed class DynatraceAlertingProfileClient : IDynatraceAlertingProfileClient
{
    private const string SchemaId = "builtin:alerting.profile";
    private const int MaximumPages = 10_000;
    private readonly IRemoteHttpClientFactory _httpClientFactory;

    public DynatraceAlertingProfileClient(IRemoteHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<IReadOnlyList<DynatraceAlertingProfileSnapshot>> GetAllAsync(
        DynatraceAlertingProfileSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureValid();

        using var client = _httpClientFactory.CreateClient();
        var results = new Dictionary<string, DynatraceAlertingProfileSnapshot>(StringComparer.Ordinal);
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

            if (IsRedirect(response.StatusCode))
            {
                throw new HttpRequestException(
                    $"A Settings API respondeu HTTP {(int)response.StatusCode} e solicitou redirecionamento. " +
                    "Use diretamente a URL final do ambiente para que a credencial não seja encaminhada a outro host.",
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
                var profile = ParseProfile(item);
                if (!results.TryAdd(profile.RemoteObjectId, profile))
                {
                    throw new InvalidDataException(
                        $"A Settings API retornou o objeto '{profile.RemoteObjectId}' mais de uma vez.");
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
                throw new InvalidDataException("A Settings API repetiu a chave de paginação e o sincronismo foi interrompido.");
            }

            address = BuildNextPageAddress(source.BaseAddress, nextPageKey);
        }

        if (address is not null)
        {
            throw new InvalidDataException($"A Settings API excedeu o limite de {MaximumPages:N0} páginas.");
        }

        return results.Values
            .OrderBy(static profile => profile.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static DynatraceAlertingProfileSnapshot ParseProfile(JsonElement item)
    {
        var objectId = ReadRequiredString(item, "objectId");
        var schemaId = ReadString(item, "schemaId");
        var schemaVersion = ReadString(item, "schemaVersion");
        var scope = ReadString(item, "scope");
        var value = item.TryGetProperty("value", out var valueElement)
            && valueElement.ValueKind == JsonValueKind.Object
                ? valueElement
                : default;
        var name = value.ValueKind == JsonValueKind.Object
            ? ReadString(value, "name")
            : string.Empty;
        var managementZone = value.ValueKind == JsonValueKind.Object
            ? ReadManagementZone(value)
            : string.Empty;
        var severityRuleCount = CountArray(value, "severityRules");
        var eventFilterCount = CountArray(value, "eventFilters");
        var rawJson = item.GetRawText();
        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));

        return new DynatraceAlertingProfileSnapshot(
            objectId,
            string.IsNullOrWhiteSpace(schemaId) ? SchemaId : schemaId,
            schemaVersion,
            scope,
            string.IsNullOrWhiteSpace(name) ? "Perfil sem nome" : name,
            managementZone,
            severityRuleCount,
            eventFilterCount,
            ReadTimestamp(item, "created"),
            ReadTimestamp(item, "modified"),
            contentHash,
            rawJson);
    }

    private static int CountArray(JsonElement parent, string propertyName) =>
        parent.ValueKind == JsonValueKind.Object
        && parent.TryGetProperty(propertyName, out var value)
        && value.ValueKind == JsonValueKind.Array
            ? value.GetArrayLength()
            : 0;

    private static string ReadManagementZone(JsonElement value)
    {
        if (!value.TryGetProperty("managementZone", out var zone))
        {
            return string.Empty;
        }

        if (zone.ValueKind == JsonValueKind.String)
        {
            return zone.GetString() ?? string.Empty;
        }

        if (zone.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        var name = ReadString(zone, "name");
        if (name.Length > 0)
        {
            return name;
        }

        var id = ReadString(zone, "id");
        return id.Length > 0 ? id : zone.GetRawText();
    }

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

        if (value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var timestamp))
        {
            return timestamp;
        }

        return null;
    }

    private static string ReadRequiredString(JsonElement item, string propertyName)
    {
        var value = ReadString(item, propertyName);
        return value.Length > 0
            ? value
            : throw new InvalidDataException($"Um perfil retornado não contém '{propertyName}'.");
    }

    private static string ReadString(JsonElement item, string propertyName) =>
        item.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static Uri BuildFirstPageAddress(DynatraceAlertingProfileSource source)
    {
        var query = $"schemaIds={Uri.EscapeDataString(SchemaId)}&pageSize=500";
        if (source.RequestAdminAccess)
        {
            query += "&adminAccess=true";
        }

        return BuildSettingsAddress(source.BaseAddress, query);
    }

    private static Uri BuildNextPageAddress(Uri baseAddress, string nextPageKey) =>
        BuildSettingsAddress(baseAddress, $"nextPageKey={Uri.EscapeDataString(nextPageKey)}");

    private static Uri BuildSettingsAddress(Uri baseAddress, string query)
    {
        var root = baseAddress.AbsoluteUri.TrimEnd('/') + "/";
        var endpoint = new Uri(new Uri(root), "api/v2/settings/objects");
        return new UriBuilder(endpoint) { Query = query }.Uri;
    }

    private static void ApplyAuthentication(
        HttpRequestMessage request,
        DynatraceAlertingProfileSource source)
    {
        request.Headers.Authorization = source.AuthenticationMode switch
        {
            RemoteAuthenticationMode.BearerToken => new AuthenticationHeaderValue("Bearer", source.AccessKey),
            RemoteAuthenticationMode.DynatraceApiToken => new AuthenticationHeaderValue("Api-Token", source.AccessKey),
            _ => throw new ArgumentOutOfRangeException(nameof(source), "Tipo de autenticação incompatível com o Dynatrace.")
        };
    }

    private static bool IsRedirect(HttpStatusCode statusCode) => (int)statusCode is >= 300 and <= 399;

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
                "A API negou o inventário administrativo. Conceda 'settings:objects:admin' ou desative o acesso administrativo para sincronizar apenas os objetos permitidos.",
            HttpStatusCode.Forbidden =>
                "A API negou a leitura. Conceda 'settings:objects:read' e revise as políticas IAM do token.",
            _ => $"A Settings API respondeu HTTP {(int)response.StatusCode} ({response.ReasonPhrase ?? "sem descrição"})."
        };
        var message = detail.Length > 0 ? $"{prefix} Detalhe: {detail}" : prefix;
        return new HttpRequestException(message, inner: null, response.StatusCode);
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
            var value = ReadString(root, "message");
            if (value.Length == 0 && root.TryGetProperty("error", out var error))
            {
                value = error.ValueKind == JsonValueKind.String
                    ? error.GetString() ?? string.Empty
                    : ReadString(error, "message");
            }

            return Truncate(value);
        }
        catch (JsonException)
        {
            return Truncate(body.Replace('\r', ' ').Replace('\n', ' ').Trim());
        }
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
