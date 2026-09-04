using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceAnomalyDetectorSource(
    string TenantKey,
    string Environment,
    string TenantAlias,
    Uri BaseAddress,
    RemoteAuthenticationMode AuthenticationMode,
    string AccessKey,
    bool RequestAdminAccess)
{
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(TenantKey)
            || string.IsNullOrWhiteSpace(Environment)
            || string.IsNullOrWhiteSpace(TenantAlias))
        {
            throw new ArgumentException("A identificação do ambiente Dynatrace está incompleta.");
        }

        if (BaseAddress is null || !string.Equals(BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A URL-base do Dynatrace deve usar HTTPS.");
        }

        if (AuthenticationMode is not RemoteAuthenticationMode.BearerToken
            and not RemoteAuthenticationMode.DynatraceApiToken)
        {
            throw new ArgumentException("Selecione Platform Token, OAuth ou API Token para sincronizar os detectores.");
        }

        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            throw new ArgumentException("O ambiente selecionado não possui uma chave de acesso.");
        }
    }
}
