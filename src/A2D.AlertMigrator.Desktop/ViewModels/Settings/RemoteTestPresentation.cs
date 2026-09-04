using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Desktop.ViewModels.Settings;

public enum RemoteTestVisualState
{
    Idle,
    Running,
    Success,
    Warning,
    Error
}

public sealed record RemoteTestPresentation(
    RemoteTestVisualState State,
    string Title,
    string Message,
    string StatusCode,
    string Duration,
    string Protocol,
    string RedirectLocation)
{
    public bool HasStatusCode => !string.IsNullOrWhiteSpace(StatusCode);

    public bool HasDuration => !string.IsNullOrWhiteSpace(Duration);

    public bool HasProtocol => !string.IsNullOrWhiteSpace(Protocol);

    public bool HasRedirect => !string.IsNullOrWhiteSpace(RedirectLocation);

    public static RemoteTestPresentation Idle(string platform) => new(
        RemoteTestVisualState.Idle,
        "Ainda não testado",
        $"Execute uma chamada real para validar o acesso ao {platform}.",
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    public static RemoteTestPresentation Running(string platform) => new(
        RemoteTestVisualState.Running,
        "Teste em andamento",
        $"Validando DNS, proxy, TLS, autenticação e resposta do {platform}...",
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    public static RemoteTestPresentation Error(string message) => new(
        RemoteTestVisualState.Error,
        "Não foi possível concluir o teste",
        message,
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty);

    public static RemoteTestPresentation FromResult(RemoteConnectionTestResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var state = result.Outcome switch
        {
            RemoteConnectionTestOutcome.Success => RemoteTestVisualState.Success,
            RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus or
                RemoteConnectionTestOutcome.Redirect => RemoteTestVisualState.Warning,
            _ => RemoteTestVisualState.Error
        };
        var title = result.Outcome switch
        {
            RemoteConnectionTestOutcome.Success => "Conexão validada",
            RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus => "API acessível com outro status",
            RemoteConnectionTestOutcome.Redirect => "Redirecionamento detectado",
            RemoteConnectionTestOutcome.AuthenticationRejected => "Credencial não aceita",
            RemoteConnectionTestOutcome.AccessDenied => "Permissão insuficiente",
            RemoteConnectionTestOutcome.Cancelled => "Teste cancelado",
            RemoteConnectionTestOutcome.TransportError => "Falha de conexão",
            _ => "A API respondeu com erro"
        };

        return new RemoteTestPresentation(
            state,
            title,
            result.Message,
            result.StatusCode is null ? string.Empty : $"HTTP {(int)result.StatusCode} — {result.StatusCode}",
            $"{result.ElapsedMilliseconds} ms",
            result.HttpVersion,
            result.RedirectLocation);
    }
}
