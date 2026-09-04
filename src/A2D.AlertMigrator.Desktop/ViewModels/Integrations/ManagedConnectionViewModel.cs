using A2D.AlertMigrator.Application.Remote;
using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Configuration;
using A2D.AlertMigrator.Desktop.ViewModels.Settings;

namespace A2D.AlertMigrator.Desktop.ViewModels.Integrations;

public sealed class ManagedConnectionViewModel : ObservableObject
{
    private string _alias;
    private string _tenantIdentifier;
    private string _baseAddress;
    private string _testAddress;
    private RemoteAuthenticationMode _authenticationMode;
    private string _username;
    private string _key;
    private int _expectedStatusCode;
    private string _details;
    private bool _enabled;
    private RemoteTestPresentation _testResult;

    public ManagedConnectionViewModel(ManagedConnectionSettings settings, string platformLabel)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Environment = settings.Environment;
        _alias = settings.Alias;
        _tenantIdentifier = settings.TenantIdentifier;
        _baseAddress = settings.BaseAddress;
        _testAddress = settings.TestAddress;
        _authenticationMode = settings.AuthenticationMode;
        _username = settings.Username;
        _key = settings.Key;
        _expectedStatusCode = settings.ExpectedStatusCode;
        _details = settings.Details;
        _enabled = settings.Enabled;
        _testResult = RemoteTestPresentation.Idle(platformLabel);
    }

    public ManagedEnvironment Environment { get; }

    public string EnvironmentLabel => Environment.ToString().ToUpperInvariant();

    public string Alias
    {
        get => _alias;
        set
        {
            if (SetProperty(ref _alias, value))
            {
                OnPropertyChanged(nameof(DisplayName));
            }
        }
    }

    public string DisplayName => string.IsNullOrWhiteSpace(Alias) ? EnvironmentLabel : Alias;

    public string TenantIdentifier
    {
        get => _tenantIdentifier;
        set
        {
            if (SetProperty(ref _tenantIdentifier, value))
            {
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        }
    }

    public string BaseAddress
    {
        get => _baseAddress;
        set
        {
            if (SetProperty(ref _baseAddress, value))
            {
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        }
    }

    public string TestAddress
    {
        get => _testAddress;
        set => SetProperty(ref _testAddress, value);
    }

    public RemoteAuthenticationMode AuthenticationMode
    {
        get => _authenticationMode;
        set => SetProperty(ref _authenticationMode, value);
    }

    public string Username
    {
        get => _username;
        set => SetProperty(ref _username, value);
    }

    public string Key
    {
        get => _key;
        set
        {
            if (SetProperty(ref _key, value))
            {
                OnPropertyChanged(nameof(KeyStatus));
            }
        }
    }

    public string KeyStatus => string.IsNullOrWhiteSpace(Key)
        ? "Nenhuma chave armazenada"
        : "Chave armazenada no settings.json";

    public int ExpectedStatusCode
    {
        get => _expectedStatusCode;
        set => SetProperty(ref _expectedStatusCode, value);
    }

    public string Details
    {
        get => _details;
        set => SetProperty(ref _details, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (SetProperty(ref _enabled, value))
            {
                OnPropertyChanged(nameof(ConnectionSummary));
            }
        }
    }

    public string ConnectionSummary
    {
        get
        {
            if (!Enabled)
            {
                return "Ambiente desativado";
            }

            if (!string.IsNullOrWhiteSpace(TenantIdentifier))
            {
                return TenantIdentifier;
            }

            return string.IsNullOrWhiteSpace(BaseAddress) ? "Não configurado" : BaseAddress;
        }
    }

    public RemoteTestPresentation TestResult
    {
        get => _testResult;
        set => SetProperty(ref _testResult, value);
    }

    public ManagedConnectionSettings ToSettings() => new(
        Environment,
        Alias,
        TenantIdentifier,
        BaseAddress,
        TestAddress,
        AuthenticationMode,
        Username,
        Key,
        ExpectedStatusCode,
        Details,
        Enabled);
}
