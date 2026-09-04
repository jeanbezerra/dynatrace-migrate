using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;
using static A2D.AlertMigrator.Infrastructure.Importing.Json.JsonImportDiagnosticFactory;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Validation;

internal sealed class JsonImportBatchValidator : IJsonImportBatchValidator
{
    public ImportBatch Validate(
        IReadOnlyList<ImportedApplication> applications,
        IReadOnlyList<ImportDiagnostic> diagnostics,
        ImportLimits limits)
    {
        var checkedApplications = MarkDuplicateApplicationIds(applications);
        var batchDiagnostics = diagnostics.ToList();
        var totalRules = checkedApplications.Sum(static application => application.Document?.Rules.Count ?? 0);
        if (totalRules > limits.MaxRulesTotal)
        {
            batchDiagnostics.Add(Error(
                "JSON_TOTAL_RULE_LIMIT_EXCEEDED",
                $"O lote possui {totalRules} regras normalizadas; o limite é {limits.MaxRulesTotal}."));
        }

        return new ImportBatch(checkedApplications, batchDiagnostics);
    }

    private static IReadOnlyList<ImportedApplication> MarkDuplicateApplicationIds(
        IReadOnlyList<ImportedApplication> applications)
    {
        var duplicateIds = applications
            .Where(static application => application.Document is not null)
            .GroupBy(static application => application.Document!.Application.Id, StringComparer.Ordinal)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        if (duplicateIds.Count == 0)
        {
            return applications;
        }

        return applications.Select(application =>
        {
            var applicationId = application.Document?.Application.Id;
            if (applicationId is null || !duplicateIds.Contains(applicationId))
            {
                return application;
            }

            var applicationDiagnostics = application.Diagnostics.ToList();
            applicationDiagnostics.Add(Error(
                "APPLICATION_ID_DUPLICATE",
                $"Mais de um arquivo declara application.id '{applicationId}'.",
                application.Source.RelativePath,
                applicationId));
            return application with { Diagnostics = applicationDiagnostics };
        }).ToArray();
    }
}
