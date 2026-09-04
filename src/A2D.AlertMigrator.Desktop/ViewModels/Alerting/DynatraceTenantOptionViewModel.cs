using System.Security.Cryptography;
using System.Text;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Remote;
using A2D.AlertMigrator.Desktop.Configuration;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceTenantOptionViewModel
{
    private readonly ManagedConnectionSettings _settings;

    public DynatraceTenantOptionViewModel(ManagedConnectionSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        EnvironmentLabel = settings.Environment.ToString().ToUpperInvariant();
        Alias = string.IsNullOrWhiteSpace(settings.Alias) ? EnvironmentLabel : settings.Alias;
        DisplayName = $"{EnvironmentLabel} · {Alias}";
        BaseAddress = settings.BaseAddress;
        IsReady = settings.Enabled
            && Uri.TryCreate(settings.BaseAddress, UriKind.Absolute, out var address)
            && address.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(settings.Key);
        ReadinessText = IsReady
            ? settings.BaseAddress
            : "Complete URL, autenticação e chave em Configurações > Dynatrace";
        IsDavisEventReady = IsReady
            && settings.AuthenticationMode == RemoteAuthenticationMode.BearerToken
            && Uri.TryCreate(settings.BaseAddress, UriKind.Absolute, out var grailAddress)
            && (grailAddress.Host.EndsWith(".live.dynatrace.com", StringComparison.OrdinalIgnoreCase)
                || grailAddress.Host.EndsWith(".apps.dynatrace.com", StringComparison.OrdinalIgnoreCase));
        DavisEventReadinessText = IsDavisEventReady
            ? settings.BaseAddress
            : "Use uma URL SaaS Dynatrace e Platform Token ou OAuth no modo Bearer.";
        TenantKey = CreateTenantKey(settings.Environment, settings.BaseAddress);
    }

    public string EnvironmentLabel { get; }

    public string Alias { get; }

    public string DisplayName { get; }

    public string BaseAddress { get; }

    public string ReadinessText { get; }

    public string TenantKey { get; }

    public bool IsReady { get; }

    public bool IsDavisEventReady { get; }

    public string DavisEventReadinessText { get; }

    public DynatraceAlertingProfileSource CreateSource(bool requestAdminAccess)
    {
        var normalized = _settings.Normalize(RemotePlatform.Dynatrace);
        if (!Uri.TryCreate(normalized.BaseAddress, UriKind.Absolute, out var address))
        {
            throw new ArgumentException("O ambiente selecionado não possui uma URL-base válida.");
        }

        return new DynatraceAlertingProfileSource(
            TenantKey,
            EnvironmentLabel,
            Alias,
            address,
            normalized.AuthenticationMode,
            normalized.Key,
            requestAdminAccess);
    }

    public DynatraceAnomalyDetectorSource CreateAnomalyDetectorSource(bool requestAdminAccess)
    {
        var normalized = _settings.Normalize(RemotePlatform.Dynatrace);
        if (!Uri.TryCreate(normalized.BaseAddress, UriKind.Absolute, out var address))
        {
            throw new ArgumentException("O ambiente selecionado não possui uma URL-base válida.");
        }

        return new DynatraceAnomalyDetectorSource(
            TenantKey,
            EnvironmentLabel,
            Alias,
            address,
            normalized.AuthenticationMode,
            normalized.Key,
            requestAdminAccess);
    }

    public DynatraceDavisEventSource CreateDavisEventSource(int lookbackHours, int resultLimit)
    {
        var normalized = _settings.Normalize(RemotePlatform.Dynatrace);
        if (!Uri.TryCreate(normalized.BaseAddress, UriKind.Absolute, out var address))
        {
            throw new ArgumentException("O ambiente selecionado não possui uma URL-base válida.");
        }

        return new DynatraceDavisEventSource(
            TenantKey,
            EnvironmentLabel,
            Alias,
            address,
            normalized.AuthenticationMode,
            normalized.Key,
            lookbackHours,
            resultLimit);
    }

    public DynatraceProblemSource CreateProblemSource(int lookbackHours, int resultLimit)
    {
        var normalized = _settings.Normalize(RemotePlatform.Dynatrace);
        if (!Uri.TryCreate(normalized.BaseAddress, UriKind.Absolute, out var address))
        {
            throw new ArgumentException("O ambiente selecionado não possui uma URL-base válida.");
        }

        return new DynatraceProblemSource(
            TenantKey,
            EnvironmentLabel,
            Alias,
            address,
            normalized.AuthenticationMode,
            normalized.Key,
            lookbackHours,
            resultLimit);
    }

    private static string CreateTenantKey(ManagedEnvironment environment, string baseAddress)
    {
        var identity = $"dynatrace|{environment}|{(baseAddress ?? string.Empty).Trim().TrimEnd('/').ToLowerInvariant()}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
    }
}
