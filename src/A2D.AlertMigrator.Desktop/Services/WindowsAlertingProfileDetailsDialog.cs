using System.Windows;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;
using A2D.AlertMigrator.Desktop.Views.Alerting;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsAlertingProfileDetailsDialog : IAlertingProfileDetailsDialog
{
    public void Show(StoredDynatraceAlertingProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var window = new DynatraceAlertingProfileDetailsWindow
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = new DynatraceAlertingProfileDetailsViewModel(profile)
        };
        window.ShowDialog();
    }
}
