using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Services;

namespace A2D.AlertMigrator.Desktop.ViewModels.Help;

public sealed class DynatraceHelpViewModel
{
    private static readonly Uri PlatformTokensAddress =
        new("https://docs.dynatrace.com/docs/manage/identity-access-management/access-tokens-and-oauth-clients/platform-tokens");

    private static readonly Uri AlertAutomationAddress =
        new("https://docs.dynatrace.com/docs/dynatrace-intelligence/anomaly-detection/set-up-anomaly-detectors-via-api");

    private static readonly Uri GrailPermissionsAddress =
        new("https://docs.dynatrace.com/docs/platform/grail/organize-data/assign-permissions-in-grail");

    public DynatraceHelpViewModel(IExternalUriLauncher uriLauncher)
    {
        ArgumentNullException.ThrowIfNull(uriLauncher);
        OpenPlatformTokensCommand = new RelayCommand(() => uriLauncher.Open(PlatformTokensAddress));
        OpenAlertAutomationCommand = new RelayCommand(() => uriLauncher.Open(AlertAutomationAddress));
        OpenGrailPermissionsCommand = new RelayCommand(() => uriLauncher.Open(GrailPermissionsAddress));
    }

    public RelayCommand OpenPlatformTokensCommand { get; }

    public RelayCommand OpenAlertAutomationCommand { get; }

    public RelayCommand OpenGrailPermissionsCommand { get; }
}
