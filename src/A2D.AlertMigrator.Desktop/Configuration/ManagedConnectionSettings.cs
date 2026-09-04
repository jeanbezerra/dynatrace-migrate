using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Desktop.Configuration;

public sealed record ManagedConnectionSettings(
    ManagedEnvironment Environment,
    string Alias,
    string TenantIdentifier,
    string BaseAddress,
    string TestAddress,
    RemoteAuthenticationMode AuthenticationMode,
    string Username,
    string Key,
    int ExpectedStatusCode,
    string Details,
    bool Enabled = true)
{
    public static ManagedConnectionSettings CreateDefault(
        RemotePlatform platform,
        ManagedEnvironment environment)
    {
        var alias = environment switch
        {
            ManagedEnvironment.Dev => "Desenvolvimento",
            ManagedEnvironment.Hml => "Homologação",
            ManagedEnvironment.Prd => "Produção",
            _ => environment.ToString()
        };

        return new ManagedConnectionSettings(
            environment,
            alias,
            string.Empty,
            string.Empty,
            string.Empty,
            RemoteAuthenticationMode.BearerToken,
            string.Empty,
            string.Empty,
            200,
            string.Empty,
            Enabled: true);
    }

    public ManagedConnectionSettings Normalize(RemotePlatform platform)
    {
        if (!Enum.IsDefined(Environment) || !Enum.IsDefined(AuthenticationMode))
        {
            throw new ArgumentException("A conexão possui uma opção inválida.");
        }

        if (platform == RemotePlatform.Dynatrace && AuthenticationMode == RemoteAuthenticationMode.Basic)
        {
            throw new ArgumentException("Autenticação Basic não é aceita para ambientes Dynatrace.");
        }

        if (platform == RemotePlatform.AppDynamics
            && AuthenticationMode == RemoteAuthenticationMode.DynatraceApiToken)
        {
            throw new ArgumentException("API Token do Dynatrace não é aceito em ambientes AppDynamics.");
        }

        var alias = NormalizeText(Alias, 80, "apelido");
        var tenantIdentifier = NormalizeText(TenantIdentifier, 240, "identificador do tenant");
        var baseAddress = NormalizeAddress(BaseAddress, "URL-base");
        if (platform == RemotePlatform.Dynatrace
            && baseAddress.Length == 0
            && tenantIdentifier.Length > 0)
        {
            if (tenantIdentifier.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-'))
            {
                throw new ArgumentException("O ID do ambiente Dynatrace deve conter apenas letras, números ou hífen.");
            }

            baseAddress = $"https://{tenantIdentifier}.live.dynatrace.com";
        }
        var testAddress = NormalizeAddress(TestAddress, "URL de teste");
        var username = NormalizeText(Username, 320, "usuário");
        var key = (Key ?? string.Empty).Trim();
        var details = NormalizeText(Details, 4_000, "detalhes");

        if (key.Length > 16_384)
        {
            throw new ArgumentException("A chave deve possuir no máximo 16.384 caracteres.");
        }

        if (ExpectedStatusCode is < 100 or > 599)
        {
            throw new ArgumentOutOfRangeException(
                nameof(ExpectedStatusCode),
                "O status HTTP esperado deve estar entre 100 e 599.");
        }

        return this with
        {
            Alias = alias,
            TenantIdentifier = tenantIdentifier,
            BaseAddress = baseAddress,
            TestAddress = testAddress,
            Username = username,
            Key = key,
            Details = details
        };
    }

    private static string NormalizeText(string? value, int maximumLength, string field)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentException($"O campo {field} deve possuir no máximo {maximumLength} caracteres.");
        }

        return normalized;
    }

    private static string NormalizeAddress(string? value, string field)
    {
        var normalized = (value ?? string.Empty).Trim().TrimEnd('/');
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        if (normalized.Length > 2_048
            || !Uri.TryCreate(normalized, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException($"A {field} deve ser uma URL HTTPS absoluta.");
        }

        return address.AbsoluteUri.TrimEnd('/');
    }
}
