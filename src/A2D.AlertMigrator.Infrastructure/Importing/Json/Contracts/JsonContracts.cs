using System.Text.Json.Serialization;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Contracts;

internal sealed class ApplicationFileDto
{
    [JsonPropertyName("$schema")]
    public string? Schema { get; init; }

    public string? SchemaVersion { get; init; }

    public ApplicationDto? Application { get; init; }

    public DefaultsDto? Defaults { get; init; }

    public List<RuleDto>? Rules { get; init; }
}

internal sealed class ApplicationDto
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? Description { get; init; }

    public List<string>? Owners { get; init; }

    public Dictionary<string, string>? Labels { get; init; }
}

internal sealed class DefaultsDto
{
    public bool? Enabled { get; init; }

    public string? EventType { get; init; }

    public string? AlertGroup { get; init; }

    public ProfileDto? Profile { get; init; }

    public ScheduleDto? Schedule { get; init; }
}

internal sealed class RuleDto
{
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? GroupId { get; init; }

    public bool? Enabled { get; init; }

    public string? Description { get; init; }

    public List<TargetDto>? Targets { get; init; }

    public SignalDto? Signal { get; init; }

    public DetectorDto? Detector { get; init; }

    public EventDto? Event { get; init; }

    public ProfileDto? Profile { get; init; }

    public ScheduleDto? Schedule { get; init; }
}

internal sealed class TargetDto
{
    public string? SelectorType { get; init; }

    public string? Value { get; init; }
}

internal sealed class SignalDto
{
    public string? Kind { get; init; }

    public string? MetricKey { get; init; }

    public string? Aggregation { get; init; }

    public string? Rollup { get; init; }

    public string? Expression { get; init; }
}

internal sealed class DetectorDto
{
    public string? Model { get; init; }

    public string? Condition { get; init; }

    public decimal? Threshold { get; init; }

    public decimal? NumberOfSignalFluctuations { get; init; }

    public decimal? Tolerance { get; init; }

    public int? ViolatingSamples { get; init; }

    public int? SlidingWindow { get; init; }

    public int? DealertingSamples { get; init; }

    public bool? AlertOnMissingData { get; init; }
}

internal sealed class EventDto
{
    public string? Name { get; init; }

    public string? Description { get; init; }

    public string? Type { get; init; }

    public string? AlertGroup { get; init; }
}

internal sealed class ProfileDto
{
    public string? Name { get; init; }

    public string? Severity { get; init; }

    public int? DelayMinutes { get; init; }

    public List<string>? TagFilters { get; init; }
}

internal sealed class ScheduleDto
{
    public string? Mode { get; init; }

    public string? Timezone { get; init; }

    public List<ScheduleWindowDto>? Windows { get; init; }
}

internal sealed class ScheduleWindowDto
{
    public List<string>? Days { get; init; }

    public string? Start { get; init; }

    public string? End { get; init; }
}
