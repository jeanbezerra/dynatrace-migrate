using System.Windows;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceProblemDetailsWindow : Window
{
    public DynatraceProblemDetailsWindow()
    {
        InitializeComponent();
    }

    private void CopyEvents_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DynatraceProblemDetailsViewModel viewModel)
        {
            Clipboard.SetText(viewModel.CorrelatedEventsText);
            CopyStatus.Text = "IDs copiados.";
        }
    }

    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DynatraceProblemDetailsViewModel viewModel)
        {
            Clipboard.SetText(viewModel.FormattedJson);
            CopyStatus.Text = "JSON copiado.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
