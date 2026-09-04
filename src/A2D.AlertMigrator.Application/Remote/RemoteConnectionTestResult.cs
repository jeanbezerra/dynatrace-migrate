using System.Net;

namespace A2D.AlertMigrator.Application.Remote;

public sealed record RemoteConnectionTestResult(
    RemotePlatform Platform,
    RemoteConnectionTestOutcome Outcome,
    bool TransportSucceeded,
    HttpStatusCode? StatusCode,
    long ElapsedMilliseconds,
    string Message,
    bool ExpectedStatusReceived = false,
    string HttpVersion = "",
    string RedirectLocation = "");
