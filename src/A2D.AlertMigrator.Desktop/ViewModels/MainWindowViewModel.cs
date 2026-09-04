using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.ViewModels.Alerting;
using A2D.AlertMigrator.Desktop.ViewModels.Help;
using A2D.AlertMigrator.Desktop.ViewModels.Importing;
using A2D.AlertMigrator.Desktop.ViewModels.Integrations;
using A2D.AlertMigrator.Desktop.ViewModels.Settings;

namespace A2D.AlertMigrator.Desktop.ViewModels;

public sealed class MainWindowViewModel : ObservableObject
{
    private object _currentPage;

    public MainWindowViewModel(
        ImportWorkspaceViewModel importWorkspace,
        DynatraceAlertingProfilesViewModel dynatraceAlertingProfiles,
        DynatraceAnomalyDetectorsViewModel dynatraceAnomalyDetectors,
        DynatraceDavisEventsViewModel dynatraceDavisEvents,
        DynatraceProblemsViewModel dynatraceProblems,
        SettingsViewModel settings,
        IntegrationSettingsViewModel dynatraceIntegrations,
        IntegrationSettingsViewModel appDynamicsIntegrations,
        DynatraceHelpViewModel dynatraceHelp,
        AppDynamicsHelpViewModel appDynamicsHelp)
    {
        ImportWorkspace = importWorkspace ?? throw new ArgumentNullException(nameof(importWorkspace));
        DynatraceAlertingProfiles = dynatraceAlertingProfiles ?? throw new ArgumentNullException(nameof(dynatraceAlertingProfiles));
        DynatraceAnomalyDetectors = dynatraceAnomalyDetectors ?? throw new ArgumentNullException(nameof(dynatraceAnomalyDetectors));
        DynatraceDavisEvents = dynatraceDavisEvents ?? throw new ArgumentNullException(nameof(dynatraceDavisEvents));
        DynatraceProblems = dynatraceProblems ?? throw new ArgumentNullException(nameof(dynatraceProblems));
        Settings = settings ?? throw new ArgumentNullException(nameof(settings));
        DynatraceIntegrations = dynatraceIntegrations ?? throw new ArgumentNullException(nameof(dynatraceIntegrations));
        AppDynamicsIntegrations = appDynamicsIntegrations ?? throw new ArgumentNullException(nameof(appDynamicsIntegrations));
        DynatraceHelp = dynatraceHelp ?? throw new ArgumentNullException(nameof(dynatraceHelp));
        AppDynamicsHelp = appDynamicsHelp ?? throw new ArgumentNullException(nameof(appDynamicsHelp));
        _currentPage = ImportWorkspace;
        NavigateImportCommand = new RelayCommand(() => CurrentPage = ImportWorkspace);
        NavigateDynatraceAlertingProfilesCommand = new RelayCommand(() => CurrentPage = DynatraceAlertingProfiles);
        NavigateDynatraceAnomalyDetectorsCommand = new RelayCommand(() => CurrentPage = DynatraceAnomalyDetectors);
        NavigateDynatraceDavisEventsCommand = new RelayCommand(() => CurrentPage = DynatraceDavisEvents);
        NavigateDynatraceProblemsCommand = new RelayCommand(() => CurrentPage = DynatraceProblems);
        NavigateSettingsCommand = new RelayCommand(() => CurrentPage = Settings);
        NavigateDynatraceIntegrationsCommand = new RelayCommand(() => CurrentPage = DynatraceIntegrations);
        NavigateAppDynamicsIntegrationsCommand = new RelayCommand(() => CurrentPage = AppDynamicsIntegrations);
        NavigateDynatraceHelpCommand = new RelayCommand(() => CurrentPage = DynatraceHelp);
        NavigateAppDynamicsHelpCommand = new RelayCommand(() => CurrentPage = AppDynamicsHelp);
    }

    public ImportWorkspaceViewModel ImportWorkspace { get; }

    public DynatraceAlertingProfilesViewModel DynatraceAlertingProfiles { get; }

    public DynatraceAnomalyDetectorsViewModel DynatraceAnomalyDetectors { get; }

    public DynatraceDavisEventsViewModel DynatraceDavisEvents { get; }

    public DynatraceProblemsViewModel DynatraceProblems { get; }

    public SettingsViewModel Settings { get; }

    public IntegrationSettingsViewModel DynatraceIntegrations { get; }

    public IntegrationSettingsViewModel AppDynamicsIntegrations { get; }

    public DynatraceHelpViewModel DynatraceHelp { get; }

    public AppDynamicsHelpViewModel AppDynamicsHelp { get; }

    public RelayCommand NavigateImportCommand { get; }

    public RelayCommand NavigateDynatraceAlertingProfilesCommand { get; }

    public RelayCommand NavigateDynatraceAnomalyDetectorsCommand { get; }

    public RelayCommand NavigateDynatraceDavisEventsCommand { get; }

    public RelayCommand NavigateDynatraceProblemsCommand { get; }

    public RelayCommand NavigateSettingsCommand { get; }

    public RelayCommand NavigateDynatraceIntegrationsCommand { get; }

    public RelayCommand NavigateAppDynamicsIntegrationsCommand { get; }

    public RelayCommand NavigateDynatraceHelpCommand { get; }

    public RelayCommand NavigateAppDynamicsHelpCommand { get; }

    public object CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (SetProperty(ref _currentPage, value))
            {
                OnPropertyChanged(nameof(IsImportPage));
                OnPropertyChanged(nameof(IsDynatraceAlertingProfilesPage));
                OnPropertyChanged(nameof(IsDynatraceAnomalyDetectorsPage));
                OnPropertyChanged(nameof(IsDynatraceDavisEventsPage));
                OnPropertyChanged(nameof(IsDynatraceProblemsPage));
                OnPropertyChanged(nameof(IsSettingsPage));
                OnPropertyChanged(nameof(IsDynatraceIntegrationsPage));
                OnPropertyChanged(nameof(IsAppDynamicsIntegrationsPage));
                OnPropertyChanged(nameof(IsDynatraceHelpPage));
                OnPropertyChanged(nameof(IsAppDynamicsHelpPage));
            }
        }
    }

    public bool IsImportPage => ReferenceEquals(CurrentPage, ImportWorkspace);

    public bool IsDynatraceAlertingProfilesPage => ReferenceEquals(CurrentPage, DynatraceAlertingProfiles);

    public bool IsDynatraceAnomalyDetectorsPage => ReferenceEquals(CurrentPage, DynatraceAnomalyDetectors);

    public bool IsDynatraceDavisEventsPage => ReferenceEquals(CurrentPage, DynatraceDavisEvents);

    public bool IsDynatraceProblemsPage => ReferenceEquals(CurrentPage, DynatraceProblems);

    public bool IsSettingsPage => ReferenceEquals(CurrentPage, Settings);

    public bool IsDynatraceIntegrationsPage => ReferenceEquals(CurrentPage, DynatraceIntegrations);

    public bool IsAppDynamicsIntegrationsPage => ReferenceEquals(CurrentPage, AppDynamicsIntegrations);

    public bool IsDynatraceHelpPage => ReferenceEquals(CurrentPage, DynatraceHelp);

    public bool IsAppDynamicsHelpPage => ReferenceEquals(CurrentPage, AppDynamicsHelp);
}
