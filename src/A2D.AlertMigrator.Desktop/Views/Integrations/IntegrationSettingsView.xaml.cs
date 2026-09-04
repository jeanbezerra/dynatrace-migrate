using System.Windows;
using System.Windows.Controls;
using A2D.AlertMigrator.Desktop.ViewModels.Integrations;

namespace A2D.AlertMigrator.Desktop.Views.Integrations;

public partial class IntegrationSettingsView : UserControl
{
    private bool _synchronizingKey;

    public IntegrationSettingsView()
    {
        InitializeComponent();
    }

    private void ViewLoaded(object sender, RoutedEventArgs e) => SynchronizeKeyField();

    private void ViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Dispatcher.BeginInvoke(SynchronizeKeyField);

    private void EnvironmentSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        Dispatcher.BeginInvoke(SynchronizeKeyField);

    private void IntegrationKeyPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (_synchronizingKey
            || DataContext is not IntegrationSettingsViewModel viewModel
            || viewModel.SelectedEnvironment is null
            || sender is not PasswordBox passwordBox)
        {
            return;
        }

        viewModel.SelectedEnvironment.Key = passwordBox.Password;
    }

    private void SynchronizeKeyField()
    {
        if (!IsLoaded || IntegrationKeyBox is null)
        {
            return;
        }

        _synchronizingKey = true;
        try
        {
            IntegrationKeyBox.Password = DataContext is IntegrationSettingsViewModel viewModel
                ? viewModel.SelectedEnvironment?.Key ?? string.Empty
                : string.Empty;
        }
        finally
        {
            _synchronizingKey = false;
        }
    }
}
