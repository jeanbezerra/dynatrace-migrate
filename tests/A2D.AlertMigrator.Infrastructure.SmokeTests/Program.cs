using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Application.Persistence;
using A2D.AlertMigrator.Application.Remote;
using A2D.AlertMigrator.Domain.Importing;
using A2D.AlertMigrator.Infrastructure.Importing.Json;
using A2D.AlertMigrator.Infrastructure.Persistence;
using A2D.AlertMigrator.Infrastructure.Remote;

var workspace = args.Length == 1
    ? Path.GetFullPath(args[0])
    : FindWorkspace(AppContext.BaseDirectory);
var samplePath = Path.Combine(workspace, "samples", "json", "checkout.json");
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

var tests = new (string Name, Func<Task> Run)[]
{
    ("imports valid canonical application", ImportValidApplication),
    ("isolates malformed file", IsolateMalformedFile),
    ("rejects duplicate JSON properties", RejectDuplicateProperties),
    ("marks duplicate application ids", MarkDuplicateApplications),
    ("orders files deterministically", OrderFilesDeterministically),
    ("honors recursive option", HonorRecursiveOption),
    ("rejects schedule and missing-data conflict", RejectScheduleMissingDataConflict),
    ("rejects incompatible grouped rules", RejectIncompatibleGroup),
    ("imports the generated 300-application sample", ImportGeneratedApplications),
    ("accepts UTF-8 with BOM by default", AcceptUtf8Bom),
    ("rejects UTF-8 BOM when configured", RejectUtf8Bom),
    ("requires UTF-8 BOM when configured", RequireUtf8Bom),
    ("rejects invalid UTF-8", RejectInvalidUtf8),
    ("classifies redirects and authentication outcomes", ClassifyRemoteHttpResponses),
    ("paginates Dynatrace alerting profiles safely", PaginateDynatraceAlertingProfiles),
    ("synchronizes Dynatrace alerting profiles transactionally", SynchronizeDynatraceAlertingProfiles),
    ("parses Dynatrace anomaly detectors", ParseDynatraceAnomalyDetectors),
    ("synchronizes Dynatrace anomaly detectors transactionally", SynchronizeDynatraceAnomalyDetectors),
    ("queries and polls Dynatrace Davis events", QueryDynatraceDavisEvents),
    ("synchronizes Dynatrace Davis events transactionally", SynchronizeDynatraceDavisEvents),
    ("queries deduplicated Dynatrace problems", QueryDynatraceProblems),
    ("synchronizes Dynatrace problems transactionally", SynchronizeDynatraceProblems)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        await test.Run();
        Console.WriteLine($"PASS {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL {test.Name}: {exception.Message}");
    }
}

Console.WriteLine($"{tests.Length - failures.Count}/{tests.Length} tests passed");
return failures.Count == 0 ? 0 : 1;

async Task ImportValidApplication()
{
    using var folder = new TestFolder();
    File.Copy(samplePath, Path.Combine(folder.Path, "checkout.json"));

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(batch.IsValid, $"batch should be valid: {Describe(batch)}");
    Assert(batch.Applications.Count == 1, "one application expected");
    Assert(batch.RuleCount == 2, "two rules expected");
    Assert(batch.Applications[0].Source.Sha256.Length == 64, "SHA-256 should be recorded");

    var firstRule = batch.Applications[0].Document!.Rules[0];
    Assert(firstRule.Targets.Count == 2, "multi-service target should be preserved");
    Assert(firstRule.Event.Type == "ERROR_EVENT", "event type should inherit from defaults");
    Assert(firstRule.Event.AlertGroup == "payments-checkout", "alert group should inherit from defaults");
    Assert(firstRule.Schedule.Mode == "ACTIVE_WINDOW", "schedule should inherit from defaults");
}

async Task IsolateMalformedFile()
{
    using var folder = new TestFolder();
    File.Copy(samplePath, Path.Combine(folder.Path, "valid.json"));
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "broken.json"), "{ not-json }");

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(!batch.IsValid, "batch should expose the malformed file");
    Assert(batch.Applications.Count == 2, "both files should be represented");
    Assert(batch.Applications.Count(static app => app.IsValid) == 1, "valid file must remain usable");
    Assert(HasCode(batch, "JSON_SYNTAX_ERROR"), "syntax diagnostic expected");
}

async Task RejectDuplicateProperties()
{
    using var folder = new TestFolder();
    var json = await File.ReadAllTextAsync(samplePath);
    json = json.Replace("\"schemaVersion\": \"1.0\",", "\"schemaVersion\": \"1.0\",\n  \"schemaVersion\": \"1.0\",", StringComparison.Ordinal);
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "duplicate.json"), json);

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(HasCode(batch, "JSON_DUPLICATE_PROPERTY"), "duplicate-property diagnostic expected");
    Assert(batch.Applications.Single().Document is null, "ambiguous document must not be materialized");
}

async Task MarkDuplicateApplications()
{
    using var folder = new TestFolder();
    File.Copy(samplePath, Path.Combine(folder.Path, "first.json"));
    File.Copy(samplePath, Path.Combine(folder.Path, "second.json"));

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(batch.Applications.All(static app => !app.IsValid), "both duplicate applications must be blocked");
    Assert(batch.Applications.All(static app => app.Diagnostics.Any(d => d.Code == "APPLICATION_ID_DUPLICATE")), "each duplicate should be diagnosed");
}

async Task OrderFilesDeterministically()
{
    using var folder = new TestFolder();
    await WriteApplicationCopy("z.json", "z-app");
    await WriteApplicationCopy("a.json", "a-app");

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(batch.Applications.Select(static app => app.Source.RelativePath).SequenceEqual(["a.json", "z.json"]), "files should use ordinal relative-path order");

    async Task WriteApplicationCopy(string fileName, string applicationId)
    {
        var document = JsonNode.Parse(await File.ReadAllTextAsync(samplePath))!.AsObject();
        document["application"]!["id"] = applicationId;
        await File.WriteAllTextAsync(Path.Combine(folder.Path, fileName), document.ToJsonString(jsonOptions));
    }
}

async Task HonorRecursiveOption()
{
    using var folder = new TestFolder();
    var nested = Directory.CreateDirectory(Path.Combine(folder.Path, "nested"));
    File.Copy(samplePath, Path.Combine(nested.FullName, "checkout.json"));

    var adapter = new JsonFolderImportAdapter();
    var flat = await adapter.ReadAsync(new JsonFolderImportOptions(folder.Path, Recursive: false));
    var recursive = await adapter.ReadAsync(new JsonFolderImportOptions(folder.Path, Recursive: true));

    Assert(flat.Applications.Count == 0, "flat import should ignore nested files");
    Assert(recursive.Applications.Count == 1, "recursive import should include nested files");
    Assert(recursive.Applications[0].Source.RelativePath == "nested/checkout.json", "relative path should be normalized");
}

async Task RejectScheduleMissingDataConflict()
{
    using var folder = new TestFolder();
    var document = JsonNode.Parse(await File.ReadAllTextAsync(samplePath))!.AsObject();
    document["rules"]![0]!["detector"]!["alertOnMissingData"] = true;
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "conflict.json"), document.ToJsonString(jsonOptions));

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(HasCode(batch, "SCHEDULE_MISSING_DATA_CONFLICT"), "schedule conflict diagnostic expected");
    Assert(!batch.Applications.Single().IsValid, "application should be blocked");
}

async Task RejectIncompatibleGroup()
{
    using var folder = new TestFolder();
    var document = JsonNode.Parse(await File.ReadAllTextAsync(samplePath))!.AsObject();
    var firstRule = document["rules"]![0]!.DeepClone();
    firstRule["id"] = "APPD-HR-2042";
    firstRule["name"] = "Different threshold";
    firstRule["detector"]!["threshold"] = 99;
    document["rules"]!.AsArray().Add(firstRule);
    await File.WriteAllTextAsync(Path.Combine(folder.Path, "group.json"), document.ToJsonString(jsonOptions));

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(HasCode(batch, "GROUP_SEMANTIC_MISMATCH"), "group mismatch diagnostic expected");
    Assert(!batch.Applications.Single().IsValid, "incompatible group should block the application");
}

async Task ImportGeneratedApplications()
{
    var generatedFolder = Path.Combine(workspace, "samples", "json", "generated-300");
    Assert(Directory.Exists(generatedFolder), "generated sample folder should exist");

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(generatedFolder));

    Assert(batch.IsValid, $"generated batch should be valid: {Describe(batch)}");
    Assert(batch.Applications.Count == 300, "300 generated applications expected");
    Assert(batch.RuleCount == 600, "two rules per generated application expected");
}

async Task AcceptUtf8Bom()
{
    using var folder = new TestFolder();
    var content = await File.ReadAllBytesAsync(samplePath);
    var contentWithBom = new byte[content.Length + 3];
    contentWithBom[0] = 0xEF;
    contentWithBom[1] = 0xBB;
    contentWithBom[2] = 0xBF;
    content.CopyTo(contentWithBom, 3);
    await File.WriteAllBytesAsync(Path.Combine(folder.Path, "with-bom.json"), contentWithBom);

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(batch.IsValid, $"UTF-8 with BOM should be accepted: {Describe(batch)}");
}

async Task RejectUtf8Bom()
{
    using var folder = new TestFolder();
    var content = await File.ReadAllBytesAsync(samplePath);
    var contentWithBom = new byte[content.Length + 3];
    contentWithBom[0] = 0xEF;
    contentWithBom[1] = 0xBB;
    contentWithBom[2] = 0xBF;
    content.CopyTo(contentWithBom, 3);
    await File.WriteAllBytesAsync(Path.Combine(folder.Path, "with-bom.json"), contentWithBom);

    var options = new JsonFolderImportOptions(
        folder.Path,
        Encoding: new JsonEncodingOptions(Utf8BomPolicy.Reject));
    var batch = await new JsonFolderImportAdapter().ReadAsync(options);

    Assert(HasCode(batch, "JSON_UTF8_BOM_REJECTED"), "BOM rejection diagnostic expected");
}

async Task RejectInvalidUtf8()
{
    using var folder = new TestFolder();
    await File.WriteAllBytesAsync(Path.Combine(folder.Path, "invalid-utf8.json"), [0x7B, 0xC3, 0x28, 0x7D]);

    var batch = await new JsonFolderImportAdapter().ReadAsync(new JsonFolderImportOptions(folder.Path));

    Assert(HasCode(batch, "JSON_ENCODING_INVALID"), "invalid UTF-8 diagnostic expected");
}

async Task RequireUtf8Bom()
{
    using var folder = new TestFolder();
    File.Copy(samplePath, Path.Combine(folder.Path, "without-bom.json"));

    var options = new JsonFolderImportOptions(
        folder.Path,
        Encoding: new JsonEncodingOptions(Utf8BomPolicy.Require));
    var batch = await new JsonFolderImportAdapter().ReadAsync(options);

    Assert(HasCode(batch, "JSON_UTF8_BOM_REQUIRED"), "required BOM diagnostic expected");
}

Task ClassifyRemoteHttpResponses()
{
    Assert(
        RemoteHttpResponseClassifier.Classify(HttpStatusCode.TemporaryRedirect, 200)
            == RemoteConnectionTestOutcome.Redirect,
        "HTTP 307 should be classified as a redirect, not an authentication failure");
    Assert(
        RemoteHttpResponseClassifier.Classify(HttpStatusCode.NoContent, 200)
            == RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus,
        "an unexpected 2xx response should remain a successful API response");
    Assert(
        RemoteHttpResponseClassifier.Classify(HttpStatusCode.Unauthorized, 200)
            == RemoteConnectionTestOutcome.AuthenticationRejected,
        "HTTP 401 should identify an authentication failure");
    Assert(
        RemoteHttpResponseClassifier.Classify(HttpStatusCode.Unauthorized, 401)
            == RemoteConnectionTestOutcome.AuthenticationRejected,
        "an expected HTTP 401 should still identify an authentication failure");
    Assert(
        RemoteHttpResponseClassifier.Classify(HttpStatusCode.Forbidden, 200)
            == RemoteConnectionTestOutcome.AccessDenied,
        "HTTP 403 should identify an authorization failure");
    return Task.CompletedTask;
}

async Task PaginateDynatraceAlertingProfiles()
{
    var firstPage = """
        {
          "items": [
            {
              "objectId": "vu9U3hXa3q0AAAABACRidWlsdGluOmFsZXJ0aW5nLnByb2ZpbGU",
              "schemaId": "builtin:alerting.profile",
              "schemaVersion": "1.0.0",
              "scope": "environment",
              "value": {
                "name": "Operação crítica",
                "severityRules": [{"severityLevel":"AVAILABILITY"}],
                "eventFilters": []
              },
              "created": 1704067200000,
              "modified": 1704153600000
            }
          ],
          "nextPageKey": "next page +/="
        }
        """;
    var secondPage = """
        {
          "items": [
            {
              "objectId": "profile-2",
              "schemaId": "builtin:alerting.profile",
              "scope": "environment",
              "value": {
                "name": "Backoffice",
                "managementZone": "MZ Backoffice",
                "severityRules": [],
                "eventFilters": [{"customTitleFilter":{"enabled":true}}]
              }
            }
          ]
        }
        """;
    using var factory = new FakeRemoteHttpClientFactory(firstPage, secondPage);
    var client = new DynatraceAlertingProfileClient(factory);
    var source = new DynatraceAlertingProfileSource(
        "tenant-key",
        "HML",
        "Homologação",
        new Uri("https://abc.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "secret-platform-token",
        RequestAdminAccess: true);

    var profiles = await client.GetAllAsync(source);

    Assert(profiles.Count == 2, "two profiles should be returned across both pages");
    Assert(profiles.Single(profile => profile.RemoteObjectId == "profile-2").EventFilterCount == 1,
        "event filters should be counted");
    Assert(factory.Requests.Count == 2, "two HTTP requests expected");
    Assert(factory.Requests[0].Address.Query.Contains("schemaIds=builtin%3Aalerting.profile", StringComparison.Ordinal),
        "first request should filter the alerting-profile schema");
    Assert(factory.Requests[0].Address.Query.Contains("adminAccess=true", StringComparison.Ordinal),
        "first request should request administrative inventory");
    Assert(factory.Requests[1].Address.Query == "?nextPageKey=next%20page%20%2B%2F%3D",
        "continuation request should contain only the encoded nextPageKey");
    Assert(factory.Requests.All(request => request.AuthenticationScheme == "Bearer"),
        "every page should receive Bearer authentication");
    Assert(factory.Requests.All(request => request.AuthenticationParameter == "secret-platform-token"),
        "every page should receive the configured token");
}

Task SynchronizeDynatraceAlertingProfiles()
{
    using var folder = new TestFolder();
    var databasePath = Path.Combine(folder.Path, "history.db");
    var database = new SqliteLocalDatabaseService(new LocalDatabaseOptions(
        databasePath,
        BusyTimeoutSeconds: 10,
        UseWriteAheadLogging: true));
    var source = new DynatraceAlertingProfileSource(
        "tenant-key",
        "PRD",
        "Produção",
        new Uri("https://prod.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "never-persist-this-token",
        RequestAdminAccess: true);
    var profile1 = CreateProfile("profile-1", "Críticos", "hash-1");
    var profile2 = CreateProfile("profile-2", "Backoffice", "hash-2");
    var first = database.Synchronize(
        source,
        [profile1, profile2],
        "run-1",
        DateTimeOffset.UtcNow.AddSeconds(-2),
        DateTimeOffset.UtcNow.AddSeconds(-1));
    var second = database.Synchronize(
        source,
        [profile1, profile2],
        "run-2",
        DateTimeOffset.UtcNow.AddSeconds(-1),
        DateTimeOffset.UtcNow);
    var changedProfile1 = profile1 with { Name = "Críticos atualizados", ContentHash = "hash-1b" };
    var third = database.Synchronize(
        source,
        [changedProfile1],
        "run-3",
        DateTimeOffset.UtcNow,
        DateTimeOffset.UtcNow.AddMilliseconds(1));

    Assert(first.Inserted == 2 && first.Updated == 0, "first sync should insert both profiles");
    Assert(second.Unchanged == 2, "second sync should identify unchanged profiles");
    Assert(third.Updated == 1 && third.Missing == 1, "third sync should update one and mark one missing");
    Assert(database.GetProfiles(source.TenantKey).Count == 1, "default inventory should hide missing profiles");
    var all = database.GetProfiles(source.TenantKey, includeMissing: true);
    Assert(all.Count == 2 && all.Single(profile => profile.RemoteObjectId == "profile-2").IsPresent == false,
        "historical inventory should preserve the missing profile");
    Assert(database.GetLatestSync(source.TenantKey)?.RunId == "run-3", "latest run should be queryable");
    Assert(File.ReadAllText(databasePath).Contains("never-persist-this-token", StringComparison.Ordinal) == false,
        "inventory database must not persist the access token");

    var partialSource = source with { TenantKey = "partial-tenant", RequestAdminAccess = false };
    database.Synchronize(partialSource, [profile1, profile2], "partial-1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    var partial = database.Synchronize(partialSource, [profile1], "partial-2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
    Assert(partial.Missing == 0 && database.GetProfiles(partialSource.TenantKey).Count == 2,
        "partial inventory must not mark inaccessible profiles as missing");
    Assert(database.GetInfo().SchemaVersion == 5, "SQLite schema should be upgraded to version 5");
    return Task.CompletedTask;
}

async Task ParseDynatraceAnomalyDetectors()
{
    var payload = """
        {
          "items": [
            {
              "objectId": "detector-1",
              "schemaId": "builtin:davis.anomaly-detectors",
              "schemaVersion": "1.0.2",
              "scope": "environment",
              "modified": 1704153600000,
              "value": {
                "title": "Latência de checkout",
                "description": "Alerta de serviço",
                "enabled": true,
                "source": "A2D",
                "analyzer": {
                  "name": "dt.statistics.ui.anomaly_detection.StaticThresholdAnomalyDetectionAnalyzer",
                  "input": [
                    {"key":"query.expression","value":"timeseries latency = avg(dt.service.request.response_time)"},
                    {"key":"threshold","value":"500"}
                  ]
                },
                "eventTemplate": {
                  "properties": [
                    {"key":"event.type","value":"PERFORMANCE_EVENT"},
                    {"key":"event.name","value":"Checkout lento"},
                    {"key":"dt.alert_group","value":"checkout"}
                  ]
                },
                "executionSettings": {"actor":"service-user-id"}
              }
            },
            {
              "objectId": "detector-2",
              "schemaId": "builtin:davis.anomaly-detectors",
              "scope": "environment",
              "value": {
                "title": "Erros em logs",
                "enabled": false,
                "analyzer": {
                  "name": "dt.statistics.anomaly_detection.RecordAnomalyDetectionAnalyzer",
                  "input": [{"key":"query","value":"fetch logs | filter loglevel == \"ERROR\""}]
                },
                "eventTemplate": {"properties": []},
                "executionSettings": {"actor":"service-user-id"}
              }
            }
          ]
        }
        """;
    using var factory = new FakeRemoteHttpClientFactory(payload);
    var client = new DynatraceAnomalyDetectorClient(factory);
    var source = new DynatraceAnomalyDetectorSource(
        "tenant-key",
        "HML",
        "Homologação",
        new Uri("https://abc.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "platform-token",
        RequestAdminAccess: true);

    var detectors = await client.GetAllAsync(source);

    var staticDetector = detectors.Single(detector => detector.RemoteObjectId == "detector-1");
    var recordDetector = detectors.Single(detector => detector.RemoteObjectId == "detector-2");
    Assert(staticDetector.Model == "Estático" && staticDetector.UsesTimeseries,
        "static timeseries detector should be classified");
    Assert(staticDetector.EventType == "PERFORMANCE_EVENT" && staticDetector.AlertGroup == "checkout",
        "event properties should be extracted");
    Assert(recordDetector.Model == "Registros" && !recordDetector.UsesTimeseries && !recordDetector.Enabled,
        "record query should be classified outside the strict timeseries pattern");
    Assert(factory.Requests.Single().Address.Query.Contains("builtin%3Adavis.anomaly-detectors", StringComparison.Ordinal),
        "request should filter the Davis anomaly detector schema");
}

Task SynchronizeDynatraceAnomalyDetectors()
{
    using var folder = new TestFolder();
    var database = new SqliteLocalDatabaseService(new LocalDatabaseOptions(
        Path.Combine(folder.Path, "anomaly.db"),
        BusyTimeoutSeconds: 10,
        UseWriteAheadLogging: true));
    var source = new DynatraceAnomalyDetectorSource(
        "anomaly-tenant",
        "PRD",
        "Produção",
        new Uri("https://prod.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "not-persisted",
        RequestAdminAccess: true);
    var firstDetector = CreateDetector("detector-1", "Checkout", "hash-1", enabled: true, usesTimeseries: true);
    var secondDetector = CreateDetector("detector-2", "Logs", "hash-2", enabled: false, usesTimeseries: false);
    var first = database.SynchronizeAnomalyDetectors(
        source,
        [firstDetector, secondDetector],
        "anomaly-run-1",
        DateTimeOffset.UtcNow.AddSeconds(-2),
        DateTimeOffset.UtcNow.AddSeconds(-1));
    var changed = firstDetector with { Title = "Checkout atualizado", ContentHash = "hash-1b" };
    var second = database.SynchronizeAnomalyDetectors(
        source,
        [changed],
        "anomaly-run-2",
        DateTimeOffset.UtcNow.AddSeconds(-1),
        DateTimeOffset.UtcNow);

    Assert(first.Inserted == 2, "first anomaly sync should insert both detectors");
    Assert(second.Updated == 1 && second.Missing == 1,
        "second anomaly sync should update one detector and mark one missing");
    var all = database.GetAnomalyDetectors(source.TenantKey, includeMissing: true);
    Assert(all.Count == 2 && all.Single(detector => detector.RemoteObjectId == "detector-2").IsPresent == false,
        "missing anomaly detector should remain in local history");
    Assert(database.GetLatestAnomalyDetectorSync(source.TenantKey)?.RunId == "anomaly-run-2",
        "latest anomaly sync should be queryable");
    Assert(File.ReadAllText(database.CurrentPath).Contains("not-persisted", StringComparison.Ordinal) == false,
        "anomaly inventory must not persist credentials");
    return Task.CompletedTask;
}

async Task QueryDynatraceDavisEvents()
{
    var startedNanoseconds = DateTimeOffset.Parse("2026-09-04T12:00:00Z").ToUnixTimeMilliseconds() * 1_000_000L;
    var initial = """
        {"state":"RUNNING","requestToken":"token +/="}
        """;
    var completed = $$"""
        {
          "state": "SUCCEEDED",
          "result": {
            "records": [
              {
                "timestamp": {{startedNanoseconds}},
                "event.id": "event-1",
                "event.name": "Checkout Response Time High",
                "event.description": "Latência acima do limite",
                "event.category": "SLOWDOWN",
                "event.status": "ACTIVE",
                "event.status_transition": "CREATED",
                "event.severity": 2,
                "event.provider": "metric_events",
                "event.type": "CUSTOM_ALERT",
                "event.start": "2026-09-04T12:00:00Z",
                "dt.smartscape_source.id": "SERVICE-123",
                "dt.smartscape_source.type": "SERVICE",
                "dt.settings.object_id": "detector-1",
                "dt.settings.schema_id": "builtin:davis.anomaly-detectors",
                "dt.alert_group": "checkout",
                "dt.query": "timeseries latency = avg(metric)",
                "dt.davis.is_frequent_event": false,
                "dt.davis.is_merging_allowed": true,
                "maintenance.is_under_maintenance": false
              }
            ]
          }
        }
        """;
    using var factory = new FakeRemoteHttpClientFactory(initial, completed);
    var source = new DynatraceDavisEventSource(
        "davis-tenant",
        "PRD",
        "Produção",
        new Uri("https://abc.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "platform-token",
        LookbackHours: 24,
        ResultLimit: 5_000);

    var result = await new DynatraceDavisEventClient(factory).QueryAsync(source);

    var item = result.Events.Single();
    Assert(item.EventId == "event-1" && item.Name.Contains("Checkout", StringComparison.Ordinal),
        "Davis event identity and name should be parsed");
    Assert(item.Severity == 2 && item.SourceEntityId == "SERVICE-123" && item.IsMergingAllowed,
        "Davis event operational fields should be parsed");
    Assert(item.Timestamp == DateTimeOffset.Parse("2026-09-04T12:00:00Z"),
        "nanosecond timestamps should be parsed");
    Assert(factory.Requests.Count == 2, "execute and poll requests expected");
    Assert(factory.Requests[0].Method == HttpMethod.Post
        && factory.Requests[0].Address.Host == "abc.apps.dynatrace.com"
        && factory.Requests[0].Address.AbsolutePath.EndsWith("/query:execute", StringComparison.Ordinal),
        "query should execute on the apps domain");
    Assert(factory.Requests[0].Body.Contains("fetch dt.davis.events, from: -24h", StringComparison.Ordinal),
        "query should use the selected lookback");
    Assert(factory.Requests[1].Address.Query == "?request-token=token%20%2B%2F%3D",
        "poll request token should be encoded");
    Assert(factory.Requests.All(request => request.AuthenticationScheme == "Bearer"),
        "DQL requests should use Bearer authentication");
}

Task SynchronizeDynatraceDavisEvents()
{
    using var folder = new TestFolder();
    var database = new SqliteLocalDatabaseService(new LocalDatabaseOptions(
        Path.Combine(folder.Path, "davis-events.db"),
        BusyTimeoutSeconds: 10,
        UseWriteAheadLogging: true));
    var source = new DynatraceDavisEventSource(
        "events-tenant",
        "HML",
        "Homologação",
        new Uri("https://hml.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "never-store-platform-token",
        LookbackHours: 168,
        ResultLimit: 5_000);
    var firstEvent = CreateDavisEvent("event-1", "Checkout lento", "ACTIVE", "hash-1");
    var secondEvent = CreateDavisEvent("event-2", "Checkout indisponível", "CLOSED", "hash-2");
    var first = database.SynchronizeDavisEvents(
        source,
        new DynatraceDavisEventQueryResult([firstEvent, secondEvent], LimitReached: false),
        "events-run-1",
        DateTimeOffset.UtcNow.AddSeconds(-2),
        DateTimeOffset.UtcNow.AddSeconds(-1));
    var changed = firstEvent with { Status = "CLOSED", ContentHash = "hash-1b" };
    var second = database.SynchronizeDavisEvents(
        source,
        new DynatraceDavisEventQueryResult([changed], LimitReached: true),
        "events-run-2",
        DateTimeOffset.UtcNow.AddSeconds(-1),
        DateTimeOffset.UtcNow);

    Assert(first.Inserted == 2 && second.Updated == 1 && second.LimitReached,
        "event snapshots should insert, update and retain limit state");
    var stored = database.GetDavisEvents(source.TenantKey);
    Assert(stored.Count == 2 && stored.Single(item => item.EventId == "event-1").Status == "CLOSED",
        "historical events should remain available and current state should update");
    Assert(database.GetLatestDavisEventSync(source.TenantKey)?.RunId == "events-run-2",
        "latest Davis event query should be available");
    Assert(File.ReadAllText(database.CurrentPath).Contains("never-store-platform-token", StringComparison.Ordinal) == false,
        "Davis event database must not persist credentials");
    Assert(database.GetInfo().SchemaVersion == 5, "Davis event schema should be upgraded with problems");
    return Task.CompletedTask;
}

async Task QueryDynatraceProblems()
{
    var payload = """
        {
          "state": "SUCCEEDED",
          "result": {
            "records": [
              {
                "timestamp": "2026-09-04T13:00:00Z",
                "event.id": "problem-event-1",
                "display_id": "P-12345",
                "event.name": "Checkout indisponível",
                "event.description": "Falha correlacionada no checkout",
                "event.category": "AVAILABILITY",
                "event.status": "ACTIVE",
                "event.severity": 1,
                "event.start": "2026-09-04T12:55:00Z",
                "dt.davis.affected_users_count": 240,
                "affected_entity_ids": ["SERVICE-123", "HOST-456"],
                "affected_entity_types": ["SERVICE", "HOST"],
                "affected_service_ids": ["SERVICE-123"],
                "correlated_event_ids": ["event-1", "event-2"],
                "affected_entity_count": 2,
                "correlated_event_count": 2,
                "root_cause_entity_id": "HOST-456",
                "root_cause_entity_name": "checkout-host",
                "dt.davis.is_rootcause": true,
                "maintenance.is_under_maintenance": false
              }
            ]
          }
        }
        """;
    using var factory = new FakeRemoteHttpClientFactory(payload);
    var source = new DynatraceProblemSource(
        "problem-tenant",
        "PRD",
        "Produção",
        new Uri("https://abc.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "platform-token",
        LookbackHours: 24,
        ResultLimit: 5_000);

    var result = await new DynatraceProblemClient(factory).QueryAsync(source);

    var problem = result.Problems.Single();
    Assert(problem.DisplayId == "P-12345" && problem.Status == "ACTIVE",
        "problem identity and state should be parsed");
    Assert(problem.RootCauseEntityId == "HOST-456" && problem.RootCauseEntityName == "checkout-host",
        "root cause should be parsed");
    Assert(problem.AffectedUsersCount == 240 && problem.AffectedEntityCount == 2
        && problem.CorrelatedEventIds.SequenceEqual(["event-1", "event-2"]),
        "problem impact and correlated events should be parsed");
    Assert(factory.Requests.Single().Body.Contains("filter not(dt.davis.is_duplicate)", StringComparison.Ordinal),
        "problem query should filter duplicates before presentation");
}

Task SynchronizeDynatraceProblems()
{
    using var folder = new TestFolder();
    var database = new SqliteLocalDatabaseService(new LocalDatabaseOptions(
        Path.Combine(folder.Path, "problems.db"),
        BusyTimeoutSeconds: 10,
        UseWriteAheadLogging: true));
    var source = new DynatraceProblemSource(
        "problem-tenant",
        "PRD",
        "Produção",
        new Uri("https://prod.live.dynatrace.com"),
        RemoteAuthenticationMode.BearerToken,
        "never-store-problem-token",
        LookbackHours: 168,
        ResultLimit: 5_000);
    var firstProblem = CreateProblem("problem-1", "P-1", "ACTIVE", "hash-1");
    var secondProblem = CreateProblem("problem-2", "P-2", "CLOSED", "hash-2");
    var first = database.SynchronizeProblems(
        source,
        new DynatraceProblemQueryResult([firstProblem, secondProblem], LimitReached: false),
        "problem-run-1",
        DateTimeOffset.UtcNow.AddSeconds(-2),
        DateTimeOffset.UtcNow.AddSeconds(-1));
    var changed = firstProblem with { Status = "CLOSED", ContentHash = "hash-1b" };
    var second = database.SynchronizeProblems(
        source,
        new DynatraceProblemQueryResult([changed], LimitReached: true),
        "problem-run-2",
        DateTimeOffset.UtcNow.AddSeconds(-1),
        DateTimeOffset.UtcNow);

    Assert(first.Inserted == 2 && second.Updated == 1 && second.LimitReached,
        "problem snapshots should insert, update and retain limit state");
    var stored = database.GetProblems(source.TenantKey);
    Assert(stored.Count == 2 && stored.Single(item => item.EventId == "problem-1").Status == "CLOSED",
        "historical problems should remain and current state should update");
    Assert(stored.Single(item => item.EventId == "problem-1").AffectedEntityIds.Count == 2,
        "problem entity collections should round-trip through SQLite");
    Assert(database.GetLatestProblemSync(source.TenantKey)?.RunId == "problem-run-2",
        "latest problem query should be available");
    Assert(File.ReadAllText(database.CurrentPath).Contains("never-store-problem-token", StringComparison.Ordinal) == false,
        "problem database must not persist credentials");
    Assert(database.GetInfo().SchemaVersion == 5, "problem schema should use version 5");
    return Task.CompletedTask;
}

static DynatraceAlertingProfileSnapshot CreateProfile(string id, string name, string hash) => new(
    id,
    "builtin:alerting.profile",
    "1.0.0",
    "environment",
    name,
    string.Empty,
    SeverityRuleCount: 1,
    EventFilterCount: 0,
    RemoteCreatedAt: null,
    RemoteModifiedAt: null,
    hash,
    $"{{\"objectId\":\"{id}\",\"value\":{{\"name\":\"{name}\"}}}}");

static DynatraceAnomalyDetectorSnapshot CreateDetector(
    string id,
    string title,
    string hash,
    bool enabled,
    bool usesTimeseries) => new(
        id,
        "builtin:davis.anomaly-detectors",
        "1.0.2",
        "environment",
        title,
        string.Empty,
        "A2D",
        enabled,
        usesTimeseries
            ? "dt.statistics.ui.anomaly_detection.StaticThresholdAnomalyDetectionAnalyzer"
            : "dt.statistics.anomaly_detection.RecordAnomalyDetectionAnalyzer",
        usesTimeseries ? "Estático" : "Registros",
        usesTimeseries ? "timeseries value = avg(metric)" : "fetch logs",
        usesTimeseries,
        "CUSTOM_ALERT",
        title,
        "migration",
        "actor-id",
        AnalyzerInputCount: 1,
        EventPropertyCount: 3,
        RemoteCreatedAt: null,
        RemoteModifiedAt: null,
        hash,
        $"{{\"objectId\":\"{id}\",\"value\":{{\"title\":\"{title}\"}}}}");

static DynatraceDavisEventSnapshot CreateDavisEvent(
    string id,
    string name,
    string status,
    string hash) => new(
        id,
        name,
        "Descrição do evento",
        "SLOWDOWN",
        status,
        status == "ACTIVE" ? "CREATED" : "RECOVERED",
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
        Timestamp: DateTimeOffset.UtcNow,
        Start: DateTimeOffset.UtcNow.AddMinutes(-10),
        End: status == "ACTIVE" ? null : DateTimeOffset.UtcNow,
        hash,
        $"{{\"event.id\":\"{id}\",\"event.name\":\"{name}\",\"event.status\":\"{status}\"}}");

static DynatraceProblemSnapshot CreateProblem(
    string eventId,
    string displayId,
    string status,
    string hash) => new(
        eventId,
        displayId,
        "Checkout indisponível",
        "Descrição do problema",
        "AVAILABILITY",
        status,
        Severity: 1,
        AffectedUsersCount: 25,
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
        Timestamp: DateTimeOffset.UtcNow,
        Start: DateTimeOffset.UtcNow.AddMinutes(-20),
        End: status == "ACTIVE" ? null : DateTimeOffset.UtcNow,
        hash,
        $"{{\"event.id\":\"{eventId}\",\"display_id\":\"{displayId}\",\"event.status\":\"{status}\"}}");

static bool HasCode(ImportBatch batch, string code) =>
    batch.Diagnostics.Any(diagnostic => diagnostic.Code == code)
    || batch.Applications.SelectMany(static application => application.Diagnostics).Any(diagnostic => diagnostic.Code == code);

static string Describe(ImportBatch batch) => string.Join(
    " | ",
    batch.Diagnostics
        .Concat(batch.Applications.SelectMany(static application => application.Diagnostics))
        .Select(static diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static string FindWorkspace(string startPath)
{
    var directory = new DirectoryInfo(startPath);
    while (directory is not null)
    {
        if (File.Exists(Path.Combine(directory.FullName, "README.md"))
            && Directory.Exists(Path.Combine(directory.FullName, "samples")))
        {
            return directory.FullName;
        }

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Workspace root not found.");
}

sealed class TestFolder : IDisposable
{
    public TestFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "A2DAlertMigrator.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        var safeRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "A2DAlertMigrator.Tests"));
        var target = System.IO.Path.GetFullPath(Path);
        if (target.StartsWith(safeRoot + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(target))
        {
            for (var attempt = 1; attempt <= 5; attempt++)
            {
                try
                {
                    Directory.Delete(target, recursive: true);
                    break;
                }
                catch (IOException) when (attempt < 5)
                {
                    Thread.Sleep(50 * attempt);
                }
            }
        }
    }
}

sealed class FakeRemoteHttpClientFactory : IRemoteHttpClientFactory
{
    private readonly Queue<string> _responses;
    private readonly RecordingHandler _handler;

    public FakeRemoteHttpClientFactory(params string[] responses)
    {
        _responses = new Queue<string>(responses);
        _handler = new RecordingHandler(_responses, Requests);
    }

    public List<RecordedRequest> Requests { get; } = [];

    public string? LastError => null;

    public void Configure(RemoteHttpClientOptions options)
    {
    }

    public HttpClient CreateClient() => new(_handler, disposeHandler: false);

    public Task<RemoteConnectionTestResult> TestConnectionAsync(
        RemoteConnectionTestRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public void Dispose() => _handler.Dispose();
}

sealed class RecordingHandler(
    Queue<string> responses,
    List<RecordedRequest> requests) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);
        requests.Add(new RecordedRequest(
            request.RequestUri ?? throw new InvalidOperationException("request URI expected"),
            request.Headers.Authorization?.Scheme ?? string.Empty,
            request.Headers.Authorization?.Parameter ?? string.Empty,
            request.Method,
            body));
        if (responses.Count == 0)
        {
            throw new InvalidOperationException("No fake response available.");
        }

        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responses.Dequeue(), Encoding.UTF8, "application/json")
        };
    }
}

sealed record RecordedRequest(
    Uri Address,
    string AuthenticationScheme,
    string AuthenticationParameter,
    HttpMethod Method,
    string Body);
