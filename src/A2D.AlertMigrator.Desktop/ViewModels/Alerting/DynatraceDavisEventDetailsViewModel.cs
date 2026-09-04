using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.Common;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceDavisEventDetailsViewModel
{
    public DynatraceDavisEventDetailsViewModel(StoredDynatraceDavisEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        Name = item.Name;
        Description = string.IsNullOrWhiteSpace(item.Description) ? "Sem descrição" : item.Description;
        StatusText = item.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase) ? "Ativo" : "Encerrado";
        IsActive = item.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);
        SeverityText = new DynatraceDavisEventItemViewModel(item).SeverityText;
        Category = EmptyAsDash(item.Category);
        Provider = EmptyAsDash(item.Provider);
        EventType = EmptyAsDash(item.EventType);
        EntityType = EmptyAsDash(item.SourceEntityType);
        EntityId = EmptyAsDash(item.SourceEntityId);
        AlertGroup = EmptyAsDash(item.AlertGroup);
        SettingsObjectId = EmptyAsDash(item.SettingsObjectId);
        SettingsSchemaId = EmptyAsDash(item.SettingsSchemaId);
        EventId = item.EventId;
        StatusTransition = EmptyAsDash(item.StatusTransition);
        StartedText = FormatTimestamp(item.Start ?? item.Timestamp);
        EndedText = item.End is null ? "Ainda ativo" : FormatTimestamp(item.End);
        DurationText = FormatDuration(item.Start ?? item.Timestamp, item.End);
        MaintenanceText = item.IsUnderMaintenance ? "Sim" : "Não";
        FrequentText = item.IsFrequent ? "Sim" : "Não";
        MergingText = item.IsMergingAllowed ? "Permitido" : "Bloqueado";
        Query = string.IsNullOrWhiteSpace(item.Query) ? "// Este evento não possui DQL associado." : item.Query;
        FormattedJson = JsonTextFormatter.Format(item.RawJson);
    }

    public string Name { get; }
    public string Description { get; }
    public string StatusText { get; }
    public bool IsActive { get; }
    public string SeverityText { get; }
    public string Category { get; }
    public string Provider { get; }
    public string EventType { get; }
    public string EntityType { get; }
    public string EntityId { get; }
    public string AlertGroup { get; }
    public string SettingsObjectId { get; }
    public string SettingsSchemaId { get; }
    public string EventId { get; }
    public string StatusTransition { get; }
    public string StartedText { get; }
    public string EndedText { get; }
    public string DurationText { get; }
    public string MaintenanceText { get; }
    public string FrequentText { get; }
    public string MergingText { get; }
    public string Query { get; }
    public string FormattedJson { get; }

    private static string EmptyAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;

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
}
