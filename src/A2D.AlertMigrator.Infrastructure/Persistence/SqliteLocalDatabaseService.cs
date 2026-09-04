using System.Globalization;
using System.IO;
using System.Text.Json;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Persistence;
using Microsoft.Data.Sqlite;

namespace A2D.AlertMigrator.Infrastructure.Persistence;

public sealed class SqliteLocalDatabaseService :
    ILocalDatabaseService,
    IDynatraceAlertingProfileStore,
    IDynatraceAnomalyDetectorStore,
    IDynatraceDavisEventStore,
    IDynatraceProblemStore
{
    private const int CurrentSchemaVersion = 5;
    private readonly object _gate = new();
    private LocalDatabaseOptions _options;
    private string? _lastError;

    public SqliteLocalDatabaseService(LocalDatabaseOptions options)
    {
        options.EnsureValid();
        _options = options;
        Configure(options);
    }

    public string CurrentPath
    {
        get
        {
            lock (_gate)
            {
                return _options.FilePath;
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

    public void Configure(LocalDatabaseOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.EnsureValid();

        lock (_gate)
        {
            _options = options;
            try
            {
                InitializeDatabase();
                _lastError = null;
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                _lastError = exception.Message;
            }
        }
    }

    public LocalDatabaseInfo GetInfo()
    {
        lock (_gate)
        {
            try
            {
                InitializeDatabase();
                using var connection = OpenConnection();
                var schemaVersion = Convert.ToInt32(ExecuteScalar(connection, "PRAGMA user_version;"), CultureInfo.InvariantCulture);
                var journalMode = Convert.ToString(ExecuteScalar(connection, "PRAGMA journal_mode;"), CultureInfo.InvariantCulture)
                    ?? "unknown";
                var historyRecordCount = Convert.ToInt64(
                    ExecuteScalar(connection, "SELECT COUNT(*) FROM migration_history;"),
                    CultureInfo.InvariantCulture);
                _lastError = null;

                return new LocalDatabaseInfo(
                    _options.FilePath,
                    File.Exists(_options.FilePath),
                    GetStorageSize(_options.FilePath),
                    schemaVersion,
                    journalMode.ToUpperInvariant(),
                    historyRecordCount,
                    LastError: null);
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                _lastError = exception.Message;
                return new LocalDatabaseInfo(
                    _options.FilePath,
                    File.Exists(_options.FilePath),
                    GetStorageSize(_options.FilePath),
                    SchemaVersion: 0,
                    JournalMode: "INDISPONÍVEL",
                    HistoryRecordCount: 0,
                    LastError: _lastError);
            }
        }
    }

    public bool VerifyIntegrity()
    {
        lock (_gate)
        {
            try
            {
                using var connection = OpenConnection();
                var result = Convert.ToString(ExecuteScalar(connection, "PRAGMA quick_check;"), CultureInfo.InvariantCulture);
                var isHealthy = string.Equals(result, "ok", StringComparison.OrdinalIgnoreCase);
                _lastError = isHealthy ? null : $"O SQLite retornou: {result ?? "resultado vazio"}.";
                return isHealthy;
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                _lastError = exception.Message;
                return false;
            }
        }
    }

    public void RecordImport(ImportExecutionRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            try
            {
                InitializeDatabase();
                using var connection = OpenConnection();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO migration_history(
                        id,
                        started_utc,
                        completed_utc,
                        status,
                        source_type,
                        applications,
                        rules,
                        errors,
                        warnings)
                    VALUES (
                        $id,
                        $startedUtc,
                        $completedUtc,
                        $status,
                        $sourceType,
                        $applications,
                        $rules,
                        $errors,
                        $warnings)
                    ON CONFLICT(id) DO UPDATE SET
                        completed_utc = excluded.completed_utc,
                        status = excluded.status,
                        applications = excluded.applications,
                        rules = excluded.rules,
                        errors = excluded.errors,
                        warnings = excluded.warnings;
                    """;
                command.Parameters.AddWithValue("$id", record.Id);
                command.Parameters.AddWithValue("$startedUtc", record.StartedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$completedUtc", record.CompletedUtc.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$status", record.Status);
                command.Parameters.AddWithValue("$sourceType", record.SourceType);
                command.Parameters.AddWithValue("$applications", record.Applications);
                command.Parameters.AddWithValue("$rules", record.Rules);
                command.Parameters.AddWithValue("$errors", record.Errors);
                command.Parameters.AddWithValue("$warnings", record.Warnings);
                command.ExecuteNonQuery();
                _lastError = null;
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                _lastError = exception.Message;
            }
        }
    }

    public void Export(string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(destinationPath))
        {
            throw new ArgumentException("Informe o arquivo de destino do backup.", nameof(destinationPath));
        }

        var destination = Path.GetFullPath(destinationPath);

        lock (_gate)
        {
            if (string.Equals(destination, _options.FilePath, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("Escolha um arquivo diferente do banco em uso.", nameof(destinationPath));
            }

            var destinationDirectory = Path.GetDirectoryName(destination)
                ?? throw new InvalidOperationException("A pasta de exportação é inválida.");
            Directory.CreateDirectory(destinationDirectory);
            var temporaryPath = Path.Combine(destinationDirectory, $"a2d-sqlite-export-{Guid.NewGuid():N}.tmp");

            try
            {
                InitializeDatabase();
                using var source = OpenConnection();
                using var target = new SqliteConnection(new SqliteConnectionStringBuilder
                {
                    DataSource = temporaryPath,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                }.ToString());
                target.Open();
                source.BackupDatabase(target);
                target.Close();
                source.Close();

                File.Move(temporaryPath, destination, overwrite: true);
                _lastError = null;
            }
            catch (Exception exception) when (IsDatabaseException(exception))
            {
                _lastError = exception.Message;
                throw new InvalidOperationException($"Não foi possível exportar o banco SQLite: {exception.Message}", exception);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try
                    {
                        File.Delete(temporaryPath);
                    }
                    catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
                    {
                        _lastError ??= cleanupException.Message;
                    }
                }
            }
        }
    }

    public IReadOnlyList<StoredDynatraceAlertingProfile> GetProfiles(
        string tenantKey,
        bool includeMissing = false)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    tenant_key,
                    environment,
                    tenant_alias,
                    tenant_base_address,
                    remote_object_id,
                    schema_id,
                    schema_version,
                    scope,
                    name,
                    management_zone,
                    severity_rule_count,
                    event_filter_count,
                    remote_created_utc,
                    remote_modified_utc,
                    first_seen_utc,
                    last_seen_utc,
                    is_present,
                    raw_json
                FROM dynatrace_alerting_profiles
                WHERE tenant_key = $tenantKey
                  AND ($includeMissing = 1 OR is_present = 1)
                ORDER BY name COLLATE NOCASE, remote_object_id;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            command.Parameters.AddWithValue("$includeMissing", includeMissing ? 1 : 0);

            using var reader = command.ExecuteReader();
            var profiles = new List<StoredDynatraceAlertingProfile>();
            while (reader.Read())
            {
                profiles.Add(new StoredDynatraceAlertingProfile(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    new Uri(reader.GetString(3), UriKind.Absolute),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetInt32(10),
                    reader.GetInt32(11),
                    ReadOptionalTimestamp(reader, 12),
                    ReadOptionalTimestamp(reader, 13),
                    ReadRequiredTimestamp(reader, 14),
                    ReadRequiredTimestamp(reader, 15),
                    reader.GetInt32(16) == 1,
                    reader.GetString(17)));
            }

            _lastError = null;
            return profiles;
        }
    }

    public DynatraceAlertingProfileSyncStatus? GetLatestSync(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    id,
                    started_utc,
                    completed_utc,
                    status,
                    received,
                    inserted,
                    updated,
                    unchanged,
                    missing,
                    is_complete_inventory,
                    error_message
                FROM dynatrace_alert_profile_sync_runs
                WHERE tenant_key = $tenantKey
                ORDER BY started_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                _lastError = null;
                return null;
            }

            _lastError = null;
            return new DynatraceAlertingProfileSyncStatus(
                reader.GetString(0),
                ReadRequiredTimestamp(reader, 1),
                ReadOptionalTimestamp(reader, 2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9) == 1,
                reader.GetString(10));
        }
    }

    public DynatraceAlertingProfileSyncResult Synchronize(
        DynatraceAlertingProfileSource source,
        IReadOnlyList<DynatraceAlertingProfileSnapshot> profiles,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(profiles);
        source.EnsureValid();

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                var existing = ReadExistingProfiles(connection, transaction, source.TenantKey);
                var receivedIds = profiles.Select(static profile => profile.RemoteObjectId).ToHashSet(StringComparer.Ordinal);
                var inserted = 0;
                var updated = 0;
                var unchanged = 0;

                foreach (var profile in profiles)
                {
                    if (!existing.TryGetValue(profile.RemoteObjectId, out var current))
                    {
                        inserted++;
                    }
                    else if (!current.IsPresent
                        || !string.Equals(current.ContentHash, profile.ContentHash, StringComparison.Ordinal))
                    {
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    UpsertProfile(connection, transaction, source, profile, completedAt);
                }

                var missing = 0;
                if (source.RequestAdminAccess)
                {
                    missing = existing.Count(pair => pair.Value.IsPresent && !receivedIds.Contains(pair.Key));
                    MarkMissingProfiles(connection, transaction, source.TenantKey, receivedIds);
                }

                InsertSyncRun(
                    connection,
                    transaction,
                    source,
                    runId,
                    startedAt,
                    completedAt,
                    "success",
                    profiles.Count,
                    inserted,
                    updated,
                    unchanged,
                    missing,
                    errorMessage: string.Empty);
                transaction.Commit();
                _lastError = null;

                return new DynatraceAlertingProfileSyncResult(
                    runId,
                    startedAt,
                    completedAt,
                    profiles.Count,
                    inserted,
                    updated,
                    unchanged,
                    missing,
                    source.RequestAdminAccess);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void RecordFailedSync(
        DynatraceAlertingProfileSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertSyncRun(
                connection,
                transaction,
                source,
                runId,
                startedAt,
                completedAt,
                "failed",
                received: 0,
                inserted: 0,
                updated: 0,
                unchanged: 0,
                missing: 0,
                errorMessage);
            transaction.Commit();
        }
    }

    public IReadOnlyList<StoredDynatraceAnomalyDetector> GetAnomalyDetectors(
        string tenantKey,
        bool includeMissing = false)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    tenant_key, environment, tenant_alias, tenant_base_address,
                    remote_object_id, schema_id, schema_version, scope,
                    title, description, source_name, enabled, analyzer_name, model,
                    dql_query, uses_timeseries, event_type, event_name, alert_group, actor,
                    analyzer_input_count, event_property_count,
                    remote_created_utc, remote_modified_utc,
                    first_seen_utc, last_seen_utc, is_present, raw_json
                FROM dynatrace_anomaly_detectors
                WHERE tenant_key = $tenantKey
                  AND ($includeMissing = 1 OR is_present = 1)
                ORDER BY title COLLATE NOCASE, remote_object_id;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            command.Parameters.AddWithValue("$includeMissing", includeMissing ? 1 : 0);
            using var reader = command.ExecuteReader();
            var detectors = new List<StoredDynatraceAnomalyDetector>();
            while (reader.Read())
            {
                detectors.Add(new StoredDynatraceAnomalyDetector(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    new Uri(reader.GetString(3), UriKind.Absolute),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.GetString(10),
                    reader.GetInt32(11) == 1,
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.GetInt32(15) == 1,
                    reader.GetString(16),
                    reader.GetString(17),
                    reader.GetString(18),
                    reader.GetString(19),
                    reader.GetInt32(20),
                    reader.GetInt32(21),
                    ReadOptionalTimestamp(reader, 22),
                    ReadOptionalTimestamp(reader, 23),
                    ReadRequiredTimestamp(reader, 24),
                    ReadRequiredTimestamp(reader, 25),
                    reader.GetInt32(26) == 1,
                    reader.GetString(27)));
            }

            _lastError = null;
            return detectors;
        }
    }

    public DynatraceAnomalyDetectorSyncStatus? GetLatestAnomalyDetectorSync(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, started_utc, completed_utc, status,
                       received, inserted, updated, unchanged, missing,
                       is_complete_inventory, error_message
                FROM dynatrace_anomaly_detector_sync_runs
                WHERE tenant_key = $tenantKey
                ORDER BY started_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                _lastError = null;
                return null;
            }

            _lastError = null;
            return new DynatraceAnomalyDetectorSyncStatus(
                reader.GetString(0),
                ReadRequiredTimestamp(reader, 1),
                ReadOptionalTimestamp(reader, 2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9) == 1,
                reader.GetString(10));
        }
    }

    public DynatraceAnomalyDetectorSyncResult SynchronizeAnomalyDetectors(
        DynatraceAnomalyDetectorSource source,
        IReadOnlyList<DynatraceAnomalyDetectorSnapshot> detectors,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(detectors);
        source.EnsureValid();

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                var existing = ReadExistingAnomalyDetectors(connection, transaction, source.TenantKey);
                var receivedIds = detectors.Select(static detector => detector.RemoteObjectId).ToHashSet(StringComparer.Ordinal);
                var inserted = 0;
                var updated = 0;
                var unchanged = 0;

                foreach (var detector in detectors)
                {
                    if (!existing.TryGetValue(detector.RemoteObjectId, out var current))
                    {
                        inserted++;
                    }
                    else if (!current.IsPresent
                        || !string.Equals(current.ContentHash, detector.ContentHash, StringComparison.Ordinal))
                    {
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    UpsertAnomalyDetector(connection, transaction, source, detector, completedAt);
                }

                var missing = 0;
                if (source.RequestAdminAccess)
                {
                    missing = existing.Count(pair => pair.Value.IsPresent && !receivedIds.Contains(pair.Key));
                    MarkMissingAnomalyDetectors(connection, transaction, source.TenantKey, receivedIds);
                }

                InsertAnomalyDetectorSyncRun(
                    connection,
                    transaction,
                    source,
                    runId,
                    startedAt,
                    completedAt,
                    "success",
                    detectors.Count,
                    inserted,
                    updated,
                    unchanged,
                    missing,
                    string.Empty);
                transaction.Commit();
                _lastError = null;
                return new DynatraceAnomalyDetectorSyncResult(
                    runId,
                    startedAt,
                    completedAt,
                    detectors.Count,
                    inserted,
                    updated,
                    unchanged,
                    missing,
                    source.RequestAdminAccess);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void RecordFailedAnomalyDetectorSync(
        DynatraceAnomalyDetectorSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertAnomalyDetectorSyncRun(
                connection,
                transaction,
                source,
                runId,
                startedAt,
                completedAt,
                "failed",
                received: 0,
                inserted: 0,
                updated: 0,
                unchanged: 0,
                missing: 0,
                errorMessage);
            transaction.Commit();
        }
    }

    public IReadOnlyList<StoredDynatraceDavisEvent> GetDavisEvents(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    tenant_key, environment, tenant_alias, tenant_base_address,
                    event_id, event_name, description, category, status, status_transition,
                    severity, provider, event_type, source_entity_id, source_entity_type,
                    settings_object_id, settings_schema_id, alert_group, dql_query,
                    is_frequent, is_merging_allowed, is_under_maintenance,
                    event_timestamp_utc, event_start_utc, event_end_utc,
                    first_seen_utc, last_seen_utc, raw_json
                FROM dynatrace_davis_events
                WHERE tenant_key = $tenantKey
                ORDER BY COALESCE(event_start_utc, event_timestamp_utc) DESC, event_name COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            using var reader = command.ExecuteReader();
            var events = new List<StoredDynatraceDavisEvent>();
            while (reader.Read())
            {
                events.Add(new StoredDynatraceDavisEvent(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    new Uri(reader.GetString(3), UriKind.Absolute),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.GetString(11),
                    reader.GetString(12),
                    reader.GetString(13),
                    reader.GetString(14),
                    reader.GetString(15),
                    reader.GetString(16),
                    reader.GetString(17),
                    reader.GetString(18),
                    reader.GetInt32(19) == 1,
                    reader.GetInt32(20) == 1,
                    reader.GetInt32(21) == 1,
                    ReadOptionalTimestamp(reader, 22),
                    ReadOptionalTimestamp(reader, 23),
                    ReadOptionalTimestamp(reader, 24),
                    ReadRequiredTimestamp(reader, 25),
                    ReadRequiredTimestamp(reader, 26),
                    reader.GetString(27)));
            }

            _lastError = null;
            return events;
        }
    }

    public DynatraceDavisEventSyncStatus? GetLatestDavisEventSync(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, started_utc, completed_utc, status, lookback_hours, result_limit,
                       received, inserted, updated, unchanged, limit_reached, error_message
                FROM dynatrace_davis_event_sync_runs
                WHERE tenant_key = $tenantKey
                ORDER BY started_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                _lastError = null;
                return null;
            }

            _lastError = null;
            return new DynatraceDavisEventSyncStatus(
                reader.GetString(0),
                ReadRequiredTimestamp(reader, 1),
                ReadOptionalTimestamp(reader, 2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10) == 1,
                reader.GetString(11));
        }
    }

    public DynatraceDavisEventSyncResult SynchronizeDavisEvents(
        DynatraceDavisEventSource source,
        DynatraceDavisEventQueryResult queryResult,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(queryResult);
        source.EnsureValid();

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                var existing = ReadExistingDavisEvents(connection, transaction, source.TenantKey);
                var inserted = 0;
                var updated = 0;
                var unchanged = 0;
                foreach (var item in queryResult.Events)
                {
                    if (!existing.TryGetValue(item.EventId, out var contentHash))
                    {
                        inserted++;
                    }
                    else if (!string.Equals(contentHash, item.ContentHash, StringComparison.Ordinal))
                    {
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    UpsertDavisEvent(connection, transaction, source, item, completedAt);
                }

                InsertDavisEventSyncRun(
                    connection,
                    transaction,
                    source,
                    runId,
                    startedAt,
                    completedAt,
                    "success",
                    queryResult.Events.Count,
                    inserted,
                    updated,
                    unchanged,
                    queryResult.LimitReached,
                    string.Empty);
                transaction.Commit();
                _lastError = null;
                return new DynatraceDavisEventSyncResult(
                    runId,
                    startedAt,
                    completedAt,
                    source.LookbackHours,
                    source.ResultLimit,
                    queryResult.Events.Count,
                    inserted,
                    updated,
                    unchanged,
                    queryResult.LimitReached);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void RecordFailedDavisEventSync(
        DynatraceDavisEventSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(source);

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertDavisEventSyncRun(
                connection,
                transaction,
                source,
                runId,
                startedAt,
                completedAt,
                "failed",
                received: 0,
                inserted: 0,
                updated: 0,
                unchanged: 0,
                limitReached: false,
                errorMessage);
            transaction.Commit();
        }
    }

    public IReadOnlyList<StoredDynatraceProblem> GetProblems(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    tenant_key, environment, tenant_alias, tenant_base_address,
                    event_id, display_id, problem_name, description, category, status,
                    severity, affected_users_count, affected_entity_count, correlated_event_count,
                    root_cause_entity_id, root_cause_entity_name, root_cause_entity_type,
                    affected_entity_ids_json, affected_entity_types_json,
                    affected_service_ids_json, correlated_event_ids_json,
                    is_root_cause, is_under_maintenance,
                    event_timestamp_utc, event_start_utc, event_end_utc,
                    first_seen_utc, last_seen_utc, raw_json
                FROM dynatrace_problems
                WHERE tenant_key = $tenantKey
                ORDER BY COALESCE(event_start_utc, event_timestamp_utc) DESC, display_id COLLATE NOCASE;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            using var reader = command.ExecuteReader();
            var problems = new List<StoredDynatraceProblem>();
            while (reader.Read())
            {
                problems.Add(new StoredDynatraceProblem(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    new Uri(reader.GetString(3), UriKind.Absolute),
                    reader.GetString(4),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetString(7),
                    reader.GetString(8),
                    reader.GetString(9),
                    reader.IsDBNull(10) ? null : reader.GetInt32(10),
                    reader.GetInt64(11),
                    reader.GetInt32(12),
                    reader.GetInt32(13),
                    reader.GetString(14),
                    reader.GetString(15),
                    reader.GetString(16),
                    DeserializeStringList(reader.GetString(17)),
                    DeserializeStringList(reader.GetString(18)),
                    DeserializeStringList(reader.GetString(19)),
                    DeserializeStringList(reader.GetString(20)),
                    reader.GetInt32(21) == 1,
                    reader.GetInt32(22) == 1,
                    ReadOptionalTimestamp(reader, 23),
                    ReadOptionalTimestamp(reader, 24),
                    ReadOptionalTimestamp(reader, 25),
                    ReadRequiredTimestamp(reader, 26),
                    ReadRequiredTimestamp(reader, 27),
                    reader.GetString(28)));
            }

            _lastError = null;
            return problems;
        }
    }

    public DynatraceProblemSyncStatus? GetLatestProblemSync(string tenantKey)
    {
        if (string.IsNullOrWhiteSpace(tenantKey))
        {
            throw new ArgumentException("Informe o ambiente que será consultado.", nameof(tenantKey));
        }

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT id, started_utc, completed_utc, status, lookback_hours, result_limit,
                       received, inserted, updated, unchanged, limit_reached, error_message
                FROM dynatrace_problem_sync_runs
                WHERE tenant_key = $tenantKey
                ORDER BY started_utc DESC
                LIMIT 1;
                """;
            command.Parameters.AddWithValue("$tenantKey", tenantKey);
            using var reader = command.ExecuteReader();
            if (!reader.Read())
            {
                _lastError = null;
                return null;
            }

            _lastError = null;
            return new DynatraceProblemSyncStatus(
                reader.GetString(0),
                ReadRequiredTimestamp(reader, 1),
                ReadOptionalTimestamp(reader, 2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetInt32(5),
                reader.GetInt32(6),
                reader.GetInt32(7),
                reader.GetInt32(8),
                reader.GetInt32(9),
                reader.GetInt32(10) == 1,
                reader.GetString(11));
        }
    }

    public DynatraceProblemSyncResult SynchronizeProblems(
        DynatraceProblemSource source,
        DynatraceProblemQueryResult queryResult,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(queryResult);
        source.EnsureValid();

        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                var existing = ReadExistingProblems(connection, transaction, source.TenantKey);
                var inserted = 0;
                var updated = 0;
                var unchanged = 0;
                foreach (var problem in queryResult.Problems)
                {
                    if (!existing.TryGetValue(problem.EventId, out var contentHash))
                    {
                        inserted++;
                    }
                    else if (!string.Equals(contentHash, problem.ContentHash, StringComparison.Ordinal))
                    {
                        updated++;
                    }
                    else
                    {
                        unchanged++;
                    }

                    UpsertProblem(connection, transaction, source, problem, completedAt);
                }

                InsertProblemSyncRun(
                    connection, transaction, source, runId, startedAt, completedAt,
                    "success", queryResult.Problems.Count, inserted, updated, unchanged,
                    queryResult.LimitReached, string.Empty);
                transaction.Commit();
                _lastError = null;
                return new DynatraceProblemSyncResult(
                    runId, startedAt, completedAt, source.LookbackHours, source.ResultLimit,
                    queryResult.Problems.Count, inserted, updated, unchanged, queryResult.LimitReached);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
    }

    public void RecordFailedProblemSync(
        DynatraceProblemSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string errorMessage)
    {
        ArgumentNullException.ThrowIfNull(source);
        lock (_gate)
        {
            InitializeDatabase();
            using var connection = OpenConnection();
            using var transaction = connection.BeginTransaction();
            InsertProblemSyncRun(
                connection, transaction, source, runId, startedAt, completedAt,
                "failed", 0, 0, 0, 0, false, errorMessage);
            transaction.Commit();
        }
    }

    private void InitializeDatabase()
    {
        var directory = Path.GetDirectoryName(_options.FilePath)
            ?? throw new InvalidOperationException("A pasta do banco SQLite é inválida.");
        Directory.CreateDirectory(directory);

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var journalMode = _options.UseWriteAheadLogging ? "WAL" : "DELETE";
        command.CommandText = $"""
            PRAGMA journal_mode = {journalMode};
            PRAGMA synchronous = NORMAL;
            CREATE TABLE IF NOT EXISTS schema_migrations (
                version INTEGER NOT NULL PRIMARY KEY,
                applied_utc TEXT NOT NULL
            );
            INSERT OR IGNORE INTO schema_migrations(version, applied_utc)
            VALUES ($schemaVersion, $appliedUtc);
            PRAGMA user_version = {CurrentSchemaVersion};
            CREATE TABLE IF NOT EXISTS migration_history (
                id TEXT NOT NULL PRIMARY KEY,
                started_utc TEXT NOT NULL,
                completed_utc TEXT,
                status TEXT NOT NULL,
                source_type TEXT NOT NULL,
                applications INTEGER NOT NULL DEFAULT 0,
                rules INTEGER NOT NULL DEFAULT 0,
                errors INTEGER NOT NULL DEFAULT 0,
                warnings INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS dynatrace_alerting_profiles (
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                tenant_base_address TEXT NOT NULL,
                remote_object_id TEXT NOT NULL,
                schema_id TEXT NOT NULL,
                schema_version TEXT NOT NULL,
                scope TEXT NOT NULL,
                name TEXT NOT NULL,
                management_zone TEXT NOT NULL,
                severity_rule_count INTEGER NOT NULL DEFAULT 0,
                event_filter_count INTEGER NOT NULL DEFAULT 0,
                remote_created_utc TEXT,
                remote_modified_utc TEXT,
                content_hash TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                is_present INTEGER NOT NULL DEFAULT 1 CHECK(is_present IN (0, 1)),
                PRIMARY KEY (tenant_key, remote_object_id)
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_alerting_profiles_tenant_name
                ON dynatrace_alerting_profiles(tenant_key, is_present, name COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS dynatrace_alert_profile_sync_runs (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                started_utc TEXT NOT NULL,
                completed_utc TEXT,
                status TEXT NOT NULL CHECK(status IN ('success', 'failed')),
                received INTEGER NOT NULL DEFAULT 0,
                inserted INTEGER NOT NULL DEFAULT 0,
                updated INTEGER NOT NULL DEFAULT 0,
                unchanged INTEGER NOT NULL DEFAULT 0,
                missing INTEGER NOT NULL DEFAULT 0,
                is_complete_inventory INTEGER NOT NULL DEFAULT 0 CHECK(is_complete_inventory IN (0, 1)),
                error_message TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_alert_profile_sync_runs_tenant_started
                ON dynatrace_alert_profile_sync_runs(tenant_key, started_utc DESC);
            CREATE TABLE IF NOT EXISTS dynatrace_anomaly_detectors (
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                tenant_base_address TEXT NOT NULL,
                remote_object_id TEXT NOT NULL,
                schema_id TEXT NOT NULL,
                schema_version TEXT NOT NULL,
                scope TEXT NOT NULL,
                title TEXT NOT NULL,
                description TEXT NOT NULL,
                source_name TEXT NOT NULL,
                enabled INTEGER NOT NULL CHECK(enabled IN (0, 1)),
                analyzer_name TEXT NOT NULL,
                model TEXT NOT NULL,
                dql_query TEXT NOT NULL,
                uses_timeseries INTEGER NOT NULL CHECK(uses_timeseries IN (0, 1)),
                event_type TEXT NOT NULL,
                event_name TEXT NOT NULL,
                alert_group TEXT NOT NULL,
                actor TEXT NOT NULL,
                analyzer_input_count INTEGER NOT NULL DEFAULT 0,
                event_property_count INTEGER NOT NULL DEFAULT 0,
                remote_created_utc TEXT,
                remote_modified_utc TEXT,
                content_hash TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                is_present INTEGER NOT NULL DEFAULT 1 CHECK(is_present IN (0, 1)),
                PRIMARY KEY (tenant_key, remote_object_id)
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_anomaly_detectors_tenant_title
                ON dynatrace_anomaly_detectors(tenant_key, is_present, title COLLATE NOCASE);
            CREATE TABLE IF NOT EXISTS dynatrace_anomaly_detector_sync_runs (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                started_utc TEXT NOT NULL,
                completed_utc TEXT,
                status TEXT NOT NULL CHECK(status IN ('success', 'failed')),
                received INTEGER NOT NULL DEFAULT 0,
                inserted INTEGER NOT NULL DEFAULT 0,
                updated INTEGER NOT NULL DEFAULT 0,
                unchanged INTEGER NOT NULL DEFAULT 0,
                missing INTEGER NOT NULL DEFAULT 0,
                is_complete_inventory INTEGER NOT NULL DEFAULT 0 CHECK(is_complete_inventory IN (0, 1)),
                error_message TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_anomaly_detector_sync_runs_tenant_started
                ON dynatrace_anomaly_detector_sync_runs(tenant_key, started_utc DESC);
            CREATE TABLE IF NOT EXISTS dynatrace_davis_events (
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                tenant_base_address TEXT NOT NULL,
                event_id TEXT NOT NULL,
                event_name TEXT NOT NULL,
                description TEXT NOT NULL,
                category TEXT NOT NULL,
                status TEXT NOT NULL,
                status_transition TEXT NOT NULL,
                severity INTEGER,
                provider TEXT NOT NULL,
                event_type TEXT NOT NULL,
                source_entity_id TEXT NOT NULL,
                source_entity_type TEXT NOT NULL,
                settings_object_id TEXT NOT NULL,
                settings_schema_id TEXT NOT NULL,
                alert_group TEXT NOT NULL,
                dql_query TEXT NOT NULL,
                is_frequent INTEGER NOT NULL DEFAULT 0 CHECK(is_frequent IN (0, 1)),
                is_merging_allowed INTEGER NOT NULL DEFAULT 0 CHECK(is_merging_allowed IN (0, 1)),
                is_under_maintenance INTEGER NOT NULL DEFAULT 0 CHECK(is_under_maintenance IN (0, 1)),
                event_timestamp_utc TEXT,
                event_start_utc TEXT,
                event_end_utc TEXT,
                content_hash TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_key, event_id)
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_davis_events_tenant_start
                ON dynatrace_davis_events(tenant_key, event_start_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_dynatrace_davis_events_tenant_status_severity
                ON dynatrace_davis_events(tenant_key, status, severity);
            CREATE TABLE IF NOT EXISTS dynatrace_davis_event_sync_runs (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                started_utc TEXT NOT NULL,
                completed_utc TEXT,
                status TEXT NOT NULL CHECK(status IN ('success', 'failed')),
                lookback_hours INTEGER NOT NULL,
                result_limit INTEGER NOT NULL,
                received INTEGER NOT NULL DEFAULT 0,
                inserted INTEGER NOT NULL DEFAULT 0,
                updated INTEGER NOT NULL DEFAULT 0,
                unchanged INTEGER NOT NULL DEFAULT 0,
                limit_reached INTEGER NOT NULL DEFAULT 0 CHECK(limit_reached IN (0, 1)),
                error_message TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_davis_event_sync_runs_tenant_started
                ON dynatrace_davis_event_sync_runs(tenant_key, started_utc DESC);
            CREATE TABLE IF NOT EXISTS dynatrace_problems (
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                tenant_base_address TEXT NOT NULL,
                event_id TEXT NOT NULL,
                display_id TEXT NOT NULL,
                problem_name TEXT NOT NULL,
                description TEXT NOT NULL,
                category TEXT NOT NULL,
                status TEXT NOT NULL,
                severity INTEGER,
                affected_users_count INTEGER NOT NULL DEFAULT 0,
                affected_entity_count INTEGER NOT NULL DEFAULT 0,
                correlated_event_count INTEGER NOT NULL DEFAULT 0,
                root_cause_entity_id TEXT NOT NULL,
                root_cause_entity_name TEXT NOT NULL,
                root_cause_entity_type TEXT NOT NULL,
                affected_entity_ids_json TEXT NOT NULL,
                affected_entity_types_json TEXT NOT NULL,
                affected_service_ids_json TEXT NOT NULL,
                correlated_event_ids_json TEXT NOT NULL,
                is_root_cause INTEGER NOT NULL DEFAULT 0 CHECK(is_root_cause IN (0, 1)),
                is_under_maintenance INTEGER NOT NULL DEFAULT 0 CHECK(is_under_maintenance IN (0, 1)),
                event_timestamp_utc TEXT,
                event_start_utc TEXT,
                event_end_utc TEXT,
                content_hash TEXT NOT NULL,
                raw_json TEXT NOT NULL,
                first_seen_utc TEXT NOT NULL,
                last_seen_utc TEXT NOT NULL,
                PRIMARY KEY (tenant_key, event_id)
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_problems_tenant_start
                ON dynatrace_problems(tenant_key, event_start_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_dynatrace_problems_tenant_status_category
                ON dynatrace_problems(tenant_key, status, category);
            CREATE TABLE IF NOT EXISTS dynatrace_problem_sync_runs (
                id TEXT NOT NULL PRIMARY KEY,
                tenant_key TEXT NOT NULL,
                environment TEXT NOT NULL,
                tenant_alias TEXT NOT NULL,
                started_utc TEXT NOT NULL,
                completed_utc TEXT,
                status TEXT NOT NULL CHECK(status IN ('success', 'failed')),
                lookback_hours INTEGER NOT NULL,
                result_limit INTEGER NOT NULL,
                received INTEGER NOT NULL DEFAULT 0,
                inserted INTEGER NOT NULL DEFAULT 0,
                updated INTEGER NOT NULL DEFAULT 0,
                unchanged INTEGER NOT NULL DEFAULT 0,
                limit_reached INTEGER NOT NULL DEFAULT 0 CHECK(limit_reached IN (0, 1)),
                error_message TEXT NOT NULL DEFAULT ''
            );
            CREATE INDEX IF NOT EXISTS ix_dynatrace_problem_sync_runs_tenant_started
                ON dynatrace_problem_sync_runs(tenant_key, started_utc DESC);
            """;
        command.Parameters.AddWithValue("$schemaVersion", CurrentSchemaVersion);
        command.Parameters.AddWithValue("$appliedUtc", DateTimeOffset.UtcNow.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, ExistingProfile> ReadExistingProfiles(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tenantKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT remote_object_id, content_hash, is_present
            FROM dynatrace_alerting_profiles
            WHERE tenant_key = $tenantKey;
            """;
        command.Parameters.AddWithValue("$tenantKey", tenantKey);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, ExistingProfile>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result.Add(reader.GetString(0), new ExistingProfile(reader.GetString(1), reader.GetInt32(2) == 1));
        }

        return result;
    }

    private static void UpsertProfile(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceAlertingProfileSource source,
        DynatraceAlertingProfileSnapshot profile,
        DateTimeOffset synchronizedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_alerting_profiles(
                tenant_key, environment, tenant_alias, tenant_base_address,
                remote_object_id, schema_id, schema_version, scope, name, management_zone,
                severity_rule_count, event_filter_count, remote_created_utc, remote_modified_utc,
                content_hash, raw_json, first_seen_utc, last_seen_utc, is_present)
            VALUES (
                $tenantKey, $environment, $tenantAlias, $tenantBaseAddress,
                $remoteObjectId, $schemaId, $schemaVersion, $scope, $name, $managementZone,
                $severityRuleCount, $eventFilterCount, $remoteCreatedUtc, $remoteModifiedUtc,
                $contentHash, $rawJson, $firstSeenUtc, $lastSeenUtc, 1)
            ON CONFLICT(tenant_key, remote_object_id) DO UPDATE SET
                environment = excluded.environment,
                tenant_alias = excluded.tenant_alias,
                tenant_base_address = excluded.tenant_base_address,
                schema_id = excluded.schema_id,
                schema_version = excluded.schema_version,
                scope = excluded.scope,
                name = excluded.name,
                management_zone = excluded.management_zone,
                severity_rule_count = excluded.severity_rule_count,
                event_filter_count = excluded.event_filter_count,
                remote_created_utc = excluded.remote_created_utc,
                remote_modified_utc = excluded.remote_modified_utc,
                content_hash = excluded.content_hash,
                raw_json = excluded.raw_json,
                last_seen_utc = excluded.last_seen_utc,
                is_present = 1;
            """;
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$tenantBaseAddress", source.BaseAddress.AbsoluteUri.TrimEnd('/'));
        command.Parameters.AddWithValue("$remoteObjectId", profile.RemoteObjectId);
        command.Parameters.AddWithValue("$schemaId", profile.SchemaId);
        command.Parameters.AddWithValue("$schemaVersion", profile.SchemaVersion);
        command.Parameters.AddWithValue("$scope", profile.Scope);
        command.Parameters.AddWithValue("$name", profile.Name);
        command.Parameters.AddWithValue("$managementZone", profile.ManagementZone);
        command.Parameters.AddWithValue("$severityRuleCount", profile.SeverityRuleCount);
        command.Parameters.AddWithValue("$eventFilterCount", profile.EventFilterCount);
        command.Parameters.AddWithValue("$remoteCreatedUtc", ToDatabaseValue(profile.RemoteCreatedAt));
        command.Parameters.AddWithValue("$remoteModifiedUtc", ToDatabaseValue(profile.RemoteModifiedAt));
        command.Parameters.AddWithValue("$contentHash", profile.ContentHash);
        command.Parameters.AddWithValue("$rawJson", profile.RawJson);
        command.Parameters.AddWithValue("$firstSeenUtc", ToDatabaseValue(synchronizedAt));
        command.Parameters.AddWithValue("$lastSeenUtc", ToDatabaseValue(synchronizedAt));
        command.ExecuteNonQuery();
    }

    private static void MarkMissingProfiles(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tenantKey,
        IReadOnlySet<string> receivedIds)
    {
        using (var createCommand = connection.CreateCommand())
        {
            createCommand.Transaction = transaction;
            createCommand.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS received_alert_profile_ids (
                    remote_object_id TEXT NOT NULL PRIMARY KEY
                );
                DELETE FROM received_alert_profile_ids;
                """;
            createCommand.ExecuteNonQuery();
        }

        foreach (var receivedId in receivedIds)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO received_alert_profile_ids(remote_object_id) VALUES ($remoteObjectId);
                """;
            insertCommand.Parameters.AddWithValue("$remoteObjectId", receivedId);
            insertCommand.ExecuteNonQuery();
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE dynatrace_alerting_profiles
            SET is_present = 0
            WHERE tenant_key = $tenantKey
              AND is_present = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM received_alert_profile_ids received
                  WHERE received.remote_object_id = dynatrace_alerting_profiles.remote_object_id
              );
            """;
        updateCommand.Parameters.AddWithValue("$tenantKey", tenantKey);
        updateCommand.ExecuteNonQuery();
    }

    private static void InsertSyncRun(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceAlertingProfileSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        int received,
        int inserted,
        int updated,
        int unchanged,
        int missing,
        string errorMessage)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_alert_profile_sync_runs(
                id, tenant_key, environment, tenant_alias, started_utc, completed_utc,
                status, received, inserted, updated, unchanged, missing,
                is_complete_inventory, error_message)
            VALUES (
                $id, $tenantKey, $environment, $tenantAlias, $startedUtc, $completedUtc,
                $status, $received, $inserted, $updated, $unchanged, $missing,
                $isCompleteInventory, $errorMessage);
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$startedUtc", ToDatabaseValue(startedAt));
        command.Parameters.AddWithValue("$completedUtc", ToDatabaseValue(completedAt));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$received", received);
        command.Parameters.AddWithValue("$inserted", inserted);
        command.Parameters.AddWithValue("$updated", updated);
        command.Parameters.AddWithValue("$unchanged", unchanged);
        command.Parameters.AddWithValue("$missing", missing);
        command.Parameters.AddWithValue("$isCompleteInventory", source.RequestAdminAccess ? 1 : 0);
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, ExistingAnomalyDetector> ReadExistingAnomalyDetectors(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tenantKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT remote_object_id, content_hash, is_present
            FROM dynatrace_anomaly_detectors
            WHERE tenant_key = $tenantKey;
            """;
        command.Parameters.AddWithValue("$tenantKey", tenantKey);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, ExistingAnomalyDetector>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result.Add(
                reader.GetString(0),
                new ExistingAnomalyDetector(reader.GetString(1), reader.GetInt32(2) == 1));
        }

        return result;
    }

    private static void UpsertAnomalyDetector(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceAnomalyDetectorSource source,
        DynatraceAnomalyDetectorSnapshot detector,
        DateTimeOffset synchronizedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_anomaly_detectors(
                tenant_key, environment, tenant_alias, tenant_base_address,
                remote_object_id, schema_id, schema_version, scope,
                title, description, source_name, enabled, analyzer_name, model,
                dql_query, uses_timeseries, event_type, event_name, alert_group, actor,
                analyzer_input_count, event_property_count,
                remote_created_utc, remote_modified_utc, content_hash, raw_json,
                first_seen_utc, last_seen_utc, is_present)
            VALUES (
                $tenantKey, $environment, $tenantAlias, $tenantBaseAddress,
                $remoteObjectId, $schemaId, $schemaVersion, $scope,
                $title, $description, $sourceName, $enabled, $analyzerName, $model,
                $dqlQuery, $usesTimeseries, $eventType, $eventName, $alertGroup, $actor,
                $analyzerInputCount, $eventPropertyCount,
                $remoteCreatedUtc, $remoteModifiedUtc, $contentHash, $rawJson,
                $firstSeenUtc, $lastSeenUtc, 1)
            ON CONFLICT(tenant_key, remote_object_id) DO UPDATE SET
                environment = excluded.environment,
                tenant_alias = excluded.tenant_alias,
                tenant_base_address = excluded.tenant_base_address,
                schema_id = excluded.schema_id,
                schema_version = excluded.schema_version,
                scope = excluded.scope,
                title = excluded.title,
                description = excluded.description,
                source_name = excluded.source_name,
                enabled = excluded.enabled,
                analyzer_name = excluded.analyzer_name,
                model = excluded.model,
                dql_query = excluded.dql_query,
                uses_timeseries = excluded.uses_timeseries,
                event_type = excluded.event_type,
                event_name = excluded.event_name,
                alert_group = excluded.alert_group,
                actor = excluded.actor,
                analyzer_input_count = excluded.analyzer_input_count,
                event_property_count = excluded.event_property_count,
                remote_created_utc = excluded.remote_created_utc,
                remote_modified_utc = excluded.remote_modified_utc,
                content_hash = excluded.content_hash,
                raw_json = excluded.raw_json,
                last_seen_utc = excluded.last_seen_utc,
                is_present = 1;
            """;
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$tenantBaseAddress", source.BaseAddress.AbsoluteUri.TrimEnd('/'));
        command.Parameters.AddWithValue("$remoteObjectId", detector.RemoteObjectId);
        command.Parameters.AddWithValue("$schemaId", detector.SchemaId);
        command.Parameters.AddWithValue("$schemaVersion", detector.SchemaVersion);
        command.Parameters.AddWithValue("$scope", detector.Scope);
        command.Parameters.AddWithValue("$title", detector.Title);
        command.Parameters.AddWithValue("$description", detector.Description);
        command.Parameters.AddWithValue("$sourceName", detector.SourceName);
        command.Parameters.AddWithValue("$enabled", detector.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$analyzerName", detector.AnalyzerName);
        command.Parameters.AddWithValue("$model", detector.Model);
        command.Parameters.AddWithValue("$dqlQuery", detector.Query);
        command.Parameters.AddWithValue("$usesTimeseries", detector.UsesTimeseries ? 1 : 0);
        command.Parameters.AddWithValue("$eventType", detector.EventType);
        command.Parameters.AddWithValue("$eventName", detector.EventName);
        command.Parameters.AddWithValue("$alertGroup", detector.AlertGroup);
        command.Parameters.AddWithValue("$actor", detector.Actor);
        command.Parameters.AddWithValue("$analyzerInputCount", detector.AnalyzerInputCount);
        command.Parameters.AddWithValue("$eventPropertyCount", detector.EventPropertyCount);
        command.Parameters.AddWithValue("$remoteCreatedUtc", ToDatabaseValue(detector.RemoteCreatedAt));
        command.Parameters.AddWithValue("$remoteModifiedUtc", ToDatabaseValue(detector.RemoteModifiedAt));
        command.Parameters.AddWithValue("$contentHash", detector.ContentHash);
        command.Parameters.AddWithValue("$rawJson", detector.RawJson);
        command.Parameters.AddWithValue("$firstSeenUtc", ToDatabaseValue(synchronizedAt));
        command.Parameters.AddWithValue("$lastSeenUtc", ToDatabaseValue(synchronizedAt));
        command.ExecuteNonQuery();
    }

    private static void MarkMissingAnomalyDetectors(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tenantKey,
        IReadOnlySet<string> receivedIds)
    {
        using (var createCommand = connection.CreateCommand())
        {
            createCommand.Transaction = transaction;
            createCommand.CommandText = """
                CREATE TEMP TABLE IF NOT EXISTS received_anomaly_detector_ids (
                    remote_object_id TEXT NOT NULL PRIMARY KEY
                );
                DELETE FROM received_anomaly_detector_ids;
                """;
            createCommand.ExecuteNonQuery();
        }

        foreach (var receivedId in receivedIds)
        {
            using var insertCommand = connection.CreateCommand();
            insertCommand.Transaction = transaction;
            insertCommand.CommandText = """
                INSERT INTO received_anomaly_detector_ids(remote_object_id) VALUES ($remoteObjectId);
                """;
            insertCommand.Parameters.AddWithValue("$remoteObjectId", receivedId);
            insertCommand.ExecuteNonQuery();
        }

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText = """
            UPDATE dynatrace_anomaly_detectors
            SET is_present = 0
            WHERE tenant_key = $tenantKey
              AND is_present = 1
              AND NOT EXISTS (
                  SELECT 1
                  FROM received_anomaly_detector_ids received
                  WHERE received.remote_object_id = dynatrace_anomaly_detectors.remote_object_id
              );
            """;
        updateCommand.Parameters.AddWithValue("$tenantKey", tenantKey);
        updateCommand.ExecuteNonQuery();
    }

    private static void InsertAnomalyDetectorSyncRun(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceAnomalyDetectorSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        int received,
        int inserted,
        int updated,
        int unchanged,
        int missing,
        string errorMessage)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_anomaly_detector_sync_runs(
                id, tenant_key, environment, tenant_alias, started_utc, completed_utc,
                status, received, inserted, updated, unchanged, missing,
                is_complete_inventory, error_message)
            VALUES (
                $id, $tenantKey, $environment, $tenantAlias, $startedUtc, $completedUtc,
                $status, $received, $inserted, $updated, $unchanged, $missing,
                $isCompleteInventory, $errorMessage);
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$startedUtc", ToDatabaseValue(startedAt));
        command.Parameters.AddWithValue("$completedUtc", ToDatabaseValue(completedAt));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$received", received);
        command.Parameters.AddWithValue("$inserted", inserted);
        command.Parameters.AddWithValue("$updated", updated);
        command.Parameters.AddWithValue("$unchanged", unchanged);
        command.Parameters.AddWithValue("$missing", missing);
        command.Parameters.AddWithValue("$isCompleteInventory", source.RequestAdminAccess ? 1 : 0);
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, string> ReadExistingDavisEvents(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tenantKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT event_id, content_hash
            FROM dynatrace_davis_events
            WHERE tenant_key = $tenantKey;
            """;
        command.Parameters.AddWithValue("$tenantKey", tenantKey);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result.Add(reader.GetString(0), reader.GetString(1));
        }

        return result;
    }

    private static void UpsertDavisEvent(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceDavisEventSource source,
        DynatraceDavisEventSnapshot item,
        DateTimeOffset synchronizedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_davis_events(
                tenant_key, environment, tenant_alias, tenant_base_address,
                event_id, event_name, description, category, status, status_transition,
                severity, provider, event_type, source_entity_id, source_entity_type,
                settings_object_id, settings_schema_id, alert_group, dql_query,
                is_frequent, is_merging_allowed, is_under_maintenance,
                event_timestamp_utc, event_start_utc, event_end_utc,
                content_hash, raw_json, first_seen_utc, last_seen_utc)
            VALUES (
                $tenantKey, $environment, $tenantAlias, $tenantBaseAddress,
                $eventId, $eventName, $description, $category, $status, $statusTransition,
                $severity, $provider, $eventType, $sourceEntityId, $sourceEntityType,
                $settingsObjectId, $settingsSchemaId, $alertGroup, $dqlQuery,
                $isFrequent, $isMergingAllowed, $isUnderMaintenance,
                $eventTimestampUtc, $eventStartUtc, $eventEndUtc,
                $contentHash, $rawJson, $firstSeenUtc, $lastSeenUtc)
            ON CONFLICT(tenant_key, event_id) DO UPDATE SET
                environment = excluded.environment,
                tenant_alias = excluded.tenant_alias,
                tenant_base_address = excluded.tenant_base_address,
                event_name = excluded.event_name,
                description = excluded.description,
                category = excluded.category,
                status = excluded.status,
                status_transition = excluded.status_transition,
                severity = excluded.severity,
                provider = excluded.provider,
                event_type = excluded.event_type,
                source_entity_id = excluded.source_entity_id,
                source_entity_type = excluded.source_entity_type,
                settings_object_id = excluded.settings_object_id,
                settings_schema_id = excluded.settings_schema_id,
                alert_group = excluded.alert_group,
                dql_query = excluded.dql_query,
                is_frequent = excluded.is_frequent,
                is_merging_allowed = excluded.is_merging_allowed,
                is_under_maintenance = excluded.is_under_maintenance,
                event_timestamp_utc = excluded.event_timestamp_utc,
                event_start_utc = excluded.event_start_utc,
                event_end_utc = excluded.event_end_utc,
                content_hash = excluded.content_hash,
                raw_json = excluded.raw_json,
                last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$tenantBaseAddress", source.BaseAddress.AbsoluteUri.TrimEnd('/'));
        command.Parameters.AddWithValue("$eventId", item.EventId);
        command.Parameters.AddWithValue("$eventName", item.Name);
        command.Parameters.AddWithValue("$description", item.Description);
        command.Parameters.AddWithValue("$category", item.Category);
        command.Parameters.AddWithValue("$status", item.Status);
        command.Parameters.AddWithValue("$statusTransition", item.StatusTransition);
        command.Parameters.AddWithValue("$severity", item.Severity is null ? DBNull.Value : item.Severity.Value);
        command.Parameters.AddWithValue("$provider", item.Provider);
        command.Parameters.AddWithValue("$eventType", item.EventType);
        command.Parameters.AddWithValue("$sourceEntityId", item.SourceEntityId);
        command.Parameters.AddWithValue("$sourceEntityType", item.SourceEntityType);
        command.Parameters.AddWithValue("$settingsObjectId", item.SettingsObjectId);
        command.Parameters.AddWithValue("$settingsSchemaId", item.SettingsSchemaId);
        command.Parameters.AddWithValue("$alertGroup", item.AlertGroup);
        command.Parameters.AddWithValue("$dqlQuery", item.Query);
        command.Parameters.AddWithValue("$isFrequent", item.IsFrequent ? 1 : 0);
        command.Parameters.AddWithValue("$isMergingAllowed", item.IsMergingAllowed ? 1 : 0);
        command.Parameters.AddWithValue("$isUnderMaintenance", item.IsUnderMaintenance ? 1 : 0);
        command.Parameters.AddWithValue("$eventTimestampUtc", ToDatabaseValue(item.Timestamp));
        command.Parameters.AddWithValue("$eventStartUtc", ToDatabaseValue(item.Start));
        command.Parameters.AddWithValue("$eventEndUtc", ToDatabaseValue(item.End));
        command.Parameters.AddWithValue("$contentHash", item.ContentHash);
        command.Parameters.AddWithValue("$rawJson", item.RawJson);
        command.Parameters.AddWithValue("$firstSeenUtc", ToDatabaseValue(synchronizedAt));
        command.Parameters.AddWithValue("$lastSeenUtc", ToDatabaseValue(synchronizedAt));
        command.ExecuteNonQuery();
    }

    private static void InsertDavisEventSyncRun(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceDavisEventSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        int received,
        int inserted,
        int updated,
        int unchanged,
        bool limitReached,
        string errorMessage)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_davis_event_sync_runs(
                id, tenant_key, environment, tenant_alias, started_utc, completed_utc,
                status, lookback_hours, result_limit, received, inserted, updated,
                unchanged, limit_reached, error_message)
            VALUES (
                $id, $tenantKey, $environment, $tenantAlias, $startedUtc, $completedUtc,
                $status, $lookbackHours, $resultLimit, $received, $inserted, $updated,
                $unchanged, $limitReached, $errorMessage);
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$startedUtc", ToDatabaseValue(startedAt));
        command.Parameters.AddWithValue("$completedUtc", ToDatabaseValue(completedAt));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$lookbackHours", source.LookbackHours);
        command.Parameters.AddWithValue("$resultLimit", source.ResultLimit);
        command.Parameters.AddWithValue("$received", received);
        command.Parameters.AddWithValue("$inserted", inserted);
        command.Parameters.AddWithValue("$updated", updated);
        command.Parameters.AddWithValue("$unchanged", unchanged);
        command.Parameters.AddWithValue("$limitReached", limitReached ? 1 : 0);
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static Dictionary<string, string> ReadExistingProblems(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string tenantKey)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT event_id, content_hash
            FROM dynatrace_problems
            WHERE tenant_key = $tenantKey;
            """;
        command.Parameters.AddWithValue("$tenantKey", tenantKey);
        using var reader = command.ExecuteReader();
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (reader.Read())
        {
            result.Add(reader.GetString(0), reader.GetString(1));
        }

        return result;
    }

    private static void UpsertProblem(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceProblemSource source,
        DynatraceProblemSnapshot problem,
        DateTimeOffset synchronizedAt)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_problems(
                tenant_key, environment, tenant_alias, tenant_base_address,
                event_id, display_id, problem_name, description, category, status,
                severity, affected_users_count, affected_entity_count, correlated_event_count,
                root_cause_entity_id, root_cause_entity_name, root_cause_entity_type,
                affected_entity_ids_json, affected_entity_types_json,
                affected_service_ids_json, correlated_event_ids_json,
                is_root_cause, is_under_maintenance,
                event_timestamp_utc, event_start_utc, event_end_utc,
                content_hash, raw_json, first_seen_utc, last_seen_utc)
            VALUES (
                $tenantKey, $environment, $tenantAlias, $tenantBaseAddress,
                $eventId, $displayId, $problemName, $description, $category, $status,
                $severity, $affectedUsersCount, $affectedEntityCount, $correlatedEventCount,
                $rootCauseEntityId, $rootCauseEntityName, $rootCauseEntityType,
                $affectedEntityIdsJson, $affectedEntityTypesJson,
                $affectedServiceIdsJson, $correlatedEventIdsJson,
                $isRootCause, $isUnderMaintenance,
                $eventTimestampUtc, $eventStartUtc, $eventEndUtc,
                $contentHash, $rawJson, $firstSeenUtc, $lastSeenUtc)
            ON CONFLICT(tenant_key, event_id) DO UPDATE SET
                environment = excluded.environment,
                tenant_alias = excluded.tenant_alias,
                tenant_base_address = excluded.tenant_base_address,
                display_id = excluded.display_id,
                problem_name = excluded.problem_name,
                description = excluded.description,
                category = excluded.category,
                status = excluded.status,
                severity = excluded.severity,
                affected_users_count = excluded.affected_users_count,
                affected_entity_count = excluded.affected_entity_count,
                correlated_event_count = excluded.correlated_event_count,
                root_cause_entity_id = excluded.root_cause_entity_id,
                root_cause_entity_name = excluded.root_cause_entity_name,
                root_cause_entity_type = excluded.root_cause_entity_type,
                affected_entity_ids_json = excluded.affected_entity_ids_json,
                affected_entity_types_json = excluded.affected_entity_types_json,
                affected_service_ids_json = excluded.affected_service_ids_json,
                correlated_event_ids_json = excluded.correlated_event_ids_json,
                is_root_cause = excluded.is_root_cause,
                is_under_maintenance = excluded.is_under_maintenance,
                event_timestamp_utc = excluded.event_timestamp_utc,
                event_start_utc = excluded.event_start_utc,
                event_end_utc = excluded.event_end_utc,
                content_hash = excluded.content_hash,
                raw_json = excluded.raw_json,
                last_seen_utc = excluded.last_seen_utc;
            """;
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$tenantBaseAddress", source.BaseAddress.AbsoluteUri.TrimEnd('/'));
        command.Parameters.AddWithValue("$eventId", problem.EventId);
        command.Parameters.AddWithValue("$displayId", problem.DisplayId);
        command.Parameters.AddWithValue("$problemName", problem.Name);
        command.Parameters.AddWithValue("$description", problem.Description);
        command.Parameters.AddWithValue("$category", problem.Category);
        command.Parameters.AddWithValue("$status", problem.Status);
        command.Parameters.AddWithValue("$severity", problem.Severity is null ? DBNull.Value : problem.Severity.Value);
        command.Parameters.AddWithValue("$affectedUsersCount", problem.AffectedUsersCount);
        command.Parameters.AddWithValue("$affectedEntityCount", problem.AffectedEntityCount);
        command.Parameters.AddWithValue("$correlatedEventCount", problem.CorrelatedEventCount);
        command.Parameters.AddWithValue("$rootCauseEntityId", problem.RootCauseEntityId);
        command.Parameters.AddWithValue("$rootCauseEntityName", problem.RootCauseEntityName);
        command.Parameters.AddWithValue("$rootCauseEntityType", problem.RootCauseEntityType);
        command.Parameters.AddWithValue("$affectedEntityIdsJson", JsonSerializer.Serialize(problem.AffectedEntityIds));
        command.Parameters.AddWithValue("$affectedEntityTypesJson", JsonSerializer.Serialize(problem.AffectedEntityTypes));
        command.Parameters.AddWithValue("$affectedServiceIdsJson", JsonSerializer.Serialize(problem.AffectedServiceIds));
        command.Parameters.AddWithValue("$correlatedEventIdsJson", JsonSerializer.Serialize(problem.CorrelatedEventIds));
        command.Parameters.AddWithValue("$isRootCause", problem.IsRootCause ? 1 : 0);
        command.Parameters.AddWithValue("$isUnderMaintenance", problem.IsUnderMaintenance ? 1 : 0);
        command.Parameters.AddWithValue("$eventTimestampUtc", ToDatabaseValue(problem.Timestamp));
        command.Parameters.AddWithValue("$eventStartUtc", ToDatabaseValue(problem.Start));
        command.Parameters.AddWithValue("$eventEndUtc", ToDatabaseValue(problem.End));
        command.Parameters.AddWithValue("$contentHash", problem.ContentHash);
        command.Parameters.AddWithValue("$rawJson", problem.RawJson);
        command.Parameters.AddWithValue("$firstSeenUtc", ToDatabaseValue(synchronizedAt));
        command.Parameters.AddWithValue("$lastSeenUtc", ToDatabaseValue(synchronizedAt));
        command.ExecuteNonQuery();
    }

    private static void InsertProblemSyncRun(
        SqliteConnection connection,
        SqliteTransaction transaction,
        DynatraceProblemSource source,
        string runId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        string status,
        int received,
        int inserted,
        int updated,
        int unchanged,
        bool limitReached,
        string errorMessage)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dynatrace_problem_sync_runs(
                id, tenant_key, environment, tenant_alias, started_utc, completed_utc,
                status, lookback_hours, result_limit, received, inserted, updated,
                unchanged, limit_reached, error_message)
            VALUES (
                $id, $tenantKey, $environment, $tenantAlias, $startedUtc, $completedUtc,
                $status, $lookbackHours, $resultLimit, $received, $inserted, $updated,
                $unchanged, $limitReached, $errorMessage);
            """;
        command.Parameters.AddWithValue("$id", runId);
        command.Parameters.AddWithValue("$tenantKey", source.TenantKey);
        command.Parameters.AddWithValue("$environment", source.Environment);
        command.Parameters.AddWithValue("$tenantAlias", source.TenantAlias);
        command.Parameters.AddWithValue("$startedUtc", ToDatabaseValue(startedAt));
        command.Parameters.AddWithValue("$completedUtc", ToDatabaseValue(completedAt));
        command.Parameters.AddWithValue("$status", status);
        command.Parameters.AddWithValue("$lookbackHours", source.LookbackHours);
        command.Parameters.AddWithValue("$resultLimit", source.ResultLimit);
        command.Parameters.AddWithValue("$received", received);
        command.Parameters.AddWithValue("$inserted", inserted);
        command.Parameters.AddWithValue("$updated", updated);
        command.Parameters.AddWithValue("$unchanged", unchanged);
        command.Parameters.AddWithValue("$limitReached", limitReached ? 1 : 0);
        command.Parameters.AddWithValue("$errorMessage", errorMessage ?? string.Empty);
        command.ExecuteNonQuery();
    }

    private static IReadOnlyList<string> DeserializeStringList(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<string[]>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static object ToDatabaseValue(DateTimeOffset? value) =>
        value is null
            ? DBNull.Value
            : value.Value.UtcDateTime.ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset ReadRequiredTimestamp(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(
            reader.GetString(ordinal),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ReadOptionalTimestamp(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadRequiredTimestamp(reader, ordinal);

    private sealed record ExistingProfile(string ContentHash, bool IsPresent);

    private sealed record ExistingAnomalyDetector(string ContentHash, bool IsPresent);

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = _options.FilePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = _options.BusyTimeoutSeconds
        }.ToString());
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = $"""
            PRAGMA foreign_keys = ON;
            PRAGMA busy_timeout = {_options.BusyTimeoutSeconds * 1_000};
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static object? ExecuteScalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static long GetStorageSize(string databasePath)
    {
        try
        {
            var paths = new[] { databasePath, databasePath + "-wal", databasePath + "-shm" };
            return paths.Where(File.Exists).Sum(static path => new FileInfo(path).Length);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool IsDatabaseException(Exception exception) =>
        exception is SqliteException
            or IOException
            or UnauthorizedAccessException
            or ArgumentException
            or InvalidOperationException
            or NotSupportedException;
}
