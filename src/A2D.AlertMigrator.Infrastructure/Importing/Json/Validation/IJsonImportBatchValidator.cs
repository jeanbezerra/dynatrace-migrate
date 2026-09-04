using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Validation;

internal interface IJsonImportBatchValidator
{
    ImportBatch Validate(
        IReadOnlyList<ImportedApplication> applications,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        ImportLimits limits);
}
