using System.IO;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Application.Persistence;
using A2D.AlertMigrator.Application.Remote;
using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Configuration;
using A2D.AlertMigrator.Desktop.Services;

namespace A2D.AlertMigrator.Desktop.ViewModels.Settings;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly IUserSettingsService _settingsService;
    private readonly IFolderPicker _folderPicker;
    private readonly IApplicationLogger _logger;
    private readonly ILocalDatabaseService _databaseService;
    private readonly IFileSavePicker _fileSavePicker;
    private readonly IPathLauncher _pathLauncher;
    private readonly IFileOpenPicker _fileOpenPicker;
    private readonly IRemoteHttpClientFactory _remoteHttpClientFactory;
    private bool _recursiveByDefault;
    private BomPolicyOption _selectedBomPolicy = null!;
    private int _maxFileSizeMb;
    private int _maxFiles;
    private int _maxRulesPerApplication;
    private int _maxRulesTotal;
    private int _maxJsonDepth;
    private int _maxDqlCharacters;
    private string _logDirectory = string.Empty;
    private LogLevelOption _selectedLogLevel = null!;
    private bool _logRotationEnabled;
    private int _logRotationSizeMb;
    private int _retainedLogFileCount;
    private string _databasePath = string.Empty;
    private int _databaseBusyTimeoutSeconds;
    private bool _databaseUseWriteAheadLogging;
    private string _databaseSummary = "Verificando banco local...";
    private int _connectTimeoutSeconds;
    private int _requestTimeoutSeconds;
    private int _retryCount;
    private int _retryDelayMilliseconds;
    private int _pooledConnectionLifetimeMinutes;
    private int _maxConnectionsPerServer;
    private ProxyModeOption _selectedProxyMode = null!;
    private string _customProxyAddress = string.Empty;
    private bool _useDefaultProxyCredentials;
    private CertificateModeOption _selectedCertificateMode = null!;
    private bool _checkCertificateRevocation;
    private string _customCertificateAuthorityPath = string.Empty;
    private string _pinnedCertificateSha256 = string.Empty;
    private string _customHeadersText = string.Empty;
    private string _dynatraceTestAddress = string.Empty;
    private string _appDynamicsTestAddress = string.Empty;
    private RemoteTestMethodOption _selectedDynatraceTestMethod = null!;
    private RemoteTestMethodOption _selectedAppDynamicsTestMethod = null!;
    private AuthenticationModeOption _selectedDynatraceAuthentication = null!;
    private AuthenticationModeOption _selectedAppDynamicsAuthentication = null!;
    private string _dynatraceTestUsername = string.Empty;
    private string _appDynamicsTestUsername = string.Empty;
    private string _dynatraceTestSecret = string.Empty;
    private string _appDynamicsTestSecret = string.Empty;
    private int _dynatraceExpectedStatusCode;
    private int _appDynamicsExpectedStatusCode;
    private RemoteTestPresentation _dynatraceTestResult = RemoteTestPresentation.Idle("Dynatrace");
    private RemoteTestPresentation _appDynamicsTestResult = RemoteTestPresentation.Idle("AppDynamics");
    private bool _isRemoteTestRunning;
    private string _statusMessage = "As alterações serão usadas na próxima importação.";
    private bool _hasError;

    public SettingsViewModel(
        IUserSettingsService settingsService,
        IFolderPicker folderPicker,
        IApplicationLogger logger,
        ILocalDatabaseService databaseService,
        IFileSavePicker fileSavePicker,
        IPathLauncher pathLauncher,
        IFileOpenPicker fileOpenPicker,
        IRemoteHttpClientFactory remoteHttpClientFactory)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _fileSavePicker = fileSavePicker ?? throw new ArgumentNullException(nameof(fileSavePicker));
        _pathLauncher = pathLauncher ?? throw new ArgumentNullException(nameof(pathLauncher));
        _fileOpenPicker = fileOpenPicker ?? throw new ArgumentNullException(nameof(fileOpenPicker));
        _remoteHttpClientFactory = remoteHttpClientFactory ?? throw new ArgumentNullException(nameof(remoteHttpClientFactory));
        BomPolicies =
        [
            new BomPolicyOption(Utf8BomPolicy.Accept, "Aceitar com ou sem BOM", "Recomendado para compatibilidade com exportações do Windows."),
            new BomPolicyOption(Utf8BomPolicy.Reject, "Exigir sem BOM", "Rejeita arquivos que começam com BOM UTF-8."),
            new BomPolicyOption(Utf8BomPolicy.Require, "Exigir BOM", "Rejeita arquivos UTF-8 que não possuem BOM.")
        ];
        LogLevels =
        [
            new LogLevelOption(ApplicationLogLevel.Trace, "Trace", "Máximo detalhe para diagnóstico pontual."),
            new LogLevelOption(ApplicationLogLevel.Debug, "Debug", "Detalhes técnicos do fluxo de execução."),
            new LogLevelOption(ApplicationLogLevel.Information, "Informação", "Registra os eventos operacionais importantes. Esta é a opção recomendada."),
            new LogLevelOption(ApplicationLogLevel.Warning, "Aviso", "Somente situações inesperadas e falhas."),
            new LogLevelOption(ApplicationLogLevel.Error, "Erro", "Somente erros e falhas críticas."),
            new LogLevelOption(ApplicationLogLevel.Critical, "Crítico", "Somente falhas que impedem a operação."),
            new LogLevelOption(ApplicationLogLevel.None, "Desativado", "Não grava novos eventos em arquivo.")
        ];
        ProxyModes =
        [
            new ProxyModeOption(RemoteProxyMode.System, "Proxy do Windows", "Usa as configurações de proxy do Windows."),
            new ProxyModeOption(RemoteProxyMode.Disabled, "Sem proxy", "Conecta diretamente aos serviços remotos."),
            new ProxyModeOption(RemoteProxyMode.Custom, "Proxy personalizado", "Usa o servidor de proxy informado abaixo.")
        ];
        CertificateModes =
        [
            new CertificateModeOption(CertificateValidationMode.SystemTrust, "Confiança do Windows", "Valida a cadeia, o nome e a validade do certificado pelo repositório do Windows. Esta é a opção recomendada."),
            new CertificateModeOption(CertificateValidationMode.CustomCertificateAuthority, "Autoridade certificadora corporativa", "Usa um certificado PEM ou DER como raiz confiável."),
            new CertificateModeOption(CertificateValidationMode.Sha256Pinning, "Impressão digital SHA-256", "Aceita somente o certificado que corresponde à impressão digital informada."),
            new CertificateModeOption(CertificateValidationMode.DangerousAcceptAny, "Ignorar validação (inseguro)", "Use apenas em diagnósticos temporários, pois esta opção permite a interceptação da conexão.")
        ];
        TestMethods =
        [
            new RemoteTestMethodOption(RemoteTestMethod.Get, "GET"),
            new RemoteTestMethodOption(RemoteTestMethod.Head, "HEAD")
        ];
        DynatraceAuthenticationModes =
        [
            new AuthenticationModeOption(RemoteAuthenticationMode.None, "Sem autenticação", "Verifica somente DNS, proxy, TLS e resposta HTTP."),
            new AuthenticationModeOption(RemoteAuthenticationMode.BearerToken, "Platform Token ou OAuth (Bearer)", "Recomendado. Envia Authorization: Bearer para as APIs da plataforma."),
            new AuthenticationModeOption(RemoteAuthenticationMode.DynatraceApiToken, "API Token legado (Api-Token)", "Use somente com endpoints clássicos que exigem Authorization: Api-Token.")
        ];
        AppDynamicsAuthenticationModes =
        [
            new AuthenticationModeOption(RemoteAuthenticationMode.None, "Sem autenticação", "Verifica somente DNS, proxy, TLS e resposta HTTP."),
            new AuthenticationModeOption(RemoteAuthenticationMode.BearerToken, "Token Bearer ou OAuth", "Usa um token de acesso OAuth já emitido."),
            new AuthenticationModeOption(RemoteAuthenticationMode.Basic, "Autenticação Basic", "Mantém a compatibilidade com controladores AppDynamics legados.")
        ];
        SaveCommand = new RelayCommand(Save);
        ResetCommand = new RelayCommand(Reset);
        ChooseLogFolderCommand = new RelayCommand(ChooseLogFolder);
        WriteTestLogCommand = new RelayCommand(WriteTestLog);
        ChooseDatabaseFolderCommand = new RelayCommand(ChooseDatabaseFolder);
        OpenDatabaseFolderCommand = new RelayCommand(OpenDatabaseFolder);
        ExportDatabaseCommand = new RelayCommand(ExportDatabase);
        VerifyDatabaseCommand = new RelayCommand(VerifyDatabase);
        ChooseCertificateAuthorityCommand = new RelayCommand(ChooseCertificateAuthority);
        TestDynatraceCommand = new AsyncRelayCommand(() => TestRemoteConnectionAsync(RemotePlatform.Dynatrace), CanTestRemoteConnection);
        TestAppDynamicsCommand = new AsyncRelayCommand(() => TestRemoteConnectionAsync(RemotePlatform.AppDynamics), CanTestRemoteConnection);
        Load(_settingsService.Current);
        RefreshDatabaseInfo();
    }

    public IReadOnlyList<BomPolicyOption> BomPolicies { get; }

    public IReadOnlyList<LogLevelOption> LogLevels { get; }

    public IReadOnlyList<ProxyModeOption> ProxyModes { get; }

    public IReadOnlyList<CertificateModeOption> CertificateModes { get; }

    public IReadOnlyList<RemoteTestMethodOption> TestMethods { get; }

    public IReadOnlyList<AuthenticationModeOption> DynatraceAuthenticationModes { get; }

    public IReadOnlyList<AuthenticationModeOption> AppDynamicsAuthenticationModes { get; }

    public RelayCommand SaveCommand { get; }

    public RelayCommand ResetCommand { get; }

    public RelayCommand ChooseLogFolderCommand { get; }

    public RelayCommand WriteTestLogCommand { get; }

    public RelayCommand ChooseDatabaseFolderCommand { get; }

    public RelayCommand OpenDatabaseFolderCommand { get; }

    public RelayCommand ExportDatabaseCommand { get; }

    public RelayCommand VerifyDatabaseCommand { get; }

    public RelayCommand ChooseCertificateAuthorityCommand { get; }

    public AsyncRelayCommand TestDynatraceCommand { get; }

    public AsyncRelayCommand TestAppDynamicsCommand { get; }

    public string StoragePath => _settingsService.StoragePath;

    public string CurrentLogPath => _logger.CurrentLogPath ?? "Arquivo de log indisponível";

    public string CurrentDatabasePath => _databaseService.CurrentPath;

    public bool RecursiveByDefault
    {
        get => _recursiveByDefault;
        set => SetProperty(ref _recursiveByDefault, value);
    }

    public BomPolicyOption SelectedBomPolicy
    {
        get => _selectedBomPolicy;
        set => SetProperty(ref _selectedBomPolicy, value);
    }

    public int MaxFileSizeMb
    {
        get => _maxFileSizeMb;
        set => SetProperty(ref _maxFileSizeMb, value);
    }

    public int MaxFiles
    {
        get => _maxFiles;
        set => SetProperty(ref _maxFiles, value);
    }

    public int MaxRulesPerApplication
    {
        get => _maxRulesPerApplication;
        set => SetProperty(ref _maxRulesPerApplication, value);
    }

    public int MaxRulesTotal
    {
        get => _maxRulesTotal;
        set => SetProperty(ref _maxRulesTotal, value);
    }

    public int MaxJsonDepth
    {
        get => _maxJsonDepth;
        set => SetProperty(ref _maxJsonDepth, value);
    }

    public int MaxDqlCharacters
    {
        get => _maxDqlCharacters;
        set => SetProperty(ref _maxDqlCharacters, value);
    }

    public string LogDirectory
    {
        get => _logDirectory;
        set => SetProperty(ref _logDirectory, value);
    }

    public LogLevelOption SelectedLogLevel
    {
        get => _selectedLogLevel;
        set => SetProperty(ref _selectedLogLevel, value);
    }

    public bool LogRotationEnabled
    {
        get => _logRotationEnabled;
        set => SetProperty(ref _logRotationEnabled, value);
    }

    public int LogRotationSizeMb
    {
        get => _logRotationSizeMb;
        set => SetProperty(ref _logRotationSizeMb, value);
    }

    public int RetainedLogFileCount
    {
        get => _retainedLogFileCount;
        set => SetProperty(ref _retainedLogFileCount, value);
    }

    public string DatabasePath
    {
        get => _databasePath;
        set => SetProperty(ref _databasePath, value);
    }

    public int DatabaseBusyTimeoutSeconds
    {
        get => _databaseBusyTimeoutSeconds;
        set => SetProperty(ref _databaseBusyTimeoutSeconds, value);
    }

    public bool DatabaseUseWriteAheadLogging
    {
        get => _databaseUseWriteAheadLogging;
        set => SetProperty(ref _databaseUseWriteAheadLogging, value);
    }

    public string DatabaseSummary
    {
        get => _databaseSummary;
        private set => SetProperty(ref _databaseSummary, value);
    }

    public int ConnectTimeoutSeconds
    {
        get => _connectTimeoutSeconds;
        set => SetProperty(ref _connectTimeoutSeconds, value);
    }

    public int RequestTimeoutSeconds
    {
        get => _requestTimeoutSeconds;
        set => SetProperty(ref _requestTimeoutSeconds, value);
    }

    public int RetryCount
    {
        get => _retryCount;
        set => SetProperty(ref _retryCount, value);
    }

    public int RetryDelayMilliseconds
    {
        get => _retryDelayMilliseconds;
        set => SetProperty(ref _retryDelayMilliseconds, value);
    }

    public int PooledConnectionLifetimeMinutes
    {
        get => _pooledConnectionLifetimeMinutes;
        set => SetProperty(ref _pooledConnectionLifetimeMinutes, value);
    }

    public int MaxConnectionsPerServer
    {
        get => _maxConnectionsPerServer;
        set => SetProperty(ref _maxConnectionsPerServer, value);
    }

    public ProxyModeOption SelectedProxyMode
    {
        get => _selectedProxyMode;
        set
        {
            if (SetProperty(ref _selectedProxyMode, value))
            {
                OnPropertyChanged(nameof(IsCustomProxy));
                OnPropertyChanged(nameof(IsProxyEnabled));
            }
        }
    }

    public bool IsCustomProxy => SelectedProxyMode?.Mode == RemoteProxyMode.Custom;

    public bool IsProxyEnabled => SelectedProxyMode?.Mode != RemoteProxyMode.Disabled;

    public string CustomProxyAddress
    {
        get => _customProxyAddress;
        set => SetProperty(ref _customProxyAddress, value);
    }

    public bool UseDefaultProxyCredentials
    {
        get => _useDefaultProxyCredentials;
        set => SetProperty(ref _useDefaultProxyCredentials, value);
    }

    public CertificateModeOption SelectedCertificateMode
    {
        get => _selectedCertificateMode;
        set
        {
            if (SetProperty(ref _selectedCertificateMode, value))
            {
                OnPropertyChanged(nameof(IsCustomCertificateAuthority));
                OnPropertyChanged(nameof(IsCertificatePinning));
                OnPropertyChanged(nameof(IsDangerousCertificateMode));
            }
        }
    }

    public bool IsCustomCertificateAuthority =>
        SelectedCertificateMode?.Mode == CertificateValidationMode.CustomCertificateAuthority;

    public bool IsCertificatePinning =>
        SelectedCertificateMode?.Mode == CertificateValidationMode.Sha256Pinning;

    public bool IsDangerousCertificateMode =>
        SelectedCertificateMode?.Mode == CertificateValidationMode.DangerousAcceptAny;

    public bool CheckCertificateRevocation
    {
        get => _checkCertificateRevocation;
        set => SetProperty(ref _checkCertificateRevocation, value);
    }

    public string CustomCertificateAuthorityPath
    {
        get => _customCertificateAuthorityPath;
        set => SetProperty(ref _customCertificateAuthorityPath, value);
    }

    public string PinnedCertificateSha256
    {
        get => _pinnedCertificateSha256;
        set => SetProperty(ref _pinnedCertificateSha256, value);
    }

    public string CustomHeadersText
    {
        get => _customHeadersText;
        set => SetProperty(ref _customHeadersText, value);
    }

    public string DynatraceTestAddress
    {
        get => _dynatraceTestAddress;
        set => SetProperty(ref _dynatraceTestAddress, value);
    }

    public string AppDynamicsTestAddress
    {
        get => _appDynamicsTestAddress;
        set => SetProperty(ref _appDynamicsTestAddress, value);
    }

    public RemoteTestMethodOption SelectedDynatraceTestMethod
    {
        get => _selectedDynatraceTestMethod;
        set => SetProperty(ref _selectedDynatraceTestMethod, value);
    }

    public RemoteTestMethodOption SelectedAppDynamicsTestMethod
    {
        get => _selectedAppDynamicsTestMethod;
        set => SetProperty(ref _selectedAppDynamicsTestMethod, value);
    }

    public AuthenticationModeOption SelectedDynatraceAuthentication
    {
        get => _selectedDynatraceAuthentication;
        set
        {
            if (SetProperty(ref _selectedDynatraceAuthentication, value))
            {
                OnPropertyChanged(nameof(DynatraceAuthenticationRequiresSecret));
                OnPropertyChanged(nameof(IsDynatraceBasicAuthentication));
            }
        }
    }

    public AuthenticationModeOption SelectedAppDynamicsAuthentication
    {
        get => _selectedAppDynamicsAuthentication;
        set
        {
            if (SetProperty(ref _selectedAppDynamicsAuthentication, value))
            {
                OnPropertyChanged(nameof(AppDynamicsAuthenticationRequiresSecret));
                OnPropertyChanged(nameof(IsAppDynamicsBasicAuthentication));
            }
        }
    }

    public bool DynatraceAuthenticationRequiresSecret =>
        SelectedDynatraceAuthentication?.Mode != RemoteAuthenticationMode.None;

    public bool AppDynamicsAuthenticationRequiresSecret =>
        SelectedAppDynamicsAuthentication?.Mode != RemoteAuthenticationMode.None;

    public bool IsDynatraceBasicAuthentication =>
        SelectedDynatraceAuthentication?.Mode == RemoteAuthenticationMode.Basic;

    public bool IsAppDynamicsBasicAuthentication =>
        SelectedAppDynamicsAuthentication?.Mode == RemoteAuthenticationMode.Basic;

    public string DynatraceTestUsername
    {
        get => _dynatraceTestUsername;
        set => SetProperty(ref _dynatraceTestUsername, value);
    }

    public string AppDynamicsTestUsername
    {
        get => _appDynamicsTestUsername;
        set => SetProperty(ref _appDynamicsTestUsername, value);
    }

    public string DynatraceTestSecret
    {
        private get => _dynatraceTestSecret;
        set => _dynatraceTestSecret = value ?? string.Empty;
    }

    public string AppDynamicsTestSecret
    {
        private get => _appDynamicsTestSecret;
        set => _appDynamicsTestSecret = value ?? string.Empty;
    }

    public int DynatraceExpectedStatusCode
    {
        get => _dynatraceExpectedStatusCode;
        set => SetProperty(ref _dynatraceExpectedStatusCode, value);
    }

    public int AppDynamicsExpectedStatusCode
    {
        get => _appDynamicsExpectedStatusCode;
        set => SetProperty(ref _appDynamicsExpectedStatusCode, value);
    }

    public RemoteTestPresentation DynatraceTestResult
    {
        get => _dynatraceTestResult;
        private set => SetProperty(ref _dynatraceTestResult, value);
    }

    public RemoteTestPresentation AppDynamicsTestResult
    {
        get => _appDynamicsTestResult;
        private set => SetProperty(ref _appDynamicsTestResult, value);
    }

    public bool IsRemoteTestRunning
    {
        get => _isRemoteTestRunning;
        private set
        {
            if (SetProperty(ref _isRemoteTestRunning, value))
            {
                TestDynatraceCommand.RaiseCanExecuteChanged();
                TestAppDynamicsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public bool HasError
    {
        get => _hasError;
        private set => SetProperty(ref _hasError, value);
    }

    private void Save()
    {
        try
        {
            Validate();
            _settingsService.Save(new UserSettings(
                RecursiveByDefault,
                SelectedBomPolicy.Policy,
                MaxFileSizeMb,
                MaxFiles,
                MaxRulesPerApplication,
                MaxRulesTotal,
                MaxJsonDepth,
                MaxDqlCharacters,
                new ApplicationLogSettings(
                    LogDirectory,
                    SelectedLogLevel.Level,
                    LogRotationEnabled,
                    LogRotationSizeMb,
                    RetainedLogFileCount),
                new LocalDatabaseSettings(
                    DatabasePath,
                    DatabaseBusyTimeoutSeconds,
                    DatabaseUseWriteAheadLogging),
                CreateRemoteHttpSettings(),
                CreateConnectionTestSettings(),
                _settingsService.Current.EffectiveIntegrations));
            Load(_settingsService.Current);
            OnPropertyChanged(nameof(CurrentLogPath));
            RefreshDatabaseInfo();

            var configurationErrors = new[]
                { _logger.LastError, _databaseService.LastError, _remoteHttpClientFactory.LastError }
                .Where(static error => !string.IsNullOrWhiteSpace(error))
                .ToArray();
            if (configurationErrors.Length == 0)
            {
                HasError = false;
                StatusMessage = "Configurações salvas e módulos locais atualizados em tempo real.";
                _logger.Write(
                    ApplicationLogLevel.Information,
                    "settings_saved",
                    "As configurações locais foram atualizadas.",
                    properties: new Dictionary<string, object?>
                    {
                        ["minimumLogLevel"] = SelectedLogLevel.Level.ToString(),
                        ["rotationEnabled"] = LogRotationEnabled,
                        ["sqliteJournalMode"] = DatabaseUseWriteAheadLogging ? "WAL" : "DELETE",
                        ["httpProxyMode"] = SelectedProxyMode.Mode.ToString(),
                        ["tlsValidationMode"] = SelectedCertificateMode.Mode.ToString()
                    });
            }
            else
            {
                HasError = true;
                StatusMessage = $"Configurações salvas com ressalvas. {string.Join(". ", configurationErrors)}";
            }
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException
            or PathTooLongException
            or OverflowException)
        {
            HasError = true;
            StatusMessage = exception.Message;
        }
    }

    private void Reset()
    {
        try
        {
            var integrations = _settingsService.Current.EffectiveIntegrations;
            _settingsService.Save(UserSettings.Default with { Integrations = integrations });
            Load(_settingsService.Current);
            RefreshDatabaseInfo();
            HasError = _databaseService.LastError is not null
                || _logger.LastError is not null
                || _remoteHttpClientFactory.LastError is not null;
            StatusMessage = HasError
                ? "Valores padrão restaurados, mas um recurso local está indisponível."
                : "Valores padrão restaurados.";
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            HasError = true;
            StatusMessage = exception.Message;
        }
    }

    private void Load(UserSettings settings)
    {
        RecursiveByDefault = settings.RecursiveByDefault;
        SelectedBomPolicy = BomPolicies.First(option => option.Policy == settings.Utf8BomPolicy);
        MaxFileSizeMb = settings.MaxFileSizeMb;
        MaxFiles = settings.MaxFiles;
        MaxRulesPerApplication = settings.MaxRulesPerApplication;
        MaxRulesTotal = settings.MaxRulesTotal;
        MaxJsonDepth = settings.MaxJsonDepth;
        MaxDqlCharacters = settings.MaxDqlCharacters;
        var logging = settings.EffectiveLogging;
        LogDirectory = logging.DirectoryPath;
        SelectedLogLevel = LogLevels.First(option => option.Level == logging.MinimumLevel);
        LogRotationEnabled = logging.RotationEnabled;
        LogRotationSizeMb = logging.RotationSizeMb;
        RetainedLogFileCount = logging.RetainedFileCount;
        var database = settings.EffectiveDatabase;
        DatabasePath = database.FilePath;
        DatabaseBusyTimeoutSeconds = database.BusyTimeoutSeconds;
        DatabaseUseWriteAheadLogging = database.UseWriteAheadLogging;
        var remote = settings.EffectiveRemoteHttp;
        ConnectTimeoutSeconds = remote.ConnectTimeoutSeconds;
        RequestTimeoutSeconds = remote.RequestTimeoutSeconds;
        RetryCount = remote.RetryCount;
        RetryDelayMilliseconds = remote.RetryDelayMilliseconds;
        PooledConnectionLifetimeMinutes = remote.PooledConnectionLifetimeMinutes;
        MaxConnectionsPerServer = remote.MaxConnectionsPerServer;
        SelectedProxyMode = ProxyModes.First(option => option.Mode == remote.ProxyMode);
        CustomProxyAddress = remote.CustomProxyAddress;
        UseDefaultProxyCredentials = remote.UseDefaultProxyCredentials;
        SelectedCertificateMode = CertificateModes.First(option => option.Mode == remote.TlsValidationMode);
        CheckCertificateRevocation = remote.CheckCertificateRevocation;
        CustomCertificateAuthorityPath = remote.CustomCertificateAuthorityPath;
        PinnedCertificateSha256 = remote.PinnedCertificateSha256;
        CustomHeadersText = FormatHeaders(remote.CustomHeaders);
        var tests = settings.EffectiveConnectionTests;
        DynatraceTestAddress = tests.Dynatrace.TestAddress;
        AppDynamicsTestAddress = tests.AppDynamics.TestAddress;
        SelectedDynatraceTestMethod = TestMethods.First(option => option.Method == tests.Dynatrace.Method);
        SelectedAppDynamicsTestMethod = TestMethods.First(option => option.Method == tests.AppDynamics.Method);
        SelectedDynatraceAuthentication = DynatraceAuthenticationModes.First(option => option.Mode == tests.Dynatrace.AuthenticationMode);
        SelectedAppDynamicsAuthentication = AppDynamicsAuthenticationModes.First(option => option.Mode == tests.AppDynamics.AuthenticationMode);
        DynatraceTestUsername = tests.Dynatrace.Username;
        AppDynamicsTestUsername = tests.AppDynamics.Username;
        DynatraceExpectedStatusCode = tests.Dynatrace.ExpectedStatusCode;
        AppDynamicsExpectedStatusCode = tests.AppDynamics.ExpectedStatusCode;
        DynatraceTestResult = RemoteTestPresentation.Idle("Dynatrace");
        AppDynamicsTestResult = RemoteTestPresentation.Idle("AppDynamics");
        OnPropertyChanged(nameof(CurrentLogPath));
        OnPropertyChanged(nameof(CurrentDatabasePath));
    }

    private void Validate()
    {
        if (MaxFileSizeMb is < 1 or > 1024)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFileSizeMb), "Tamanho por arquivo deve estar entre 1 e 1024 MiB.");
        }

        if (MaxFiles is < 1 or > 10_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxFiles), "Quantidade de arquivos deve estar entre 1 e 10.000.");
        }

        if (MaxRulesPerApplication is < 1 or > 50_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRulesPerApplication), "Regras por aplicação deve estar entre 1 e 50.000.");
        }

        if (MaxRulesTotal < MaxRulesPerApplication || MaxRulesTotal > 500_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRulesTotal), "Total de regras deve ser maior ou igual ao limite por aplicação e no máximo 500.000.");
        }

        if (MaxJsonDepth is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxJsonDepth), "Profundidade JSON deve estar entre 1 e 256.");
        }

        if (MaxDqlCharacters is < 1024 or > 1_000_000)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxDqlCharacters), "Tamanho da DQL deve estar entre 1.024 e 1.000.000 caracteres.");
        }

        _ = new ApplicationLogSettings(
            LogDirectory,
            SelectedLogLevel.Level,
            LogRotationEnabled,
            LogRotationSizeMb,
            RetainedLogFileCount).Normalize();

        _ = new LocalDatabaseSettings(
            DatabasePath,
            DatabaseBusyTimeoutSeconds,
            DatabaseUseWriteAheadLogging).Normalize();

        _ = CreateRemoteHttpSettings().Normalize();
        _ = CreateConnectionTestSettings().Normalize();
    }

    private RemoteHttpSettings CreateRemoteHttpSettings() => new(
        ConnectTimeoutSeconds,
        RequestTimeoutSeconds,
        RetryCount,
        RetryDelayMilliseconds,
        PooledConnectionLifetimeMinutes,
        MaxConnectionsPerServer,
        SelectedProxyMode.Mode,
        CustomProxyAddress,
        UseDefaultProxyCredentials,
        SelectedCertificateMode.Mode,
        CheckCertificateRevocation,
        CustomCertificateAuthorityPath,
        PinnedCertificateSha256,
        ParseHeaders(CustomHeadersText));

    private RemoteConnectionTestSettings CreateConnectionTestSettings() => new(
        new RemoteEndpointTestSettings(
            DynatraceTestAddress,
            SelectedDynatraceTestMethod.Method,
            SelectedDynatraceAuthentication.Mode,
            DynatraceTestUsername,
            DynatraceExpectedStatusCode),
        new RemoteEndpointTestSettings(
            AppDynamicsTestAddress,
            SelectedAppDynamicsTestMethod.Method,
            SelectedAppDynamicsAuthentication.Mode,
            AppDynamicsTestUsername,
            AppDynamicsExpectedStatusCode));

    private void ChooseCertificateAuthority()
    {
        var selectedFile = _fileOpenPicker.PickFile(
            "Selecione a autoridade certificadora corporativa",
            "Certificados (*.cer;*.crt;*.pem)|*.cer;*.crt;*.pem|Todos os arquivos (*.*)|*.*",
            Path.GetDirectoryName(CustomCertificateAuthorityPath));
        if (selectedFile is not null)
        {
            CustomCertificateAuthorityPath = selectedFile;
        }
    }

    private bool CanTestRemoteConnection() => !IsRemoteTestRunning;

    private async Task TestRemoteConnectionAsync(RemotePlatform platform)
    {
        IsRemoteTestRunning = true;
        try
        {
            var options = CreateRemoteHttpSettings().Normalize().ToOptions();
            _remoteHttpClientFactory.Configure(options);
            SetTestPresentation(platform, RemoteTestPresentation.Running(GetPlatformLabel(platform)));
            var request = CreateTestRequest(platform);
            var result = await _remoteHttpClientFactory.TestConnectionAsync(request);
            var accepted = result.Outcome is RemoteConnectionTestOutcome.Success
                or RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus
                or RemoteConnectionTestOutcome.Redirect;
            HasError = !accepted;
            SetTestPresentation(platform, RemoteTestPresentation.FromResult(result));
            StatusMessage = result.Outcome switch
            {
                RemoteConnectionTestOutcome.Success => $"Teste do {GetPlatformLabel(platform)} concluído com sucesso.",
                RemoteConnectionTestOutcome.SuccessWithUnexpectedStatus => $"O {GetPlatformLabel(platform)} está acessível, mas respondeu com outro status de sucesso.",
                RemoteConnectionTestOutcome.Redirect => $"O {GetPlatformLabel(platform)} está acessível e informou um redirecionamento. Revise o destino exibido.",
                _ => result.Message
            };
            _logger.Write(
                result.Outcome == RemoteConnectionTestOutcome.Success
                    ? ApplicationLogLevel.Information
                    : ApplicationLogLevel.Warning,
                "remote_connection_test",
                "Teste do endpoint remoto concluído.",
                properties: new Dictionary<string, object?>
                {
                    ["platform"] = platform.ToString(),
                    ["method"] = request.Method.ToString(),
                    ["authenticationMode"] = request.AuthenticationMode.ToString(),
                    ["transportSucceeded"] = result.TransportSucceeded,
                    ["expectedStatusReceived"] = result.ExpectedStatusReceived,
                    ["expectedStatusCode"] = request.ExpectedStatusCode,
                    ["statusCode"] = result.StatusCode is null ? null : (int)result.StatusCode,
                    ["outcome"] = result.Outcome.ToString(),
                    ["redirectReceived"] = !string.IsNullOrWhiteSpace(result.RedirectLocation),
                    ["durationMilliseconds"] = result.ElapsedMilliseconds,
                    ["httpVersion"] = result.HttpVersion,
                    ["tlsValidationMode"] = SelectedCertificateMode.Mode.ToString()
                });
        }
        catch (Exception exception) when (exception is ArgumentException
            or InvalidOperationException
            or IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            HasError = true;
            SetTestPresentation(platform, RemoteTestPresentation.Error(exception.Message));
            StatusMessage = exception.Message;
        }
        finally
        {
            IsRemoteTestRunning = false;
        }
    }

    private RemoteConnectionTestRequest CreateTestRequest(RemotePlatform platform)
    {
        var addressText = platform == RemotePlatform.Dynatrace
            ? DynatraceTestAddress
            : AppDynamicsTestAddress;
        if (!Uri.TryCreate(addressText?.Trim(), UriKind.Absolute, out var address))
        {
            throw new ArgumentException($"Informe uma URL de teste válida para o {GetPlatformLabel(platform)}.");
        }

        var method = platform == RemotePlatform.Dynatrace
            ? SelectedDynatraceTestMethod.Method
            : SelectedAppDynamicsTestMethod.Method;
        var authentication = platform == RemotePlatform.Dynatrace
            ? SelectedDynatraceAuthentication.Mode
            : SelectedAppDynamicsAuthentication.Mode;
        var username = platform == RemotePlatform.Dynatrace
            ? DynatraceTestUsername
            : AppDynamicsTestUsername;
        var secret = platform == RemotePlatform.Dynatrace
            ? DynatraceTestSecret
            : AppDynamicsTestSecret;
        var expectedStatus = platform == RemotePlatform.Dynatrace
            ? DynatraceExpectedStatusCode
            : AppDynamicsExpectedStatusCode;
        return new RemoteConnectionTestRequest(
            platform,
            address,
            method,
            authentication,
            username,
            secret,
            expectedStatus);
    }

    private void SetTestPresentation(RemotePlatform platform, RemoteTestPresentation presentation)
    {
        if (platform == RemotePlatform.Dynatrace)
        {
            DynatraceTestResult = presentation;
        }
        else
        {
            AppDynamicsTestResult = presentation;
        }
    }

    private static Dictionary<string, string> ParseHeaders(string text)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var lines = (text ?? string.Empty).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var separator = line.IndexOf(':');
            if (separator <= 0 || separator == line.Length - 1)
            {
                throw new ArgumentException($"Cabeçalho customizado inválido: '{line}'. Use Nome: valor.");
            }

            var name = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (!headers.TryAdd(name, value))
            {
                throw new ArgumentException($"O cabeçalho '{name}' está duplicado.");
            }
        }

        return headers;
    }

    private static string FormatHeaders(IReadOnlyDictionary<string, string> headers) =>
        string.Join(Environment.NewLine, headers.OrderBy(static item => item.Key).Select(static item => $"{item.Key}: {item.Value}"));

    private static string GetPlatformLabel(RemotePlatform platform) => platform switch
    {
        RemotePlatform.Dynatrace => "Dynatrace",
        RemotePlatform.AppDynamics => "AppDynamics",
        _ => platform.ToString()
    };

    private void ChooseLogFolder()
    {
        var selectedFolder = _folderPicker.PickFolder(LogDirectory, "Selecione a pasta para os logs estruturados");
        if (selectedFolder is not null)
        {
            LogDirectory = selectedFolder;
        }
    }

    private void WriteTestLog()
    {
        var level = _settingsService.Current.EffectiveLogging.MinimumLevel;
        if (level == ApplicationLogLevel.None)
        {
            HasError = true;
            StatusMessage = "O log está desativado. Escolha outro nível e salve antes do teste.";
            return;
        }

        _logger.Write(
            level,
            "log_test",
            "Evento de teste solicitado na tela de configurações.",
            properties: new Dictionary<string, object?>
            {
                ["source"] = "settings"
            });
        OnPropertyChanged(nameof(CurrentLogPath));

        HasError = _logger.LastError is not null;
        StatusMessage = HasError
            ? $"Não foi possível gravar o evento de teste: {_logger.LastError}"
            : $"Evento de teste gravado em {CurrentLogPath}";
    }

    private void ChooseDatabaseFolder()
    {
        var currentDirectory = Path.GetDirectoryName(DatabasePath);
        var selectedFolder = _folderPicker.PickFolder(currentDirectory, "Selecione a pasta do banco SQLite");
        if (selectedFolder is not null)
        {
            var fileName = Path.GetFileName(DatabasePath);
            DatabasePath = Path.Combine(
                selectedFolder,
                string.IsNullOrWhiteSpace(fileName) ? "a2d-alert-migrator.db" : fileName);
        }
    }

    private void OpenDatabaseFolder()
    {
        try
        {
            var directory = Path.GetDirectoryName(_databaseService.CurrentPath)
                ?? throw new InvalidOperationException("A pasta do banco SQLite é inválida.");
            _pathLauncher.OpenFolder(directory);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            HasError = true;
            StatusMessage = exception.Message;
        }
    }

    private void ExportDatabase()
    {
        try
        {
            var initialDirectory = Path.GetDirectoryName(_databaseService.CurrentPath);
            var suggestedName = $"a2d-alert-migrator-backup-{DateTimeOffset.Now:yyyyMMdd-HHmmss}.db";
            var destination = _fileSavePicker.PickFile(
                "Exportar banco SQLite",
                "Banco SQLite (*.db)|*.db|Todos os arquivos (*.*)|*.*",
                ".db",
                suggestedName,
                initialDirectory);
            if (destination is null)
            {
                return;
            }

            _databaseService.Export(destination);
            _logger.Write(
                ApplicationLogLevel.Information,
                "database_exported",
                "Uma cópia consistente do banco SQLite foi exportada.",
                properties: new Dictionary<string, object?>
                {
                    ["fileName"] = Path.GetFileName(destination)
                });
            HasError = false;
            StatusMessage = $"Banco exportado com sucesso para {destination}";
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or ArgumentException)
        {
            HasError = true;
            StatusMessage = exception.Message;
        }
    }

    private void VerifyDatabase()
    {
        var isHealthy = _databaseService.VerifyIntegrity();
        RefreshDatabaseInfo();
        HasError = !isHealthy;
        StatusMessage = isHealthy
            ? "Integridade do banco SQLite verificada com sucesso."
            : $"Falha na verificação do SQLite: {_databaseService.LastError}";
    }

    private void RefreshDatabaseInfo()
    {
        var info = _databaseService.GetInfo();
        var historyLabel = info.HistoryRecordCount == 1
            ? "1 execução"
            : $"{info.HistoryRecordCount} execuções";
        OnPropertyChanged(nameof(CurrentDatabasePath));
        DatabaseSummary = info.LastError is null
            ? $"Banco pronto. Esquema v{info.SchemaVersion}. Modo {info.JournalMode}. Histórico com {historyLabel}. Tamanho {FormatBytes(info.SizeBytes)}."
            : $"Banco indisponível. {info.LastError}";
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return $"{bytes / 1024d:N1} KiB";
        }

        return $"{bytes / (1024d * 1024d):N1} MiB";
    }
}
