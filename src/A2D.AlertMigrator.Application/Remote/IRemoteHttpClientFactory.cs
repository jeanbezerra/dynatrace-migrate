namespace A2D.AlertMigrator.Application.Remote;

public interface IRemoteHttpClientFactory : IDisposable
{
    string? LastError { get; }

    void Configure(RemoteHttpClientOptions options);

    HttpClient CreateClient();

    Task<RemoteConnectionTestResult> TestConnectionAsync(
        RemoteConnectionTestRequest request,
        CancellationToken cancellationToken = default);
}
