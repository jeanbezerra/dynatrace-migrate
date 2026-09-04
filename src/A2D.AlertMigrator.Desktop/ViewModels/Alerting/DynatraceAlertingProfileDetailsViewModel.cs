using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.Common;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceAlertingProfileDetailsViewModel
{
    public DynatraceAlertingProfileDetailsViewModel(StoredDynatraceAlertingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Name = profile.Name;
        Scope = string.IsNullOrWhiteSpace(profile.Scope) ? "—" : profile.Scope;
        ManagementZone = string.IsNullOrWhiteSpace(profile.ManagementZone) ? "Todas" : profile.ManagementZone;
        SeverityRuleCount = profile.SeverityRuleCount.ToString("N0");
        EventFilterCount = profile.EventFilterCount.ToString("N0");
        RemoteObjectId = profile.RemoteObjectId;
        Schema = $"{profile.SchemaId} · {profile.SchemaVersion}";
        RemoteModifiedText = profile.RemoteModifiedAt?.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss") ?? "Não informado";
        LastSeenText = profile.LastSeenAt.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        PresenceText = profile.IsPresent ? "Presente no tenant" : "Ausente no tenant";
        IsPresent = profile.IsPresent;
        FormattedJson = JsonTextFormatter.Format(profile.RawJson);
    }

    public string Name { get; }

    public string Scope { get; }

    public string ManagementZone { get; }

    public string SeverityRuleCount { get; }

    public string EventFilterCount { get; }

    public string RemoteObjectId { get; }

    public string Schema { get; }

    public string RemoteModifiedText { get; }

    public string LastSeenText { get; }

    public string PresenceText { get; }

    public bool IsPresent { get; }

    public string FormattedJson { get; }

}
