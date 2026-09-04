namespace A2D.AlertMigrator.Application.Persistence;

public interface ILocalDatabaseService
{
    string CurrentPath { get; }

    string? LastError { get; }

    void Configure(LocalDatabaseOptions options);

    LocalDatabaseInfo GetInfo();

    bool VerifyIntegrity();

    void RecordImport(ImportExecutionRecord record);

    void Export(string destinationPath);
}
