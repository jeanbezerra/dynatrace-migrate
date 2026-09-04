using System.Windows;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceDavisEventDetailsWindow : Window
{
    public DynatraceDavisEventDetailsWindow()
    {
        InitializeComponent();
    }

    private void CopyDql_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DynatraceDavisEventDetailsViewModel viewModel)
        {
            Clipboard.SetText(viewModel.Query);
            CopyStatus.Text = "DQL copiado.";
        }
    }

    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DynatraceDavisEventDetailsViewModel viewModel)
        {
            Clipboard.SetText(viewModel.FormattedJson);
            CopyStatus.Text = "JSON copiado.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
