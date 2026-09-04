using System.Windows.Controls;
using System.Windows.Input;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceAnomalyDetectorsView : UserControl
{
    public DynatraceAnomalyDetectorsView()
    {
        InitializeComponent();
    }

    private void DetectorsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DynatraceAnomalyDetectorsViewModel viewModel
            || viewModel.SelectedDetector is null
            || !viewModel.OpenDetailsCommand.CanExecute(viewModel.SelectedDetector))
        {
            return;
        }

        viewModel.OpenDetailsCommand.Execute(viewModel.SelectedDetector);
        e.Handled = true;
    }
}
