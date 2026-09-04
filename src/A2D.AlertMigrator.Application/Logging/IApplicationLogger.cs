namespace A2D.AlertMigrator.Application.Logging;

public interface IApplicationLogger : IDisposable
{
    string? CurrentLogPath { get; }

    string? LastError { get; }

    void Configure(FileLogOptions options);

    void Write(
        ApplicationLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null);
}
