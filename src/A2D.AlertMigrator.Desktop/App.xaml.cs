using System.Windows;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Application.Persistence;
using A2D.AlertMigrator.Application.Remote;
using A2D.AlertMigrator.Desktop.Services;
using A2D.AlertMigrator.Desktop.ViewModels;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;
using A2D.AlertMigrator.Desktop.ViewModels.Help;
using A2D.AlertMigrator.Desktop.ViewModels.Importing;
using A2D.AlertMigrator.Desktop.ViewModels.Integrations;
using A2D.AlertMigrator.Desktop.ViewModels.Settings;
using A2D.AlertMigrator.Desktop.Views;
using A2D.AlertMigrator.Infrastructure.Importing.Json;
using A2D.AlertMigrator.Infrastructure.Logging;
using A2D.AlertMigrator.Infrastructure.Persistence;
using A2D.AlertMigrator.Infrastructure.Remote;

namespace A2D.AlertMigrator.Desktop;

public partial class App : System.Windows.Application
{
    private IUserSettingsService? _settingsService;
    private IApplicationLogger? _logger;
    private ILocalDatabaseService? _databaseService;
    private IRemoteHttpClientFactory? _remoteHttpClientFactory;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var settingsService = new JsonUserSettingsService();
        var logger = new JsonLinesFileLogger(settingsService.Current.EffectiveLogging.ToFileLogOptions());
        var databaseService = new SqliteLocalDatabaseService(settingsService.Current.EffectiveDatabase.ToOptions());
        var remoteHttpClientFactory = new ResilientRemoteHttpClientFactory(
            settingsService.Current.EffectiveRemoteHttp.ToOptions());
        _settingsService = settingsService;
        _logger = logger;
        _databaseService = databaseService;
        _remoteHttpClientFactory = remoteHttpClientFactory;
        settingsService.SettingsChanged += OnSettingsChanged;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        logger.Write(
            ApplicationLogLevel.Information,
            "application_started",
            "O aplicativo foi iniciado em modo local.",
            properties: new Dictionary<string, object?>
            {
                ["operatingSystem"] = Environment.OSVersion.VersionString,
                ["framework"] = Environment.Version.ToString()
            });

        var importUseCase = new ImportJsonFolderUseCase(new JsonFolderImportAdapter());
        var folderPicker = new WindowsFolderPicker();
        var importViewModel = new ImportWorkspaceViewModel(
            importUseCase,
            folderPicker,
            settingsService,
            logger,
            databaseService);
        var settingsViewModel = new SettingsViewModel(
            settingsService,
            folderPicker,
            logger,
            databaseService,
            new WindowsFileSavePicker(),
            new WindowsPathLauncher(),
            new WindowsFileOpenPicker(),
            remoteHttpClientFactory);
        var dynatraceIntegrationsViewModel = new IntegrationSettingsViewModel(
            RemotePlatform.Dynatrace,
            settingsService,
            logger,
            remoteHttpClientFactory);
        var appDynamicsIntegrationsViewModel = new IntegrationSettingsViewModel(
            RemotePlatform.AppDynamics,
            settingsService,
            logger,
            remoteHttpClientFactory);
        var alertingProfileClient = new DynatraceAlertingProfileClient(remoteHttpClientFactory);
        var alertingProfilesViewModel = new DynatraceAlertingProfilesViewModel(
            new SyncDynatraceAlertingProfilesUseCase(alertingProfileClient, databaseService),
            databaseService,
            settingsService,
            logger,
            new WindowsAlertingProfileDetailsDialog());
        var anomalyDetectorClient = new DynatraceAnomalyDetectorClient(remoteHttpClientFactory);
        var anomalyDetectorsViewModel = new DynatraceAnomalyDetectorsViewModel(
            new SyncDynatraceAnomalyDetectorsUseCase(anomalyDetectorClient, databaseService),
            databaseService,
            settingsService,
            logger,
            new WindowsAnomalyDetectorDetailsDialog());
        var davisEventClient = new DynatraceDavisEventClient(remoteHttpClientFactory);
        var davisEventsViewModel = new DynatraceDavisEventsViewModel(
            new SyncDynatraceDavisEventsUseCase(davisEventClient, databaseService),
            databaseService,
            settingsService,
            logger,
            new WindowsDavisEventDetailsDialog());
        var problemsViewModel = new DynatraceProblemsViewModel(
            new SyncDynatraceProblemsUseCase(
                new DynatraceProblemClient(remoteHttpClientFactory),
                databaseService),
            databaseService,
            settingsService,
            logger,
            new WindowsProblemDetailsDialog());
        var uriLauncher = new WindowsExternalUriLauncher();
        var mainViewModel = new MainWindowViewModel(
            importViewModel,
            alertingProfilesViewModel,
            anomalyDetectorsViewModel,
            davisEventsViewModel,
            problemsViewModel,
            settingsViewModel,
            dynatraceIntegrationsViewModel,
            appDynamicsIntegrationsViewModel,
            new DynatraceHelpViewModel(uriLauncher),
            new AppDynamicsHelpViewModel(uriLauncher));
        if (e.Args.Contains("--problems", StringComparer.OrdinalIgnoreCase))
        {
            mainViewModel.NavigateDynatraceProblemsCommand.Execute(null);
        }
        else if (e.Args.Contains("--davis-events", StringComparer.OrdinalIgnoreCase))
        {
            mainViewModel.NavigateDynatraceDavisEventsCommand.Execute(null);
        }
        else if (e.Args.Contains("--anomaly-detectors", StringComparer.OrdinalIgnoreCase))
        {
            mainViewModel.NavigateDynatraceAnomalyDetectorsCommand.Execute(null);
        }
        else if (e.Args.Contains("--alerting-profiles", StringComparer.OrdinalIgnoreCase))
        {
            mainViewModel.NavigateDynatraceAlertingProfilesCommand.Execute(null);
        }
        else if (e.Args.Contains("--dynatrace-settings", StringComparer.OrdinalIgnoreCase))
        {
            mainViewModel.NavigateDynatraceIntegrationsCommand.Execute(null);
        }
        else if (e.Args.Contains("--appdynamics-settings", StringComparer.OrdinalIgnoreCase))
        {
            mainViewModel.NavigateAppDynamicsIntegrationsCommand.Execute(null);
        }
        else if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
        {
            mainViewModel.NavigateSettingsCommand.Execute(null);
        }

        var window = new MainWindow
        {
            DataContext = mainViewModel
        };

        MainWindow = window;
        window.Show();

        var importArgumentIndex = Array.IndexOf(e.Args, "--import");
        if (importArgumentIndex >= 0 && importArgumentIndex + 1 < e.Args.Length)
        {
            _ = importViewModel.ImportFolderAsync(e.Args[importArgumentIndex + 1]);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.Write(
            ApplicationLogLevel.Information,
            "application_stopped",
            "O aplicativo foi encerrado.",
            properties: new Dictionary<string, object?>
            {
                ["exitCode"] = e.ApplicationExitCode
            });

        if (_settingsService is not null)
        {
            _settingsService.SettingsChanged -= OnSettingsChanged;
        }

        DispatcherUnhandledException -= OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
        _logger?.Dispose();
        _remoteHttpClientFactory?.Dispose();
        base.OnExit(e);
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (_settingsService is not null && _logger is not null)
        {
            _logger.Configure(_settingsService.Current.EffectiveLogging.ToFileLogOptions());
            _databaseService?.Configure(_settingsService.Current.EffectiveDatabase.ToOptions());
            _remoteHttpClientFactory?.Configure(_settingsService.Current.EffectiveRemoteHttp.ToOptions());
        }
    }

    private void OnDispatcherUnhandledException(
        object sender,
        System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) =>
        _logger?.Write(
            ApplicationLogLevel.Critical,
            "unhandled_ui_exception",
            "Uma exceção não tratada ocorreu na interface.",
            e.Exception);

    private void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) =>
        _logger?.Write(
            ApplicationLogLevel.Critical,
            "unhandled_application_exception",
            "Uma exceção não tratada encerrou o processo.",
            e.ExceptionObject as Exception,
            new Dictionary<string, object?>
            {
                ["isTerminating"] = e.IsTerminating
            });

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) =>
        _logger?.Write(
            ApplicationLogLevel.Error,
            "unobserved_task_exception",
            "Uma tarefa assíncrona terminou com exceção não observada.",
            e.Exception);
}
