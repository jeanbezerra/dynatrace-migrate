namespace A2D.AlertMigrator.Domain.Importing;

public sealed record ScheduleDefinition(
    string Mode,
    string? Timezone,
    IReadOnlyList<ScheduleWindow> Windows);

public sealed record ScheduleWindow(
    IReadOnlyList<string> Days,
    TimeOnly Start,
    TimeOnly End);
