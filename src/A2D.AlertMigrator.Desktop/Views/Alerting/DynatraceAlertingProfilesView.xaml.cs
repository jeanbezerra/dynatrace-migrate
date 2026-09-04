using System.Windows.Controls;
using System.Windows.Input;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceAlertingProfilesView : UserControl
{
    public DynatraceAlertingProfilesView()
    {
        InitializeComponent();
    }

    private void ProfilesGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not DynatraceAlertingProfilesViewModel viewModel
            || viewModel.SelectedProfile is null
            || !viewModel.OpenDetailsCommand.CanExecute(viewModel.SelectedProfile))
        {
            return;
        }

        viewModel.OpenDetailsCommand.Execute(viewModel.SelectedProfile);
        e.Handled = true;
    }
}
