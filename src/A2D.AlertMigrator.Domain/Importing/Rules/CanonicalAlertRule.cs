namespace A2D.AlertMigrator.Domain.Importing;

public sealed record CanonicalAlertRule(
    string Id,
    string Name,
    string? GroupId,
    bool Enabled,
    string? Description,
    IReadOnlyList<ServiceTarget> Targets,
    SignalDefinition Signal,
    DetectorDefinition Detector,
    EventDefinition Event,
    ProfileDefinition? Profile,
    ScheduleDefinition Schedule);

public sealed record ServiceTarget(string SelectorType, string Value);
