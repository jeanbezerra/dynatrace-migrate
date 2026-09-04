using A2D.AlertMigrator.Application.Remote;
using System.IO;
using System.Text.Json.Serialization;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record RemoteHttpSettings(
    int ConnectTimeoutSeconds,
    int RequestTimeoutSeconds,
    int RetryCount,
    int RetryDelayMilliseconds,
    int PooledConnectionLifetimeMinutes,
    int MaxConnectionsPerServer,
    RemoteProxyMode ProxyMode,
    string CustomProxyAddress,
    bool UseDefaultProxyCredentials,
    CertificateValidationMode TlsValidationMode,
    bool CheckCertificateRevocation,
    string CustomCertificateAuthorityPath,
    string PinnedCertificateSha256,
    Dictionary<string, string> CustomHeaders,
    [property: JsonPropertyName("dynatraceBaseAddress")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LegacyDynatraceBaseAddress = null,
    [property: JsonPropertyName("appDynamicsBaseAddress")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? LegacyAppDynamicsBaseAddress = null)
{
    public static RemoteHttpSettings CreateDefault() => new(
        ConnectTimeoutSeconds: 10,
        RequestTimeoutSeconds: 60,
        RetryCount: 2,
        RetryDelayMilliseconds: 500,
        PooledConnectionLifetimeMinutes: 5,
        MaxConnectionsPerServer: 16,
        ProxyMode: RemoteProxyMode.System,
        CustomProxyAddress: string.Empty,
        UseDefaultProxyCredentials: false,
        TlsValidationMode: CertificateValidationMode.SystemTrust,
        CheckCertificateRevocation: true,
        CustomCertificateAuthorityPath: string.Empty,
        PinnedCertificateSha256: string.Empty,
        CustomHeaders: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public RemoteHttpSettings Normalize()
    {
        var options = ToOptions();
        options.EnsureValid();
        return this with
        {
            CustomProxyAddress = options.CustomProxyAddress?.AbsoluteUri ?? string.Empty,
            CustomCertificateAuthorityPath = string.IsNullOrWhiteSpace(CustomCertificateAuthorityPath)
                ? string.Empty
                : Path.GetFullPath(CustomCertificateAuthorityPath.Trim()),
            PinnedCertificateSha256 = RemoteHttpClientOptions.NormalizeSha256(PinnedCertificateSha256),
            CustomHeaders = new Dictionary<string, string>(options.CustomHeaders, StringComparer.OrdinalIgnoreCase),
            LegacyDynatraceBaseAddress = null,
            LegacyAppDynamicsBaseAddress = null
        };
    }

    public RemoteHttpClientOptions ToOptions() => new(
        ConnectTimeoutSeconds,
        RequestTimeoutSeconds,
        RetryCount,
        RetryDelayMilliseconds,
        PooledConnectionLifetimeMinutes,
        MaxConnectionsPerServer,
        ProxyMode,
        ParseOptionalUri(CustomProxyAddress, "URL do proxy"),
        UseDefaultProxyCredentials,
        TlsValidationMode,
        CheckCertificateRevocation,
        string.IsNullOrWhiteSpace(CustomCertificateAuthorityPath) ? null : CustomCertificateAuthorityPath.Trim(),
        string.IsNullOrWhiteSpace(PinnedCertificateSha256) ? null : PinnedCertificateSha256.Trim(),
        new Dictionary<string, string>(CustomHeaders ?? [], StringComparer.OrdinalIgnoreCase));

    private static Uri? ParseOptionalUri(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            throw new ArgumentException($"{label} é inválida.");
        }

        return uri;
    }
}
