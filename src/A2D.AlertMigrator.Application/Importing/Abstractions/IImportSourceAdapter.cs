using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Application.Importing;

public interface IImportSourceAdapter<in TSource>
{
    Task<ImportBatch> ReadAsync(TSource source, CancellationToken cancellationToken = default);
}
