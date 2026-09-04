using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using A2D.AlertMigrator.Application.Logging;

namespace A2D.AlertMigrator.Infrastructure.Logging;

public sealed class JsonLinesFileLogger : IApplicationLogger
{
    private const string ActiveFileName = "a2d-alert-migrator.jsonl";
    private const string ArchiveSearchPattern = "a2d-alert-migrator-*.jsonl";
    private static readonly UTF8Encoding Utf8WithoutBom = new(false, true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object _gate = new();
    private readonly string _sessionId = Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture);
    private readonly string _applicationVersion =
        Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
    private FileLogOptions _options;
    private FileStream? _stream;
    private StreamWriter? _writer;
    private string? _currentLogPath;
    private string? _lastError;
    private bool _disposed;

    public JsonLinesFileLogger(FileLogOptions options)
    {
        options.EnsureValid();
        _options = options;

        lock (_gate)
        {
            TryOpenWriter();
        }
    }

    public string? CurrentLogPath
    {
        get
        {
            lock (_gate)
            {
                return _currentLogPath;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public void Configure(FileLogOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            CloseWriter();
            _options = options;
            _currentLogPath = null;
            _lastError = null;
            TryOpenWriter();
        }
    }

    public void Write(
        ApplicationLogLevel level,
        string eventName,
        string message,
        Exception? exception = null,
        IReadOnlyDictionary<string, object?>? properties = null)
    {
        lock (_gate)
        {
            if (_disposed
                || level < _options.MinimumLevel
                || _options.MinimumLevel == ApplicationLogLevel.None)
            {
                return;
            }

            try
            {
                EnsureWriter();
                if (_writer is null || _stream is null)
                {
                    return;
                }

                var entry = new JsonLogEntry(
                    SchemaVersion: 1,
                    Timestamp: DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture),
                    Level: level.ToString(),
                    Event: string.IsNullOrWhiteSpace(eventName) ? "application_event" : eventName,
                    Message: message ?? string.Empty,
                    Application: "A2D.AlertMigrator",
                    Version: _applicationVersion,
                    SessionId: _sessionId,
                    ProcessId: Environment.ProcessId,
                    ThreadId: Environment.CurrentManagedThreadId,
                    Properties: properties,
                    Exception: exception is null
                        ? null
                        : new JsonLogException(
                            exception.GetType().FullName ?? exception.GetType().Name,
                            exception.Message,
                            exception.StackTrace));

                var line = JsonSerializer.Serialize(entry, SerializerOptions);
                var lineSize = Utf8WithoutBom.GetByteCount(line) + 1;
                if (_options.RotationEnabled
                    && _stream.Length > 0
                    && _stream.Length + lineSize > _options.RotationSizeBytes)
                {
                    Rotate();
                    EnsureWriter();
                }

                _writer?.WriteLine(line);
                _lastError = null;
            }
            catch (Exception writeException) when (writeException is IOException
                or UnauthorizedAccessException
                or ArgumentException
                or NotSupportedException)
            {
                SetFailure(writeException);
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            CloseWriter();
            _disposed = true;
        }

        GC.SuppressFinalize(this);
    }

    private void EnsureWriter()
    {
        if (_writer is null)
        {
            TryOpenWriter();
        }
    }

    private void TryOpenWriter()
    {
        try
        {
            Directory.CreateDirectory(_options.DirectoryPath);
            var activePath = Path.Combine(_options.DirectoryPath, ActiveFileName);

            if (_options.RotationEnabled
                && File.Exists(activePath)
                && new FileInfo(activePath).Length >= _options.RotationSizeBytes)
            {
                ArchiveActiveFile(activePath);
                EnforceRetention();
            }

            _stream = new FileStream(
                activePath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4_096,
                FileOptions.SequentialScan);
            _writer = new StreamWriter(_stream, Utf8WithoutBom, bufferSize: 4_096, leaveOpen: true)
            {
                AutoFlush = true,
                NewLine = "\n"
            };
            _currentLogPath = activePath;
            _lastError = null;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            SetFailure(exception);
        }
    }

    private void Rotate()
    {
        var activePath = _currentLogPath ?? Path.Combine(_options.DirectoryPath, ActiveFileName);
        CloseWriter();
        ArchiveActiveFile(activePath);
        EnforceRetention();
    }

    private static void ArchiveActiveFile(string activePath)
    {
        if (!File.Exists(activePath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(activePath)
            ?? throw new InvalidOperationException("Pasta do arquivo de log inválida.");
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        var archivePath = Path.Combine(directory, $"a2d-alert-migrator-{timestamp}.jsonl");
        var suffix = 1;
        while (File.Exists(archivePath))
        {
            archivePath = Path.Combine(directory, $"a2d-alert-migrator-{timestamp}-{suffix:000}.jsonl");
            suffix++;
        }

        File.Move(activePath, archivePath);
    }

    private void EnforceRetention()
    {
        var archives = Directory
            .EnumerateFiles(_options.DirectoryPath, ArchiveSearchPattern, SearchOption.TopDirectoryOnly)
            .Select(static path => new FileInfo(path))
            .OrderByDescending(static file => file.LastWriteTimeUtc)
            .ThenByDescending(static file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Skip(_options.RetainedFileCount)
            .ToArray();

        foreach (var archive in archives)
        {
            try
            {
                archive.Delete();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Debug.WriteLine($"Não foi possível remover o log antigo '{archive.FullName}': {exception.Message}");
            }
        }
    }

    private void SetFailure(Exception exception)
    {
        CloseWriter();
        _currentLogPath = null;
        _lastError = exception.Message;
        Debug.WriteLine($"Falha ao gravar log: {exception}");
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
            _stream?.Dispose();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _lastError ??= exception.Message;
            Debug.WriteLine($"Falha ao fechar o arquivo de log: {exception}");
        }
        finally
        {
            _writer = null;
            _stream = null;
        }
    }

    private sealed record JsonLogEntry(
        int SchemaVersion,
        string Timestamp,
        string Level,
        [property: JsonPropertyName("event")] string Event,
        string Message,
        string Application,
        string Version,
        string SessionId,
        int ProcessId,
        int ThreadId,
        IReadOnlyDictionary<string, object?>? Properties,
        JsonLogException? Exception);

    private sealed record JsonLogException(
        string Type,
        string Message,
        string? StackTrace);
}
