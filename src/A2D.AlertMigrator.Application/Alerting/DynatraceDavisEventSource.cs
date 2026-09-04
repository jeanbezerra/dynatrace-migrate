using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Application.Alerting;

public sealed record DynatraceDavisEventSource(
    string TenantKey,
    string Environment,
    string TenantAlias,
    Uri BaseAddress,
    RemoteAuthenticationMode AuthenticationMode,
    string AccessKey,
    int LookbackHours,
    int ResultLimit)
{
    public void EnsureValid()
    {
        if (string.IsNullOrWhiteSpace(TenantKey)
            || string.IsNullOrWhiteSpace(Environment)
            || string.IsNullOrWhiteSpace(TenantAlias))
        {
            throw new ArgumentException("A identificação do ambiente Dynatrace está incompleta.");
        }

        if (BaseAddress is null
            || !string.Equals(BaseAddress.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A URL-base do Dynatrace deve usar HTTPS.");
        }

        if (AuthenticationMode != RemoteAuthenticationMode.BearerToken)
        {
            throw new ArgumentException(
                "Eventos do Grail exigem Platform Token ou OAuth no modo Bearer. " +
                "API Token legado não é aceito pela DQL Query API.");
        }

        if (string.IsNullOrWhiteSpace(AccessKey))
        {
            throw new ArgumentException("O ambiente selecionado não possui uma chave de acesso.");
        }

        if (LookbackHours is < 1 or > 720)
        {
            throw new ArgumentOutOfRangeException(nameof(LookbackHours), "O período deve ficar entre 1 hora e 30 dias.");
        }

        if (ResultLimit is < 1 or > 5_000)
        {
            throw new ArgumentOutOfRangeException(nameof(ResultLimit), "O limite deve ficar entre 1 e 5.000 eventos.");
        }
    }
}
