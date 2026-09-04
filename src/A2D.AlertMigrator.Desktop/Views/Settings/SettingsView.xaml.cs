using System.Windows.Controls;
using A2D.AlertMigrator.Desktop.ViewModels.Settings;

namespace A2D.AlertMigrator.Desktop.Views.Settings;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void DynatraceSecretPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.DynatraceTestSecret = passwordBox.Password;
        }
    }

    private void AppDynamicsSecretPasswordChanged(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.AppDynamicsTestSecret = passwordBox.Password;
        }
    }
}
