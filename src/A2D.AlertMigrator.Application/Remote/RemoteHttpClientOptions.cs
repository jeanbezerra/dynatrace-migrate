namespace A2D.AlertMigrator.Application.Remote;

public sealed record RemoteHttpClientOptions(
    int ConnectTimeoutSeconds,
    int RequestTimeoutSeconds,
    int RetryCount,
    int RetryDelayMilliseconds,
    int PooledConnectionLifetimeMinutes,
    int MaxConnectionsPerServer,
    RemoteProxyMode ProxyMode,
    Uri? CustomProxyAddress,
    bool UseDefaultProxyCredentials,
    CertificateValidationMode TlsValidationMode,
    bool CheckCertificateRevocation,
    string? CustomCertificateAuthorityPath,
    string? PinnedCertificateSha256,
    IReadOnlyDictionary<string, string> CustomHeaders)
{
    private static readonly HashSet<string> ReservedHeaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "Proxy-Authorization",
        "Cookie",
        "Set-Cookie",
        "Host",
        "Content-Length",
        "Content-Type",
        "Connection",
        "Transfer-Encoding"
    };

    public void EnsureValid()
    {
        if (ConnectTimeoutSeconds is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(ConnectTimeoutSeconds), "O timeout de conexão deve estar entre 1 e 120 segundos.");
        }

        if (RequestTimeoutSeconds is < 2 or > 600)
        {
            throw new ArgumentOutOfRangeException(nameof(RequestTimeoutSeconds), "O timeout total deve estar entre 2 e 600 segundos.");
        }

        if (RequestTimeoutSeconds <= ConnectTimeoutSeconds)
        {
            throw new ArgumentException("O timeout total deve ser maior que o timeout de conexão.");
        }

        if (RetryCount is < 0 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryCount), "A quantidade de tentativas adicionais deve estar entre 0 e 10.");
        }

        if (RetryDelayMilliseconds is < 100 or > 30_000)
        {
            throw new ArgumentOutOfRangeException(nameof(RetryDelayMilliseconds), "O intervalo inicial deve estar entre 100 e 30.000 ms.");
        }

        if (PooledConnectionLifetimeMinutes is < 1 or > 120)
        {
            throw new ArgumentOutOfRangeException(nameof(PooledConnectionLifetimeMinutes), "A renovação de DNS deve estar entre 1 e 120 minutos.");
        }

        if (MaxConnectionsPerServer is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxConnectionsPerServer), "O máximo de conexões por servidor deve estar entre 1 e 256.");
        }

        if (!Enum.IsDefined(ProxyMode) || !Enum.IsDefined(TlsValidationMode))
        {
            throw new ArgumentException("A configuração do HTTP Client contém uma opção inválida.");
        }

        if (ProxyMode == RemoteProxyMode.Custom
            && (CustomProxyAddress is null
                || !CustomProxyAddress.IsAbsoluteUri
                || (CustomProxyAddress.Scheme != Uri.UriSchemeHttp
                    && CustomProxyAddress.Scheme != Uri.UriSchemeHttps)))
        {
            throw new ArgumentException("Informe uma URL HTTP ou HTTPS absoluta para o proxy personalizado.", nameof(CustomProxyAddress));
        }

        if (TlsValidationMode == CertificateValidationMode.CustomCertificateAuthority
            && string.IsNullOrWhiteSpace(CustomCertificateAuthorityPath))
        {
            throw new ArgumentException("Informe o arquivo da autoridade certificadora corporativa.");
        }

        if (TlsValidationMode == CertificateValidationMode.Sha256Pinning
            && NormalizeSha256(PinnedCertificateSha256).Length != 64)
        {
            throw new ArgumentException("O pin do certificado deve possuir 64 caracteres hexadecimais SHA-256.");
        }

        foreach (var header in CustomHeaders)
        {
            if (string.IsNullOrWhiteSpace(header.Key)
                || !header.Key.All(IsHeaderNameCharacter)
                || header.Value.Contains('\r')
                || header.Value.Contains('\n'))
            {
                throw new ArgumentException("Existe um cabeçalho HTTP personalizado inválido.");
            }

            if (ReservedHeaders.Contains(header.Key))
            {
                throw new ArgumentException($"O cabeçalho '{header.Key}' é reservado e deve ser configurado pelo conector seguro.");
            }
        }
    }

    public static string NormalizeSha256(string? value) =>
        new((value ?? string.Empty).Where(Uri.IsHexDigit).Select(char.ToUpperInvariant).ToArray());

    private static bool IsHeaderNameCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character)
        || character is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
}
