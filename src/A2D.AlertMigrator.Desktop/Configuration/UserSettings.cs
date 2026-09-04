using A2D.AlertMigrator.Application.Importing;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record UserSettings(
    bool RecursiveByDefault = false,
    Utf8BomPolicy Utf8BomPolicy = Utf8BomPolicy.Accept,
    int MaxFileSizeMb = 10,
    int MaxFiles = 1_000,
    int MaxRulesPerApplication = 5_000,
    int MaxRulesTotal = 50_000,
    int MaxJsonDepth = 64,
    int MaxDqlCharacters = 65_536,
    ApplicationLogSettings? Logging = null,
    LocalDatabaseSettings? Database = null,
    RemoteHttpSettings? RemoteHttp = null,
    RemoteConnectionTestSettings? ConnectionTests = null,
    IntegrationSettings? Integrations = null)
{
    public static UserSettings Default { get; } = new(
        Logging: ApplicationLogSettings.CreateDefault(),
        Database: LocalDatabaseSettings.CreateDefault(),
        RemoteHttp: RemoteHttpSettings.CreateDefault(),
        ConnectionTests: RemoteConnectionTestSettings.CreateDefault(),
        Integrations: IntegrationSettings.CreateDefault());

    public ApplicationLogSettings EffectiveLogging =>
        Logging ?? ApplicationLogSettings.CreateDefault();

    public LocalDatabaseSettings EffectiveDatabase =>
        Database ?? LocalDatabaseSettings.CreateDefault();

    public RemoteHttpSettings EffectiveRemoteHttp =>
        RemoteHttp ?? RemoteHttpSettings.CreateDefault();

    public RemoteConnectionTestSettings EffectiveConnectionTests =>
        ConnectionTests ?? RemoteConnectionTestSettings.CreateDefault();

    public IntegrationSettings EffectiveIntegrations =>
        Integrations ?? IntegrationSettings.CreateDefault();

    public UserSettings Normalize()
    {
        var remoteHttp = EffectiveRemoteHttp;
        var connectionTests = EffectiveConnectionTests;
        var dynatrace = connectionTests.Dynatrace;
        var appDynamics = connectionTests.AppDynamics;
        if (string.IsNullOrWhiteSpace(dynatrace.TestAddress)
            && !string.IsNullOrWhiteSpace(remoteHttp.LegacyDynatraceBaseAddress))
        {
            dynatrace = dynatrace with { TestAddress = remoteHttp.LegacyDynatraceBaseAddress };
        }

        if (string.IsNullOrWhiteSpace(appDynamics.TestAddress)
            && !string.IsNullOrWhiteSpace(remoteHttp.LegacyAppDynamicsBaseAddress))
        {
            appDynamics = appDynamics with { TestAddress = remoteHttp.LegacyAppDynamicsBaseAddress };
        }

        return this with
        {
            Logging = EffectiveLogging.Normalize(),
            Database = EffectiveDatabase.Normalize(),
            RemoteHttp = remoteHttp.Normalize(),
            ConnectionTests = new RemoteConnectionTestSettings(dynatrace, appDynamics).Normalize(),
            Integrations = EffectiveIntegrations.Normalize()
        };
    }

    public ImportLimits ToImportLimits() => new(
        MaxFileBytes: checked((long)MaxFileSizeMb * 1024 * 1024),
        MaxFiles,
        MaxRulesPerApplication,
        MaxRulesTotal,
        MaxJsonDepth,
        MaxDqlCharacters);
}
