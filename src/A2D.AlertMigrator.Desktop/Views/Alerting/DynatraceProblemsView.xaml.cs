using System.Windows.Controls;
using System.Windows.Input;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceProblemsView : UserControl
{
    public DynatraceProblemsView()
    {
        InitializeComponent();
    }

    private void ProblemsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DynatraceProblemsViewModel viewModel
            || viewModel.SelectedProblem is null
            || !viewModel.OpenDetailsCommand.CanExecute(viewModel.SelectedProblem))
        {
            return;
        }

        viewModel.OpenDetailsCommand.Execute(viewModel.SelectedProblem);
        e.Handled = true;
    }
}
