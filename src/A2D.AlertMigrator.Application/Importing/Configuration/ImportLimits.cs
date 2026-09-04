namespace A2D.AlertMigrator.Application.Importing;

public sealed record ImportLimits(
    long MaxFileBytes = 10 * 1024 * 1024,
    int MaxFiles = 1_000,
    int MaxRulesPerApplication = 5_000,
    int MaxRulesTotal = 50_000,
    int MaxJsonDepth = 64,
    int MaxDqlCharacters = 65_536)
{
    public void EnsureValid()
    {
        if (MaxFileBytes <= 0
            || MaxFiles <= 0
            || MaxRulesPerApplication <= 0
            || MaxRulesTotal <= 0
            || MaxJsonDepth is < 1 or > 256
            || MaxDqlCharacters <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ImportLimits), "Todos os limites devem ser positivos e a profundidade deve estar entre 1 e 256.");
        }
    }
}
