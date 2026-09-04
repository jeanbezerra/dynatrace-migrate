using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record PlatformIntegrationSettings(
    IReadOnlyList<ManagedConnectionSettings> Environments)
{
    private static readonly ManagedEnvironment[] RequiredEnvironments =
    [
        ManagedEnvironment.Dev,
        ManagedEnvironment.Hml,
        ManagedEnvironment.Prd
    ];

    public static PlatformIntegrationSettings CreateDefault(RemotePlatform platform) => new(
        RequiredEnvironments
            .Select(environment => ManagedConnectionSettings.CreateDefault(platform, environment))
            .ToArray());

    public PlatformIntegrationSettings Normalize(RemotePlatform platform)
    {
        var source = Environments ?? [];
        var duplicates = source
            .GroupBy(static connection => connection.Environment)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicates is not null)
        {
            throw new ArgumentException($"O ambiente {duplicates.Key} foi informado mais de uma vez.");
        }

        var normalized = RequiredEnvironments
            .Select(environment => source.FirstOrDefault(connection => connection.Environment == environment)
                ?? ManagedConnectionSettings.CreateDefault(platform, environment))
            .Select(connection => connection.Normalize(platform))
            .ToArray();
        return new PlatformIntegrationSettings(normalized);
    }
}
