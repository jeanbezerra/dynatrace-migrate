using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.Common;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceAnomalyDetectorDetailsViewModel
{
    public DynatraceAnomalyDetectorDetailsViewModel(StoredDynatraceAnomalyDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        Title = detector.Title;
        Description = string.IsNullOrWhiteSpace(detector.Description) ? "Sem descrição" : detector.Description;
        Model = detector.Model;
        Scope = EmptyAsDash(detector.Scope);
        EventType = EmptyAsDash(detector.EventType);
        EventName = EmptyAsDash(detector.EventName);
        AlertGroup = EmptyAsDash(detector.AlertGroup);
        Actor = EmptyAsDash(detector.Actor);
        StateText = !detector.IsPresent ? "Ausente no tenant" : detector.Enabled ? "Ativo" : "Inativo";
        IsPresentAndEnabled = detector.IsPresent && detector.Enabled;
        QueryStatusText = detector.UsesTimeseries ? "DQL timeseries" : "Fora do padrão timeseries";
        UsesTimeseries = detector.UsesTimeseries;
        RemoteObjectId = detector.RemoteObjectId;
        RemoteModifiedText = detector.RemoteModifiedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Não informado";
        Query = string.IsNullOrWhiteSpace(detector.Query) ? "// Consulta DQL não informada" : detector.Query;
        FormattedJson = JsonTextFormatter.Format(detector.RawJson);
    }

    public string Title { get; }
    public string Description { get; }
    public string Model { get; }
    public string Scope { get; }
    public string EventType { get; }
    public string EventName { get; }
    public string AlertGroup { get; }
    public string Actor { get; }
    public string StateText { get; }
    public bool IsPresentAndEnabled { get; }
    public string QueryStatusText { get; }
    public bool UsesTimeseries { get; }
    public string RemoteObjectId { get; }
    public string RemoteModifiedText { get; }
    public string Query { get; }
    public string FormattedJson { get; }

    private static string EmptyAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
