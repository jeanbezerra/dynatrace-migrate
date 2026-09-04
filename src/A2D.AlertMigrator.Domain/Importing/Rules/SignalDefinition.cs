namespace A2D.AlertMigrator.Domain.Importing;

public abstract record SignalDefinition(string Kind);

public sealed record MetricSignalDefinition(string MetricKey, string Aggregation, string? Rollup)
    : SignalDefinition("METRIC");

public sealed record DqlSignalDefinition(string Expression)
    : SignalDefinition("DQL");
