using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.Common;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceProblemDetailsViewModel
{
    public DynatraceProblemDetailsViewModel(StoredDynatraceProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        Name = problem.Name;
        DisplayId = problem.DisplayId;
        EventId = problem.EventId;
        Description = string.IsNullOrWhiteSpace(problem.Description) ? "Sem descrição" : problem.Description;
        IsActive = problem.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);
        StatusText = IsActive ? "Ativo" : "Encerrado";
        Category = EmptyAsDash(problem.Category);
        SeverityText = FormatSeverity(problem.Severity);
        StartedText = FormatTimestamp(problem.Start ?? problem.Timestamp);
        EndedText = problem.End is null ? "Ainda ativo" : FormatTimestamp(problem.End);
        DurationText = FormatDuration(problem.Start ?? problem.Timestamp, problem.End);
        AffectedUsersText = problem.AffectedUsersCount.ToString("N0");
        AffectedEntityCountText = problem.AffectedEntityCount.ToString("N0");
        CorrelatedEventCountText = problem.CorrelatedEventCount.ToString("N0");
        RootCauseName = EmptyAsDash(problem.RootCauseEntityName);
        RootCauseId = EmptyAsDash(problem.RootCauseEntityId);
        RootCauseType = EmptyAsDash(problem.RootCauseEntityType);
        MaintenanceText = problem.IsUnderMaintenance ? "Sim" : "Não";
        AffectedEntitiesText = FormatEntities(problem.AffectedEntityIds, problem.AffectedEntityTypes);
        AffectedServicesText = JoinLines(problem.AffectedServiceIds, "Nenhum serviço informado.");
        CorrelatedEventsText = JoinLines(problem.CorrelatedEventIds, "Nenhum evento informado.");
        FormattedJson = JsonTextFormatter.Format(problem.RawJson);
    }

    public string Name { get; }
    public string DisplayId { get; }
    public string EventId { get; }
    public string Description { get; }
    public bool IsActive { get; }
    public string StatusText { get; }
    public string Category { get; }
    public string SeverityText { get; }
    public string StartedText { get; }
    public string EndedText { get; }
    public string DurationText { get; }
    public string AffectedUsersText { get; }
    public string AffectedEntityCountText { get; }
    public string CorrelatedEventCountText { get; }
    public string RootCauseName { get; }
    public string RootCauseId { get; }
    public string RootCauseType { get; }
    public string MaintenanceText { get; }
    public string AffectedEntitiesText { get; }
    public string AffectedServicesText { get; }
    public string CorrelatedEventsText { get; }
    public string FormattedJson { get; }

    private static string EmptyAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

    private static string FormatSeverity(int? severity) => severity switch
    {
        1 => "1 · Crítica",
        2 => "2 · Alta",
        3 => "3 · Média",
        4 => "4 · Baixa",
        5 => "5 · Informativa",
        int value => value.ToString(),
        _ => "Não informada"
    };

    private static string FormatTimestamp(DateTimeOffset? value) =>
        value?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Não informado";

    private static string FormatDuration(DateTimeOffset? start, DateTimeOffset? end)
    {
        if (start is null)
        {
            return "Não informada";
        }

        var duration = (end ?? DateTimeOffset.UtcNow) - start.Value;
        if (duration < TimeSpan.Zero)
        {
            return "Não informada";
        }

        return duration.TotalDays >= 1
            ? $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}min"
            : duration.TotalHours >= 1
                ? $"{(int)duration.TotalHours}h {duration.Minutes}min"
                : $"{Math.Max(0, (int)duration.TotalMinutes)}min";
    }

    private static string JoinLines(IReadOnlyList<string> values, string fallback) =>
        values.Count == 0 ? fallback : string.Join(Environment.NewLine, values);

    private static string FormatEntities(IReadOnlyList<string> ids, IReadOnlyList<string> types)
    {
        if (ids.Count == 0)
        {
            return "Nenhuma entidade informada.";
        }

        return string.Join(
            Environment.NewLine,
            ids.Select((id, index) => index < types.Count && !string.IsNullOrWhiteSpace(types[index])
                ? $"{types[index]} · {id}"
                : id));
    }
}
