using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;
using A2D.AlertMigrator.Desktop.Views.Alerting;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsAnomalyDetectorDetailsDialog : IAnomalyDetectorDetailsDialog
{
    public void Show(StoredDynatraceAnomalyDetector detector)
    {
        ArgumentNullException.ThrowIfNull(detector);
        var window = new DynatraceAnomalyDetectorDetailsWindow
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = new DynatraceAnomalyDetectorDetailsViewModel(detector)
        };
        window.ShowDialog();
    }
}
