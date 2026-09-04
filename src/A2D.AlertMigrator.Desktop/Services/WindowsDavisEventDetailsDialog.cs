using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;
using A2D.AlertMigrator.Desktop.Views.Alerting;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsDavisEventDetailsDialog : IDavisEventDetailsDialog
{
    public void Show(StoredDynatraceDavisEvent item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var window = new DynatraceDavisEventDetailsWindow
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = new DynatraceDavisEventDetailsViewModel(item)
        };
        window.ShowDialog();
    }
}
