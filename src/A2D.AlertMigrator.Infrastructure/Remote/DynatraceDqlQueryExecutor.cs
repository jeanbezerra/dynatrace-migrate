using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Infrastructure.Remote;

internal sealed class DynatraceDqlQueryExecutor
{
    private const int MaximumPollAttempts = 200;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(300);
    private readonly IRemoteHttpClientFactory _httpClientFactory;

    public DynatraceDqlQueryExecutor(IRemoteHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    public async Task<IReadOnlyList<string>> ExecuteAsync(
        Uri environmentBaseAddress,
        string bearerToken,
        string query,
        CancellationToken cancellationToken = default)
    {
        using var client = _httpClientFactory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, BuildPlatformAddress(environmentBaseAddress, "query:execute"))
        {
            Content = new StringContent(JsonSerializer.Serialize(new { query }), Encoding.UTF8, "application/json")
        };
        ApplyAuthentication(request, bearerToken);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        using var initial = await ReadResponseAsync(response, cancellationToken).ConfigureAwait(false);
        if (TryReadRecords(initial.RootElement, out var records))
        {
            return CopyRecords(records);
        }

        var requestToken = ReadString(initial.RootElement, "requestToken");
        ThrowIfTerminalFailure(initial.RootElement);
        if (requestToken.Length == 0)
        {
            throw new InvalidDataException("A DQL Query API não retornou o resultado nem o token de acompanhamento.");
        }

        var pollAddress = BuildPollAddress(environmentBaseAddress, requestToken);
        for (var attempt = 1; attempt <= MaximumPollAttempts; attempt++)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            using var pollRequest = new HttpRequestMessage(HttpMethod.Get, pollAddress);
            ApplyAuthentication(pollRequest, bearerToken);
            using var pollResponse = await client.SendAsync(pollRequest, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            using var poll = await ReadResponseAsync(pollResponse, cancellationToken).ConfigureAwait(false);
            if (TryReadRecords(poll.RootElement, out records))
            {
                return CopyRecords(records);
            }

            ThrowIfTerminalFailure(poll.RootElement);
        }

        throw new TimeoutException("A consulta DQL não terminou dentro de 60 segundos.");
    }

    private static IReadOnlyList<string> CopyRecords(JsonElement records) =>
        records.EnumerateArray().Select(static item => item.GetRawText()).ToArray();

    private static async Task<JsonDocument> ReadResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if ((int)response.StatusCode is >= 300 and <= 399)
        {
            throw new HttpRequestException(
                $"A DQL Query API respondeu HTTP {(int)response.StatusCode} e solicitou redirecionamento. Use diretamente a URL final do ambiente para proteger a credencial.",
                inner: null,
                response.StatusCode);
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw CreateApiException(response.StatusCode, response.ReasonPhrase, body);
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("A DQL Query API retornou um JSON inválido.", exception);
        }
    }

    private static HttpRequestException CreateApiException(HttpStatusCode statusCode, string? reasonPhrase, string body)
    {
        var prefix = statusCode switch
        {
            HttpStatusCode.Unauthorized => "A autenticação foi rejeitada. Use um Platform Token ou OAuth válido no modo Bearer.",
            HttpStatusCode.Forbidden => "A leitura do Grail foi negada. Conceda a permissão da tabela consultada e acesso ao bucket correspondente.",
            _ => $"A DQL Query API respondeu HTTP {(int)statusCode} ({reasonPhrase ?? "sem descrição"})."
        };
        var detail = ExtractErrorMessage(body);
        return new HttpRequestException(detail.Length == 0 ? prefix : $"{prefix} Detalhe: {detail}", null, statusCode);
    }

    private static void ThrowIfTerminalFailure(JsonElement root)
    {
        var state = ReadString(root, "state");
        if (!state.Equals("FAILED", StringComparison.OrdinalIgnoreCase)
            && !state.Equals("CANCELLED", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var detail = ExtractErrorMessage(root.GetRawText());
        throw new InvalidOperationException(detail.Length == 0
            ? $"A consulta DQL terminou com estado {state}."
            : $"A consulta DQL terminou com estado {state}. Detalhe: {detail}");
    }

    private static bool TryReadRecords(JsonElement root, out JsonElement records)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("result", out var result)
            && result.ValueKind == JsonValueKind.Object
            && result.TryGetProperty("records", out records)
            && records.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        records = default;
        return false;
    }

    private static Uri BuildPollAddress(Uri baseAddress, string requestToken)
    {
        var endpoint = BuildPlatformAddress(baseAddress, "query:poll");
        return new UriBuilder(endpoint) { Query = $"request-token={Uri.EscapeDataString(requestToken)}" }.Uri;
    }

    private static Uri BuildPlatformAddress(Uri baseAddress, string operation)
    {
        var host = baseAddress.Host;
        const string liveSuffix = ".live.dynatrace.com";
        const string appsSuffix = ".apps.dynatrace.com";
        if (host.EndsWith(liveSuffix, StringComparison.OrdinalIgnoreCase))
        {
            host = host[..^liveSuffix.Length] + appsSuffix;
        }
        else if (!host.EndsWith(appsSuffix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Não foi possível derivar a URL da Plataforma. Use a URL SaaS no formato https://<environment-id>.live.dynatrace.com.");
        }

        return new UriBuilder(Uri.UriSchemeHttps, host)
        {
            Path = $"platform/storage/query/v1/{operation}",
            Query = string.Empty,
            Fragment = string.Empty
        }.Uri;
    }

    private static void ApplyAuthentication(HttpRequestMessage request, string bearerToken) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

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
            if (message.Length == 0 && root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
            {
                message = ReadString(error, "message");
                if (message.Length == 0)
                {
                    message = ReadString(error, "details");
                }
            }

            return Truncate(message);
        }
        catch (JsonException)
        {
            return Truncate(body.Replace('\r', ' ').Replace('\n', ' ').Trim());
        }
    }

    private static string ReadString(JsonElement item, string propertyName)
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

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
