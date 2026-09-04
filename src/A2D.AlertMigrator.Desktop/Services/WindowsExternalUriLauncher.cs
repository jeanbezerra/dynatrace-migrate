using System.Diagnostics;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class WindowsExternalUriLauncher : IExternalUriLauncher
{
    public void Open(Uri address)
    {
        ArgumentNullException.ThrowIfNull(address);

        if (!address.IsAbsoluteUri || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("Somente endereços HTTPS podem ser abertos.", nameof(address));
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = address.AbsoluteUri,
            UseShellExecute = true
        });
    }
}
