using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Application.Importing;

public sealed class ImportJsonFolderUseCase
{
    private readonly IImportSourceAdapter<JsonFolderImportOptions> _sourceAdapter;

    public ImportJsonFolderUseCase(IImportSourceAdapter<JsonFolderImportOptions> sourceAdapter)
    {
        _sourceAdapter = sourceAdapter ?? throw new ArgumentNullException(nameof(sourceAdapter));
    }

    public Task<ImportBatch> ExecuteAsync(
        JsonFolderImportOptions source,
        CancellationToken cancellationToken = default) =>
        _sourceAdapter.ReadAsync(source, cancellationToken);
}
