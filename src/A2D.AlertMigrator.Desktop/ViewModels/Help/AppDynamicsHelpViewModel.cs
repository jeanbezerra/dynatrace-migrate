using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Services;

namespace A2D.AlertMigrator.Desktop.ViewModels.Help;

public sealed class AppDynamicsHelpViewModel
{
    private static readonly Uri ApiClientsAddress =
        new("https://help.splunk.com/en/appdynamics-saas/extend-splunk-appdynamics/26.4.0/extend-splunk-appdynamics/splunk-appdynamics-apis/api-clients");

    private static readonly Uri LeastPrivilegeAddress =
        new("https://help.splunk.com/en/appdynamics-saas/mcp-server-integration/26.7.0/configure-a-least-privileged-api-client");

    private static readonly Uri HealthRuleApiAddress =
        new("https://help.splunk.com/en/appdynamics-saas/extend-splunk-appdynamics/26.3.0/extend-splunk-appdynamics/splunk-appdynamics-apis/alert-and-respond-api/health-rule-api");

    public AppDynamicsHelpViewModel(IExternalUriLauncher uriLauncher)
    {
        ArgumentNullException.ThrowIfNull(uriLauncher);
        OpenApiClientsCommand = new RelayCommand(() => uriLauncher.Open(ApiClientsAddress));
        OpenLeastPrivilegeCommand = new RelayCommand(() => uriLauncher.Open(LeastPrivilegeAddress));
        OpenHealthRuleApiCommand = new RelayCommand(() => uriLauncher.Open(HealthRuleApiAddress));
    }

    public RelayCommand OpenApiClientsCommand { get; }

    public RelayCommand OpenLeastPrivilegeCommand { get; }

    public RelayCommand OpenHealthRuleApiCommand { get; }
}
