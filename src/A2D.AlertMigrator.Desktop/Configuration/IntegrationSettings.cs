using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record IntegrationSettings(
    PlatformIntegrationSettings? Dynatrace = null,
    PlatformIntegrationSettings? AppDynamics = null)
{
    public static IntegrationSettings CreateDefault() => new(
        PlatformIntegrationSettings.CreateDefault(RemotePlatform.Dynatrace),
        PlatformIntegrationSettings.CreateDefault(RemotePlatform.AppDynamics));

    public PlatformIntegrationSettings EffectiveDynatrace =>
        Dynatrace ?? PlatformIntegrationSettings.CreateDefault(RemotePlatform.Dynatrace);

    public PlatformIntegrationSettings EffectiveAppDynamics =>
        AppDynamics ?? PlatformIntegrationSettings.CreateDefault(RemotePlatform.AppDynamics);

    public IntegrationSettings Normalize() => new(
        EffectiveDynatrace.Normalize(RemotePlatform.Dynatrace),
        EffectiveAppDynamics.Normalize(RemotePlatform.AppDynamics));
}
