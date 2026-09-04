using System.Net;

namespace A2D.AlertMigrator.Application.Remote;

public static class RemoteHttpResponseClassifier
{
    public static RemoteConnectionTestOutcome Classify(HttpStatusCode statusCode, int expectedStatusCode)
    {
        var numericStatus = (int)statusCode;
        return numericStatus switch
        {
            >= 300 and <= 399 => RemoteConnectionTestOutcome.Redirect,
            401 => RemoteConnectionTestOutcome.AuthenticationRejected,
            403 => RemoteConnectionTestOutcome.AccessDenied,
            >= 200 and <= 299 when numericStatus == expectedStatusCode => RemoteConnectionTestOutcome.Success,
            >= 200 and <= 299 => RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus,
            _ => RemoteConnectionTestOutcome.HttpError
        };
    }
}
