using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record RemoteEndpointTestSettings(
    string TestAddress,
    RemoteTestMethod Method,
    RemoteAuthenticationMode AuthenticationMode,
    string Username,
    int ExpectedStatusCode)
{
    public RemoteEndpointTestSettings Normalize(string platformLabel)
    {
        var address = TestAddress.Trim();
        if (address.Length > 0
            && (!Uri.TryCreate(address, UriKind.Absolute, out var uri)
                || uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException($"A URL de teste do {platformLabel} deve ser absoluta e usar HTTPS.");
        }

        if (!Enum.IsDefined(Method) || !Enum.IsDefined(AuthenticationMode))
        {
            throw new ArgumentException($"O teste do {platformLabel} contém uma opção inválida.");
        }

        if (ExpectedStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(ExpectedStatusCode), $"O status esperado do {platformLabel} deve estar entre 100 e 599.");
        }

        return this with { TestAddress = address, Username = Username.Trim() };
    }
}
