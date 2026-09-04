using System.Windows;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceAlertingProfileDetailsWindow : Window
{
    public DynatraceAlertingProfileDetailsWindow()
    {
        InitializeComponent();
    }

    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DynatraceAlertingProfileDetailsViewModel details)
        {
            return;
        }

        try
        {
            Clipboard.SetText(details.FormattedJson);
            CopyStatus.Text = "JSON copiado para a área de transferência.";
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException)
        {
            CopyStatus.Text = "A área de transferência está ocupada. Tente novamente.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
