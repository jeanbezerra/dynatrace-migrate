using A2D.AlertMigrator.Application.Alerting;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceAlertingProfileItemViewModel
{
    public DynatraceAlertingProfileItemViewModel(StoredDynatraceAlertingProfile profile)
    {
        Model = profile ?? throw new ArgumentNullException(nameof(profile));
    }

    public StoredDynatraceAlertingProfile Model { get; }

    public string Name => Model.Name;

    public string Scope => string.IsNullOrWhiteSpace(Model.Scope) ? "—" : Model.Scope;

    public string ManagementZone => string.IsNullOrWhiteSpace(Model.ManagementZone) ? "Todas" : Model.ManagementZone;

    public int SeverityRuleCount => Model.SeverityRuleCount;

    public int EventFilterCount => Model.EventFilterCount;

    public string RemoteObjectId => Model.RemoteObjectId;

    public string SchemaVersion => string.IsNullOrWhiteSpace(Model.SchemaVersion) ? "—" : Model.SchemaVersion;

    public string RemoteModifiedText => Model.RemoteModifiedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Não informado";

    public string LastSeenText => Model.LastSeenAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

    public string PresenceText => Model.IsPresent ? "Ativo" : "Ausente";

    public bool IsPresent => Model.IsPresent;

    public string RawJson => Model.RawJson;

    public bool Matches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Scope.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || ManagementZone.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || RemoteObjectId.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }
}
