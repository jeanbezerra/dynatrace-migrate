using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;
using A2D.AlertMigrator.Desktop.Views.Alerting;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsProblemDetailsDialog : IProblemDetailsDialog
{
    public void Show(StoredDynatraceProblem problem)
    {
        ArgumentNullException.ThrowIfNull(problem);
        var window = new DynatraceProblemDetailsWindow
        {
            Owner = System.Windows.Application.Current.MainWindow,
            DataContext = new DynatraceProblemDetailsViewModel(problem)
        };
        window.ShowDialog();
    }
}
