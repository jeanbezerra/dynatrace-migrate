using System.IO;
using System.Globalization;
using System.Text;
using System.Text.Json;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Application.Remote;
using A2D.AlertMigrator.Desktop.Configuration;
using A2D.AlertMigrator.Desktop.Services;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;
using A2D.AlertMigrator.Infrastructure.Logging;
using A2D.AlertMigrator.Infrastructure.Persistence;
using A2D.AlertMigrator.Infrastructure.Remote;

var testRoot = Path.Combine(Path.GetTempPath(), "A2DAlertMigrator.Desktop.Tests", Guid.NewGuid().ToString("N"));
var settingsPath = Path.Combine(testRoot, "settings.json");

try
{
    var expected = new UserSettings(
        RecursiveByDefault: true,
        Utf8BomPolicy: Utf8BomPolicy.Reject,
        MaxFileSizeMb: 20,
        MaxFiles: 800,
        MaxRulesPerApplication: 4_000,
        MaxRulesTotal: 40_000,
        MaxJsonDepth: 80,
        MaxDqlCharacters: 80_000,
        Logging: new ApplicationLogSettings(
            Path.Combine(testRoot, "configured-logs"),
            ApplicationLogLevel.Debug,
            RotationEnabled: true,
            RotationSizeMb: 5,
            RetainedFileCount: 7),
        Database: new LocalDatabaseSettings(
            Path.Combine(testRoot, "configured-data", "a2d.db"),
            BusyTimeoutSeconds: 15,
            UseWriteAheadLogging: true),
        Integrations: CreateIntegrationSettings());

    var writer = new JsonUserSettingsService(settingsPath);
    writer.Save(expected);

    var bytes = File.ReadAllBytes(settingsPath);
    Assert(bytes.Length >= 3, "settings file should not be empty");
    Assert(!(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "settings file must not contain UTF-8 BOM");
    _ = new UTF8Encoding(false, true).GetString(bytes);

    var reader = new JsonUserSettingsService(settingsPath);
    Assert(reader.Current with { RemoteHttp = null, ConnectionTests = null, Integrations = null }
        == expected.Normalize() with { RemoteHttp = null, ConnectionTests = null, Integrations = null },
        "saved local settings should be loaded without changes");
    Assert(reader.Current.EffectiveRemoteHttp == RemoteHttpSettings.CreateDefault()
        || reader.Current.EffectiveRemoteHttp.CustomHeaders.Count == 0,
        "new installations should receive safe HTTP Client defaults");
    Assert(reader.Current.EffectiveConnectionTests.Dynatrace.AuthenticationMode == RemoteAuthenticationMode.BearerToken,
        "new installations should use Bearer authentication for Dynatrace Platform Tokens");
    var persistedJson = new UTF8Encoding(false, true).GetString(bytes);
    Assert(persistedJson.Contains("\"integrations\"", StringComparison.Ordinal),
        "settings JSON should contain a dedicated integrations section");
    Assert(persistedJson.Contains("test-platform-token-not-a-secret", StringComparison.Ordinal),
        "managed environment keys should be persisted as plain text by design");
    Assert(reader.Current.EffectiveIntegrations.EffectiveDynatrace.Environments.Count == 3,
        "Dynatrace should always contain DEV, HML and PRD");
    Assert(reader.Current.EffectiveIntegrations.EffectiveAppDynamics.Environments.Count == 3,
        "AppDynamics should always contain DEV, HML and PRD");
    var dynatraceDev = reader.Current.EffectiveIntegrations.EffectiveDynatrace.Environments
        .Single(connection => connection.Environment == ManagedEnvironment.Dev);
    Assert(dynatraceDev.Alias == "Observabilidade DEV", "Dynatrace alias should be preserved");
    Assert(dynatraceDev.BaseAddress == "https://abc12345.live.dynatrace.com",
        "Dynatrace base address should be derived from the environment id");
    Assert(dynatraceDev.Key == "test-platform-token-not-a-secret",
        "Dynatrace plain-text key should be loaded without changes");

    Console.WriteLine("PASS persists settings as strict UTF-8 without BOM");

    TestLegacySettingsMigration(testRoot);
    TestInvalidLogSettingsFallback(testRoot);
    TestStructuredRealtimeLog(testRoot);
    TestRotationAndRetention(testRoot);
    TestRotationDisabled(testRoot);
    TestSqliteCreateVerifyAndExport(testRoot);
    TestRemoteHttpClientConfiguration();
    TestAlertingProfileDetailsFormatting();
    TestAnomalyDetectorDetailsFormatting();
    TestDavisEventDetailsFormatting();
    TestProblemDetailsFormatting();
    return 0;
}

finally
{
    var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "A2DAlertMigrator.Desktop.Tests"));
    var target = Path.GetFullPath(testRoot);
    if (target.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        && Directory.Exists(target))
    {
        Directory.Delete(target, recursive: true);
    }
}

static void TestAlertingProfileDetailsFormatting()
{
    var now = DateTimeOffset.UtcNow;
    var profile = new StoredDynatraceAlertingProfile(
        "tenant-key",
        "HML",
        "Homologação",
        new Uri("https://abc.live.dynatrace.com"),
        "profile-1",
        "builtin:alerting.profile",
        "1.0.0",
        "environment",
        "Operação crítica",
        "MZ Operação",
        SeverityRuleCount: 2,
        EventFilterCount: 1,
        RemoteCreatedAt: now.AddDays(-2),
        RemoteModifiedAt: now.AddDays(-1),
        FirstSeenAt: now.AddHours(-2),
        LastSeenAt: now,
        IsPresent: true,
        RawJson: "{\"objectId\":\"profile-1\",\"value\":{\"name\":\"Operação crítica\"}}");

    var details = new DynatraceAlertingProfileDetailsViewModel(profile);

    Assert(details.FormattedJson.Contains(Environment.NewLine + "  \"objectId\"", StringComparison.Ordinal),
        "profile JSON should be indented for the details dialog");
    Assert(details.FormattedJson.Contains("Operação crítica", StringComparison.Ordinal),
        "formatted JSON should keep readable UTF-8 characters");
    Assert(details.ManagementZone == "MZ Operação" && details.SeverityRuleCount == "2",
        "details dialog should prioritize key profile fields");
    Console.WriteLine("PASS formats alerting profile details for the modal");
}

static void TestAnomalyDetectorDetailsFormatting()
{
    var now = DateTimeOffset.UtcNow;
    var detector = new StoredDynatraceAnomalyDetector(
        "tenant-key",
        "PRD",
        "Produção",
        new Uri("https://abc.live.dynatrace.com"),
        "detector-1",
        "builtin:davis.anomaly-detectors",
        "1.0.2",
        "environment",
        "Latência de checkout",
        "Detector do serviço",
        "A2D",
        Enabled: true,
        "dt.statistics.ui.anomaly_detection.StaticThresholdAnomalyDetectionAnalyzer",
        "Estático",
        "timeseries latency = avg(dt.service.request.response_time)",
        UsesTimeseries: true,
        "PERFORMANCE_EVENT",
        "Checkout lento",
        "checkout",
        "actor-id",
        AnalyzerInputCount: 2,
        EventPropertyCount: 3,
        RemoteCreatedAt: now.AddDays(-2),
        RemoteModifiedAt: now.AddDays(-1),
        FirstSeenAt: now.AddHours(-2),
        LastSeenAt: now,
        IsPresent: true,
        RawJson: "{\"objectId\":\"detector-1\",\"value\":{\"title\":\"Latência de checkout\"}}");

    var details = new DynatraceAnomalyDetectorDetailsViewModel(detector);

    Assert(details.QueryStatusText == "DQL timeseries" && details.Model == "Estático",
        "anomaly modal should prioritize query compliance and model");
    Assert(details.FormattedJson.Contains("Latência de checkout", StringComparison.Ordinal),
        "anomaly modal JSON should remain readable UTF-8");
    Console.WriteLine("PASS formats anomaly detector details for the modal");
}

static void TestDavisEventDetailsFormatting()
{
    var now = DateTimeOffset.UtcNow;
    var item = new StoredDynatraceDavisEvent(
        "tenant-key",
        "PRD",
        "Produção",
        new Uri("https://abc.live.dynatrace.com"),
        "event-1",
        "Checkout Response Time High",
        "Latência acima do limite",
        "SLOWDOWN",
        "ACTIVE",
        "CREATED",
        Severity: 2,
        Provider: "metric_events",
        EventType: "CUSTOM_ALERT",
        SourceEntityId: "SERVICE-123",
        SourceEntityType: "SERVICE",
        SettingsObjectId: "detector-1",
        SettingsSchemaId: "builtin:davis.anomaly-detectors",
        AlertGroup: "checkout",
        Query: "timeseries latency = avg(metric)",
        IsFrequent: false,
        IsMergingAllowed: true,
        IsUnderMaintenance: false,
        Timestamp: now,
        Start: now.AddMinutes(-15),
        End: null,
        FirstSeenAt: now,
        LastSeenAt: now,
        RawJson: "{\"event.id\":\"event-1\",\"event.name\":\"Checkout Response Time High\"}");

    var details = new DynatraceDavisEventDetailsViewModel(item);

    Assert(details.StatusText == "Ativo" && details.SeverityText == "2 · Alta",
        "Davis event modal should prioritize state and severity");
    Assert(details.EntityId == "SERVICE-123" && details.SettingsObjectId == "detector-1",
        "Davis event modal should connect entity and detector context");
    Assert(details.FormattedJson.Contains(Environment.NewLine + "  \"event.id\"", StringComparison.Ordinal),
        "Davis event JSON should be indented");
    Console.WriteLine("PASS formats Davis event details for the modal");
}

static void TestProblemDetailsFormatting()
{
    var now = DateTimeOffset.UtcNow;
    var problem = new StoredDynatraceProblem(
        "tenant-key",
        "PRD",
        "Produção",
        new Uri("https://abc.live.dynatrace.com"),
        "problem-event-1",
        "P-12345",
        "Checkout indisponível",
        "Falha correlacionada no checkout",
        "AVAILABILITY",
        "ACTIVE",
        Severity: 1,
        AffectedUsersCount: 240,
        AffectedEntityCount: 2,
        CorrelatedEventCount: 2,
        RootCauseEntityId: "HOST-456",
        RootCauseEntityName: "checkout-host",
        RootCauseEntityType: "HOST",
        AffectedEntityIds: ["SERVICE-123", "HOST-456"],
        AffectedEntityTypes: ["SERVICE", "HOST"],
        AffectedServiceIds: ["SERVICE-123"],
        CorrelatedEventIds: ["event-1", "event-2"],
        IsRootCause: true,
        IsUnderMaintenance: false,
        Timestamp: now,
        Start: now.AddMinutes(-20),
        End: null,
        FirstSeenAt: now,
        LastSeenAt: now,
        RawJson: "{\"event.id\":\"problem-event-1\",\"display_id\":\"P-12345\"}");

    var details = new DynatraceProblemDetailsViewModel(problem);

    Assert(details.DisplayId == "P-12345" && details.StatusText == "Ativo",
        "problem modal should prioritize display ID and state");
    Assert(details.RootCauseId == "HOST-456" && details.AffectedUsersText == "240",
        "problem modal should prioritize root cause and impact");
    Assert(details.CorrelatedEventsText.Contains("event-2", StringComparison.Ordinal)
        && details.FormattedJson.Contains(Environment.NewLine + "  \"event.id\"", StringComparison.Ordinal),
        "problem modal should expose correlated events and formatted JSON");
    Console.WriteLine("PASS formats problem details for the modal");
}

static void TestLegacySettingsMigration(string testRoot)
{
    var legacyPath = Path.Combine(testRoot, "legacy-settings.json");
    const string legacyJson = """
        {
          "recursiveByDefault": false,
          "utf8BomPolicy": "accept",
          "maxFileSizeMb": 10,
          "maxFiles": 1000,
          "maxRulesPerApplication": 5000,
          "maxRulesTotal": 50000,
          "maxJsonDepth": 64,
          "maxDqlCharacters": 65536
        }
        """;
    File.WriteAllText(legacyPath, legacyJson, new UTF8Encoding(false, true));

    var service = new JsonUserSettingsService(legacyPath);
    Assert(service.Current.Logging is not null, "legacy settings should receive default logging configuration");
    Assert(service.Current.Database is not null, "legacy settings should receive default SQLite configuration");
    Assert(service.Current.RemoteHttp is not null, "legacy settings should receive default HTTP Client configuration");
    Assert(service.Current.ConnectionTests is not null, "legacy settings should receive default connection tests");
    Assert(service.Current.Integrations is not null, "legacy settings should receive DEV, HML and PRD integrations");
    Assert(Path.IsPathFullyQualified(service.Current.EffectiveLogging.DirectoryPath),
        "default log directory should be absolute");

    Console.WriteLine("PASS migrates settings created before logging configuration");
}

static IntegrationSettings CreateIntegrationSettings()
{
    var defaults = IntegrationSettings.CreateDefault();
    var dynatrace = defaults.EffectiveDynatrace.Environments
        .Select(connection => connection.Environment == ManagedEnvironment.Dev
            ? connection with
            {
                Alias = "Observabilidade DEV",
                TenantIdentifier = "abc12345",
                Key = "test-platform-token-not-a-secret",
                Details = "Conexão usada apenas pelo smoke test."
            }
            : connection)
        .ToArray();
    var appDynamics = defaults.EffectiveAppDynamics.Environments
        .Select(connection => connection.Environment == ManagedEnvironment.Hml
            ? connection with
            {
                Alias = "Controller HML",
                BaseAddress = "https://controller.example.com",
                Key = "test-appdynamics-key-not-a-secret"
            }
            : connection)
        .ToArray();
    return new IntegrationSettings(
        new PlatformIntegrationSettings(dynatrace),
        new PlatformIntegrationSettings(appDynamics));
}

static void TestStructuredRealtimeLog(string testRoot)
{
    var logDirectory = Path.Combine(testRoot, "realtime-logs");
    var options = new FileLogOptions(
        logDirectory,
        ApplicationLogLevel.Debug,
        RotationEnabled: true,
        RotationSizeBytes: 1024 * 1024,
        RetainedFileCount: 3);

    using var logger = new JsonLinesFileLogger(options);
    logger.Write(ApplicationLogLevel.Trace, "filtered_event", "must not be written");
    logger.Write(
        ApplicationLogLevel.Information,
        "test_event",
        "ação concluída",
        properties: new Dictionary<string, object?>
        {
            ["count"] = 3,
            ["success"] = true
        });

    var logPath = logger.CurrentLogPath ?? throw new InvalidOperationException("logger should expose the active path");
    byte[] bytes;
    using (var liveStream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
    using (var memory = new MemoryStream())
    {
        liveStream.CopyTo(memory);
        bytes = memory.ToArray();
    }

    Assert(bytes.Length >= 3, "log file should not be empty");
    Assert(!(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF), "log file must not contain UTF-8 BOM");
    var content = new UTF8Encoding(false, true).GetString(bytes);
    var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    Assert(lines.Length == 1, "minimum level should filter Trace and keep Information");
    using var document = JsonDocument.Parse(lines[0]);
    var root = document.RootElement;
    var timestamp = root.GetProperty("timestamp").GetString();
    Assert(timestamp is not null
        && timestamp.EndsWith('Z')
        && DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out _),
        "timestamp should be ISO 8601 UTC");
    Assert(root.GetProperty("schemaVersion").GetInt32() == 1, "log schema version should be stable");
    Assert(root.GetProperty("event").GetString() == "test_event", "event name should be structured");
    Assert(root.GetProperty("properties").GetProperty("count").GetInt32() == 3, "numeric properties should remain numeric");
    Assert(root.GetProperty("sessionId").GetString()?.Length == 32, "log should contain a session id");

    Console.WriteLine("PASS writes realtime UTF-8 JSONL with ISO 8601 UTC and level filtering");
}

static void TestInvalidLogSettingsFallback(string testRoot)
{
    var invalidPath = Path.Combine(testRoot, "invalid-settings.json");
    const string invalidJson = """
        {
          "recursiveByDefault": false,
          "utf8BomPolicy": "accept",
          "maxFileSizeMb": 10,
          "maxFiles": 1000,
          "maxRulesPerApplication": 5000,
          "maxRulesTotal": 50000,
          "maxJsonDepth": 64,
          "maxDqlCharacters": 65536,
          "logging": {
            "directoryPath": "invalid\u0000path",
            "minimumLevel": "information",
            "rotationEnabled": true,
            "rotationSizeMb": 25,
            "retainedFileCount": 10
          }
        }
        """;
    File.WriteAllText(invalidPath, invalidJson, new UTF8Encoding(false, true));

    var service = new JsonUserSettingsService(invalidPath);
    Assert(
        service.Current == UserSettings.Default,
        $"invalid manual log settings should fall back to safe defaults; current log path: '{service.Current.EffectiveLogging.DirectoryPath}'");

    Console.WriteLine("PASS falls back safely when manual log settings are invalid");
}

static void TestRotationAndRetention(string testRoot)
{
    var logDirectory = Path.Combine(testRoot, "rotating-logs");
    var options = new FileLogOptions(
        logDirectory,
        ApplicationLogLevel.Trace,
        RotationEnabled: true,
        RotationSizeBytes: 1024 * 1024,
        RetainedFileCount: 2);
    var payload = new string('x', 400_000);

    using (var logger = new JsonLinesFileLogger(options))
    {
        for (var index = 0; index < 12; index++)
        {
            logger.Write(ApplicationLogLevel.Information, "rotation_test", payload);
        }
    }

    var archives = Directory.GetFiles(logDirectory, "a2d-alert-migrator-*.jsonl");
    Assert(archives.Length == 2, "rotation should enforce the configured archive retention");
    Assert(File.Exists(Path.Combine(logDirectory, "a2d-alert-migrator.jsonl")), "rotation should keep an active log file");

    Console.WriteLine("PASS rotates by size and enforces archive retention");
}

static void TestRotationDisabled(string testRoot)
{
    var logDirectory = Path.Combine(testRoot, "non-rotating-logs");
    var options = new FileLogOptions(
        logDirectory,
        ApplicationLogLevel.Trace,
        RotationEnabled: false,
        RotationSizeBytes: 1024 * 1024,
        RetainedFileCount: 2);
    var payload = new string('y', 400_000);

    using (var logger = new JsonLinesFileLogger(options))
    {
        for (var index = 0; index < 4; index++)
        {
            logger.Write(ApplicationLogLevel.Information, "no_rotation_test", payload);
        }
    }

    Assert(Directory.GetFiles(logDirectory, "a2d-alert-migrator-*.jsonl").Length == 0,
        "rotation disabled should not create archives");
    Assert(new FileInfo(Path.Combine(logDirectory, "a2d-alert-migrator.jsonl")).Length > 1024 * 1024,
        "rotation disabled should allow the active file to exceed the size threshold");

    Console.WriteLine("PASS keeps a single file when rotation is disabled");
}

static void TestSqliteCreateVerifyAndExport(string testRoot)
{
    var databasePath = Path.Combine(testRoot, "sqlite", "a2d-alert-migrator.db");
    var exportPath = Path.Combine(testRoot, "exports", "a2d-alert-migrator-copy.db");
    var service = new SqliteLocalDatabaseService(new(
        databasePath,
        BusyTimeoutSeconds: 10,
        UseWriteAheadLogging: true));

    var info = service.GetInfo();
    Assert(info.Exists, "SQLite database should be created at the configured path");
    Assert(info.SchemaVersion == 5, "SQLite schema should include problem history");
    Assert(info.JournalMode == "WAL", "SQLite should enable WAL when configured");
    Assert(service.VerifyIntegrity(), "SQLite quick integrity check should pass");

    service.RecordImport(new(
        Id: "test-operation",
        StartedUtc: DateTimeOffset.UtcNow.AddSeconds(-1),
        CompletedUtc: DateTimeOffset.UtcNow,
        Status: "completed",
        SourceType: "json_folder",
        Applications: 300,
        Rules: 600,
        Errors: 0,
        Warnings: 2));
    Assert(service.GetInfo().HistoryRecordCount == 1, "SQLite should persist import history");

    service.Export(exportPath);
    Assert(File.Exists(exportPath), "SQLite export should create a portable database file");
    var header = new byte[16];
    using (var stream = File.OpenRead(exportPath))
    {
        _ = stream.Read(header, 0, header.Length);
    }

    Assert(Encoding.ASCII.GetString(header) == "SQLite format 3\0", "export should be a valid SQLite file");
    var exportedService = new SqliteLocalDatabaseService(new(
        exportPath,
        BusyTimeoutSeconds: 10,
        UseWriteAheadLogging: false));
    Assert(exportedService.GetInfo().HistoryRecordCount == 1, "SQLite export should include import history");

    service.Configure(new(
        databasePath,
        BusyTimeoutSeconds: 10,
        UseWriteAheadLogging: false));
    Assert(service.GetInfo().JournalMode == "DELETE", "SQLite should disable WAL when configured");

    Console.WriteLine("PASS creates, verifies, configures and exports SQLite consistently");
}

static void TestRemoteHttpClientConfiguration()
{
    var settings = RemoteHttpSettings.CreateDefault() with
    {
        RetryCount = 0,
        CustomHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["X-Correlation-Origin"] = "a2d-tests"
        }
    };
    var normalized = settings.Normalize();
    using var factory = new ResilientRemoteHttpClientFactory(normalized.ToOptions());
    using var client = factory.CreateClient();

    Assert(client.BaseAddress is null, "shared HTTP Client settings should not contain platform URLs");
    Assert(client.Timeout == Timeout.InfiniteTimeSpan, "the resilience pipeline should own total request timeout");
    Assert(client.DefaultRequestHeaders.TryGetValues("X-Correlation-Origin", out var values)
        && values.Single() == "a2d-tests", "HTTP Client should apply non-sensitive custom headers");

    var unsafeSettings = settings with
    {
        CustomHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Api-Token must-not-be-stored-here"
        }
    };
    AssertThrows<ArgumentException>(() => unsafeSettings.Normalize(),
        "plain-text Authorization headers should be rejected");

    var testRequest = new RemoteConnectionTestRequest(
        RemotePlatform.Dynatrace,
        new Uri("https://example.invalid/api/v2/settings/objects"),
        RemoteTestMethod.Get,
        RemoteAuthenticationMode.DynatraceApiToken,
        Username: null,
        Secret: "session-only-token",
        ExpectedStatusCode: 200);
    testRequest.EnsureValid();
    AssertThrows<ArgumentException>(() => (testRequest with { Secret = null }).EnsureValid(),
        "authenticated tests should require an in-memory secret");
    AssertThrows<ArgumentException>(() => (testRequest with
    {
        TestAddress = new Uri("http://example.invalid/")
    }).EnsureValid(), "connection tests should require HTTPS");

    Console.WriteLine("PASS configures resilient HTTP Client and validates secure endpoint tests");
}

static void AssertThrows<TException>(Action action, string message)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException(message);
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
