using System.Windows;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;

namespace A2D.AlertMigrator.Desktop.Views.Alerting;

public partial class DynatraceAnomalyDetectorDetailsWindow : Window
{
    public DynatraceAnomalyDetectorDetailsWindow()
    {
        InitializeComponent();
    }

    private void CopyDql_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DynatraceAnomalyDetectorDetailsViewModel details)
        {
            Copy(details.Query, "DQL copiada para a área de transferência.");
        }
    }

    private void CopyJson_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is DynatraceAnomalyDetectorDetailsViewModel details)
        {
            Copy(details.FormattedJson, "JSON copiado para a área de transferência.");
        }
    }

    private void Copy(string value, string successMessage)
    {
        try
        {
            Clipboard.SetText(value);
            CopyStatus.Text = successMessage;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            CopyStatus.Text = "A área de transferência está ocupada. Tente novamente.";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
