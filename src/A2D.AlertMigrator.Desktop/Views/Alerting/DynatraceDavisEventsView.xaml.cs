using System.Windows.Controls;
using System.Windows.Input;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceDavisEventsView : UserControl
{
    public DynatraceDavisEventsView()
    {
        InitializeComponent();
    }

    private void EventsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DynatraceDavisEventsViewModel viewModel
            || viewModel.SelectedEvent is null
            || !viewModel.OpenDetailsCommand.CanExecute(viewModel.SelectedEvent))
        {
            return;
        }

        viewModel.OpenDetailsCommand.Execute(viewModel.SelectedEvent);
        e.Handled = true;
    }
}
