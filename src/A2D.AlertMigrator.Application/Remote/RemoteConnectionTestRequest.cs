namespace A2D.AlertMigrator.Application.Remote;

public sealed record RemoteConnectionTestRequest(
    RemotePlatform Platform,
    Uri TestAddress,
    RemoteTestMethod Method,
    RemoteAuthenticationMode AuthenticationMode,
    string? Username,
    string? Secret,
    int ExpectedStatusCode)
{
    public void EnsureValid()
    {
        if (!TestAddress.IsAbsoluteUri || TestAddress.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A URL de teste deve ser absoluta e usar HTTPS.", nameof(TestAddress));
        }

        if (!Enum.IsDefined(Method) || !Enum.IsDefined(AuthenticationMode))
        {
            throw new ArgumentException("O teste remoto contém uma opção inválida.");
        }

        if (ExpectedStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(nameof(ExpectedStatusCode), "O status HTTP esperado deve estar entre 100 e 599.");
        }

        if (AuthenticationMode != RemoteAuthenticationMode.None && string.IsNullOrWhiteSpace(Secret))
        {
            throw new ArgumentException("Informe o token ou segredo para executar o teste autenticado.");
        }

        if (AuthenticationMode == RemoteAuthenticationMode.Basic && string.IsNullOrWhiteSpace(Username))
        {
            throw new ArgumentException("Informe o usuário para autenticação Basic.");
        }
    }
}
