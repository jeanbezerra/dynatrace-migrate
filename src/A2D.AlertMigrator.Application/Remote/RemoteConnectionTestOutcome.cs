namespace A2D.AlertMigrator.Application.Remote;

public enum RemoteConnectionTestOutcome
{
    Success,
    SuccessWithUnexpectedStatus,
    Redirect,
    AuthenticationRejected,
    AccessDenied,
    HttpError,
    TransportError,
    Cancelled
}
