namespace A2D.AlertMigrator.Application.Persistence;

public sealed record LocalDatabaseInfo(
    string FilePath,
    bool Exists,
    long SizeBytes,
    int SchemaVersion,
    string JournalMode,
    long HistoryRecordCount,
    string? LastError);
