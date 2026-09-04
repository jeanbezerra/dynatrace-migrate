using System.Collections.ObjectModel;
using System.IO;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Application.Remote;
using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Configuration;
using A2D.AlertMigrator.Desktop.Services;
using A2D.AlertMigrator.Desktop.ViewModels.Settings;

namespace A2D.AlertMigrator.Desktop.ViewModels.Integrations;

public sealed class IntegrationSettingsViewModel : ObservableObject
{
    private readonly IUserSettingsService _settingsService;
    private readonly IApplicationLogger _logger;
    private readonly IRemoteHttpClientFactory _remoteHttpClientFactory;
    private ManagedConnectionViewModel? _selectedEnvironment;
    private bool _isBusy;
    private bool _hasError;
    private string _statusMessage;

    public IntegrationSettingsViewModel(
        RemotePlatform platform,
        IUserSettingsService settingsService,
        IApplicationLogger logger,
        IRemoteHttpClientFactory remoteHttpClientFactory)
    {
        Platform = platform;
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _remoteHttpClientFactory = remoteHttpClientFactory ?? throw new ArgumentNullException(nameof(remoteHttpClientFactory));
        AuthenticationModes = CreateAuthenticationModes(platform);
        Environments = [];
        _statusMessage = "Selecione um ambiente para revisar a conexão.";
        SaveCommand = new RelayCommand(Save, () => !IsBusy);
        TestCommand = new AsyncRelayCommand(TestSelectedAsync, CanTest);
        RestoreSelectedCommand = new RelayCommand(RestoreSelected, () => SelectedEnvironment is not null && !IsBusy);
        Load();
    }

    public RemotePlatform Platform { get; }

    public string PlatformLabel => Platform == RemotePlatform.Dynatrace ? "Dynatrace" : "AppDynamics";

    public string PageTitle => $"Ambientes {PlatformLabel}";

    public string PageDescription => Platform == RemotePlatform.Dynatrace
        ? "Gerencie os tenants DEV, HML e PRD e valide a Environment API V2."
        : "Gerencie os Controllers DEV, HML e PRD e valide cada endpoint de API.";

    public string TenantIdentifierLabel => Platform == RemotePlatform.Dynatrace
        ? "ID do ambiente"
        : "Conta ou identificador";

    public string TenantIdentifierHint => Platform == RemotePlatform.Dynatrace
        ? "Exemplo: abc12345"
        : "Conta usada pelo Controller";

    public string BaseAddressLabel => Platform == RemotePlatform.Dynatrace
        ? "URL-base do tenant"
        : "URL-base do Controller";

    public string BaseAddressExample => Platform == RemotePlatform.Dynatrace
        ? "https://environment-id.live.dynatrace.com"
        : "https://controller.example.com";

    public string TestAddressExample => Platform == RemotePlatform.Dynatrace
        ? "Opcional: a Settings API V2 será derivada da URL-base"
        : "Opcional: quando vazio, será usada a URL-base";

    public string UsernameLabel => Platform == RemotePlatform.Dynatrace
        ? "Usuário de referência (opcional)"
        : "Usuário ou client_id (quando necessário)";

    public string KeyLabel => Platform == RemotePlatform.Dynatrace
        ? "Platform Token, OAuth token ou API Token"
        : "Access token, client secret ou senha";

    public string SettingsPath => _settingsService.StoragePath;

    public IReadOnlyList<AuthenticationModeOption> AuthenticationModes { get; }

    public ObservableCollection<ManagedConnectionViewModel> Environments { get; }

    public ManagedConnectionViewModel? SelectedEnvironment
    {
        get => _selectedEnvironment;
        set
        {
            if (SetProperty(ref _selectedEnvironment, value))
            {
                TestCommand.RaiseCanExecuteChanged();
                RestoreSelectedCommand.RaiseCanExecuteChanged();
                StatusMessage = value is null
                    ? "Selecione um ambiente para revisar a conexão."
                    : $"Editando {value.EnvironmentLabel}: {value.DisplayName}.";
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                SaveCommand.RaiseCanExecuteChanged();
                TestCommand.RaiseCanExecuteChanged();
                RestoreSelectedCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public RelayCommand SaveCommand { get; }

    public AsyncRelayCommand TestCommand { get; }

    public RelayCommand RestoreSelectedCommand { get; }

    private void Load(ManagedEnvironment? selected = null)
    {
        var platformSettings = GetPlatformSettings(_settingsService.Current.EffectiveIntegrations);
        var selectedEnvironment = selected ?? SelectedEnvironment?.Environment ?? ManagedEnvironment.Dev;
        Environments.Clear();
        foreach (var connection in platformSettings.Environments)
        {
            Environments.Add(new ManagedConnectionViewModel(connection, PlatformLabel));
        }

        SelectedEnvironment = Environments.First(environment => environment.Environment == selectedEnvironment);
    }

    private void Save()
    {
        try
        {
            var selected = SelectedEnvironment?.Environment;
            var platformSettings = new PlatformIntegrationSettings(
                Environments.Select(static environment => environment.ToSettings()).ToArray())
                .Normalize(Platform);
            var current = _settingsService.Current;
            var integrations = current.EffectiveIntegrations;
            integrations = Platform == RemotePlatform.Dynatrace
                ? integrations with { Dynatrace = platformSettings }
                : integrations with { AppDynamics = platformSettings };
            _settingsService.Save(current with { Integrations = integrations });
            Load(selected);
            HasError = false;
            StatusMessage = $"Ambientes {PlatformLabel} salvos no settings.json.";
            _logger.Write(
                ApplicationLogLevel.Information,
                "integration_environments_saved",
                "As configurações de ambientes foram atualizadas.",
                properties: new Dictionary<string, object?>
                {
                    ["platform"] = Platform.ToString(),
                    ["configuredEnvironments"] = platformSettings.Environments.Count(
                        static environment => !string.IsNullOrWhiteSpace(environment.BaseAddress)),
                    ["enabledEnvironments"] = platformSettings.Environments.Count(static environment => environment.Enabled)
                });
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            HasError = true;
            StatusMessage = exception.Message;
        }
    }

    private bool CanTest() => SelectedEnvironment is not null && !IsBusy;

    private async Task TestSelectedAsync()
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        IsBusy = true;
        var selected = SelectedEnvironment;
        selected.TestResult = RemoteTestPresentation.Running(PlatformLabel);
        try
        {
            var settings = selected.ToSettings().Normalize(Platform);
            var testAddress = ResolveTestAddress(settings);
            var request = new RemoteConnectionTestRequest(
                Platform,
                testAddress,
                RemoteTestMethod.Get,
                settings.AuthenticationMode,
                settings.Username,
                settings.Key,
                settings.ExpectedStatusCode);
            _remoteHttpClientFactory.Configure(_settingsService.Current.EffectiveRemoteHttp.ToOptions());
            var result = await _remoteHttpClientFactory.TestConnectionAsync(request);
            selected.TestResult = RemoteTestPresentation.FromResult(result);
            HasError = result.Outcome is not (
                RemoteConnectionTestOutcome.Success
                or RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus
                or RemoteConnectionTestOutcome.Redirect);
            StatusMessage = result.Message;
            _logger.Write(
                result.Outcome == RemoteConnectionTestOutcome.Success
                    ? ApplicationLogLevel.Information
                    : ApplicationLogLevel.Warning,
                "managed_environment_connection_test",
                "O teste do ambiente remoto foi concluído.",
                properties: new Dictionary<string, object?>
                {
                    ["platform"] = Platform.ToString(),
                    ["environment"] = settings.Environment.ToString(),
                    ["authenticationMode"] = settings.AuthenticationMode.ToString(),
                    ["statusCode"] = result.StatusCode is null ? null : (int)result.StatusCode,
                    ["outcome"] = result.Outcome.ToString(),
                    ["durationMilliseconds"] = result.ElapsedMilliseconds
                });
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            selected.TestResult = RemoteTestPresentation.Error(exception.Message);
            HasError = true;
            StatusMessage = exception.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private Uri ResolveTestAddress(ManagedConnectionSettings settings)
    {
        var addressText = settings.TestAddress;
        if (string.IsNullOrWhiteSpace(addressText))
        {
            if (string.IsNullOrWhiteSpace(settings.BaseAddress))
            {
                throw new ArgumentException("Informe a URL-base ou uma URL de teste para o ambiente selecionado.");
            }

            addressText = Platform == RemotePlatform.Dynatrace
                ? $"{settings.BaseAddress}/api/v2/settings/objects?schemaIds=builtin:alerting.profile&pageSize=1"
                : settings.BaseAddress;
        }

        if (!Uri.TryCreate(addressText, UriKind.Absolute, out var address)
            || address.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("A URL de teste deve ser uma URL HTTPS absoluta.");
        }

        return address;
    }

    private void RestoreSelected()
    {
        if (SelectedEnvironment is null)
        {
            return;
        }

        var environment = SelectedEnvironment.Environment;
        var index = Environments.IndexOf(SelectedEnvironment);
        var defaults = ManagedConnectionSettings.CreateDefault(Platform, environment);
        Environments[index] = new ManagedConnectionViewModel(defaults, PlatformLabel);
        SelectedEnvironment = Environments[index];
        HasError = false;
        StatusMessage = $"Campos de {environment.ToString().ToUpperInvariant()} restaurados. Salve para confirmar.";
    }

    private PlatformIntegrationSettings GetPlatformSettings(IntegrationSettings integrations) =>
        Platform == RemotePlatform.Dynatrace
            ? integrations.EffectiveDynatrace
            : integrations.EffectiveAppDynamics;

    private static IReadOnlyList<AuthenticationModeOption> CreateAuthenticationModes(RemotePlatform platform) =>
        platform == RemotePlatform.Dynatrace
            ?
            [
                new AuthenticationModeOption(RemoteAuthenticationMode.BearerToken, "Platform Token ou OAuth (Bearer)", "Envia Authorization: Bearer."),
                new AuthenticationModeOption(RemoteAuthenticationMode.DynatraceApiToken, "API Token legado", "Envia Authorization: Api-Token."),
                new AuthenticationModeOption(RemoteAuthenticationMode.None, "Sem autenticação", "Valida somente DNS, proxy, TLS e HTTP.")
            ]
            :
            [
                new AuthenticationModeOption(RemoteAuthenticationMode.BearerToken, "Token Bearer ou OAuth", "Envia Authorization: Bearer."),
                new AuthenticationModeOption(RemoteAuthenticationMode.Basic, "Autenticação Basic", "Usa o usuário e a chave informados."),
                new AuthenticationModeOption(RemoteAuthenticationMode.None, "Sem autenticação", "Valida somente DNS, proxy, TLS e HTTP.")
            ];
}
