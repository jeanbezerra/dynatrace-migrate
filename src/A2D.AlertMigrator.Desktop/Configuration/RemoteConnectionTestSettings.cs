using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record RemoteConnectionTestSettings(
    RemoteEndpointTestSettings Dynatrace,
    RemoteEndpointTestSettings AppDynamics)
{
    public static RemoteConnectionTestSettings CreateDefault() => new(
        new RemoteEndpointTestSettings(
            string.Empty,
            RemoteTestMethod.Get,
            RemoteAuthenticationMode.BearerToken,
            string.Empty,
            200),
        new RemoteEndpointTestSettings(
            string.Empty,
            RemoteTestMethod.Get,
            RemoteAuthenticationMode.BearerToken,
            string.Empty,
            200));

    public RemoteConnectionTestSettings Normalize()
    {
        var dynatrace = Dynatrace.Normalize("Dynatrace");
        var appDynamics = AppDynamics.Normalize("AppDynamics");
        if (dynatrace.AuthenticationMode == RemoteAuthenticationMode.Basic)
        {
            throw new ArgumentException("Autenticação Basic não é suportada no teste Dynatrace.");
        }

        if (appDynamics.AuthenticationMode == RemoteAuthenticationMode.DynatraceApiToken)
        {
            throw new ArgumentException("Dynatrace API token não é válido no teste AppDynamics.");
        }

        return new RemoteConnectionTestSettings(dynatrace, appDynamics);
    }
}
