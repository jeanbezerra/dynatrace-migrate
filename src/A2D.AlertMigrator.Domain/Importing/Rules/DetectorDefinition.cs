namespace A2D.AlertMigrator.Domain.Importing;

public sealed record DetectorDefinition(
    string Model,
    string Condition,
    decimal? Threshold,
    decimal? NumberOfSignalFluctuations,
    decimal? Tolerance,
    int ViolatingSamples,
    int SlidingWindow,
    int DealertingSamples,
    bool AlertOnMissingData);
