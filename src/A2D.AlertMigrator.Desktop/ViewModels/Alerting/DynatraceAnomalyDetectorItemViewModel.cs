using A2D.AlertMigrator.Application.Alerting;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceAnomalyDetectorItemViewModel
{
    public DynatraceAnomalyDetectorItemViewModel(StoredDynatraceAnomalyDetector detector)
    {
        Model = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    public StoredDynatraceAnomalyDetector Model { get; }

    public string Title => Model.Title;

    public string ModelName => Model.Model;

    public string EventType => EmptyAsDash(Model.EventType);

    public string AlertGroup => EmptyAsDash(Model.AlertGroup);

    public string Scope => EmptyAsDash(Model.Scope);

    public bool UsesTimeseries => Model.UsesTimeseries;

    public string QueryStatusText => Model.UsesTimeseries ? "timeseries" : "Fora do padrão";

    public bool IsPresent => Model.IsPresent;

    public bool IsEnabled => Model.Enabled;

    public string StateText => !Model.IsPresent ? "Ausente" : Model.Enabled ? "Ativo" : "Inativo";

    public string RemoteModifiedText => Model.RemoteModifiedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Não informado";

    public bool Matches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return Title.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || ModelName.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || EventType.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || AlertGroup.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Model.Query.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Model.RemoteObjectId.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static string EmptyAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
