using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Application.Persistence;
using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Services;
using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Desktop.ViewModels.Importing;

public sealed class ImportWorkspaceViewModel : ObservableObject
{
    private readonly ImportJsonFolderUseCase _importUseCase;
    private readonly IFolderPicker _folderPicker;
    private readonly IUserSettingsService _settingsService;
    private readonly IApplicationLogger _logger;
    private readonly ILocalDatabaseService _databaseService;
    private IReadOnlyList<ImportDiagnostic> _batchDiagnostics = [];
    private CancellationTokenSource? _importCancellation;
    private string _folderPath = string.Empty;
    private bool _recursive;
    private bool _isBusy;
    private ApplicationItemViewModel? _selectedApplication;
    private int _totalApplications;
    private int _validApplications;
    private int _ruleCount;
    private int _errorCount;
    private int _warningCount;
    private int _continueCount;
    private string _searchText = string.Empty;
    private string _statusTitle = "Pronto para importar";
    private string _statusMessage = "Selecione uma pasta contendo um JSON por aplicação.";

    public ImportWorkspaceViewModel(
        ImportJsonFolderUseCase importUseCase,
        IFolderPicker folderPicker,
        IUserSettingsService settingsService,
        IApplicationLogger logger,
        ILocalDatabaseService databaseService)
    {
        _importUseCase = importUseCase ?? throw new ArgumentNullException(nameof(importUseCase));
        _folderPicker = folderPicker ?? throw new ArgumentNullException(nameof(folderPicker));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _settingsService.SettingsChanged += OnSettingsChanged;
        _recursive = _settingsService.Current.RecursiveByDefault;

        ApplicationsView = CollectionViewSource.GetDefaultView(Applications);
        ApplicationsView.Filter = item => item is ApplicationItemViewModel application && application.Matches(SearchText);

        BrowseCommand = new AsyncRelayCommand(BrowseAndImportAsync, () => !IsBusy);
        ReimportCommand = new AsyncRelayCommand(ImportCurrentFolderAsync, () => !IsBusy && Directory.Exists(FolderPath));
        CancelCommand = new RelayCommand(CancelImport, () => IsBusy);
        SelectAllVisibleCommand = new RelayCommand(
            () => SetContinuationForVisible(true),
            () => !IsBusy && ApplicationsView.Cast<ApplicationItemViewModel>().Any(static item => item.CanContinue && !item.ShouldContinue));
        ClearAllVisibleCommand = new RelayCommand(
            () => SetContinuationForVisible(false),
            () => !IsBusy && ApplicationsView.Cast<ApplicationItemViewModel>().Any(static item => item.ShouldContinue));

        AddActivity("INFO", "Aplicação iniciada. Nenhuma conexão externa foi aberta.");
    }

    public ObservableCollection<ApplicationItemViewModel> Applications { get; } = [];

    public ObservableCollection<RuleItemViewModel> Rules { get; } = [];

    public ObservableCollection<DiagnosticItemViewModel> Diagnostics { get; } = [];

    public ObservableCollection<ActivityItemViewModel> Activity { get; } = [];

    public ICollectionView ApplicationsView { get; }

    public AsyncRelayCommand BrowseCommand { get; }

    public AsyncRelayCommand ReimportCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand SelectAllVisibleCommand { get; }

    public RelayCommand ClearAllVisibleCommand { get; }

    public string FolderPath
    {
        get => _folderPath;
        private set
        {
            if (SetProperty(ref _folderPath, value))
            {
                OnPropertyChanged(nameof(FolderDisplay));
                UpdateCommandStates();
            }
        }
    }

    public string FolderDisplay => string.IsNullOrWhiteSpace(FolderPath)
        ? "Nenhuma pasta selecionada"
        : FolderPath;

    public bool Recursive
    {
        get => _recursive;
        set => SetProperty(ref _recursive, value);
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (!SetProperty(ref _searchText, value))
            {
                return;
            }

            ApplicationsView.Refresh();
            OnPropertyChanged(nameof(VisibleApplicationsText));
            if (SelectedApplication is not null && !SelectedApplication.Matches(SearchText))
            {
                SelectedApplication = ApplicationsView.Cast<ApplicationItemViewModel>().FirstOrDefault();
            }

            UpdateCommandStates();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                UpdateCommandStates();
            }
        }
    }

    public ApplicationItemViewModel? SelectedApplication
    {
        get => _selectedApplication;
        set
        {
            if (SetProperty(ref _selectedApplication, value))
            {
                RebuildDetails();
            }
        }
    }

    public int TotalApplications
    {
        get => _totalApplications;
        private set => SetProperty(ref _totalApplications, value);
    }

    public int ValidApplications
    {
        get => _validApplications;
        private set => SetProperty(ref _validApplications, value);
    }

    public int RuleCount
    {
        get => _ruleCount;
        private set => SetProperty(ref _ruleCount, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set => SetProperty(ref _errorCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetProperty(ref _warningCount, value);
    }

    public int ContinueCount
    {
        get => _continueCount;
        private set
        {
            if (SetProperty(ref _continueCount, value))
            {
                OnPropertyChanged(nameof(VisibleApplicationsText));
            }
        }
    }

    public string VisibleApplicationsText =>
        $"{ApplicationsView.Cast<object>().Count()} exibidas · {ContinueCount} para continuar";

    public string StatusTitle
    {
        get => _statusTitle;
        private set => SetProperty(ref _statusTitle, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task ImportFolderAsync(string folderPath)
    {
        try
        {
            FolderPath = Path.GetFullPath(folderPath);
            await ImportCurrentFolderAsync();
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            StatusTitle = "Pasta inválida";
            StatusMessage = exception.Message;
            AddActivity("ERROR", exception.Message);
            _logger.Write(
                ApplicationLogLevel.Warning,
                "import_path_rejected",
                "A pasta informada para importação é inválida.",
                exception);
        }
    }

    private async Task BrowseAndImportAsync()
    {
        var selectedFolder = _folderPicker.PickFolder(FolderPath, "Selecione a pasta com os arquivos JSON");
        if (selectedFolder is null)
        {
            return;
        }

        FolderPath = selectedFolder;
        await ImportCurrentFolderAsync();
    }

    private async Task ImportCurrentFolderAsync()
    {
        if (!Directory.Exists(FolderPath))
        {
            StatusTitle = "Pasta indisponível";
            StatusMessage = "Selecione novamente a pasta de origem.";
            _logger.Write(
                ApplicationLogLevel.Warning,
                "import_directory_unavailable",
                "A pasta de origem não está disponível.");
            return;
        }

        _importCancellation?.Dispose();
        _importCancellation = new CancellationTokenSource();
        IsBusy = true;
        StatusTitle = "Importando regras";
        StatusMessage = "Leitura e validação em andamento…";
        AddActivity("INFO", $"Importação iniciada: {FolderPath}");
        var operationId = Guid.NewGuid().ToString("N");
        var startedUtc = DateTimeOffset.UtcNow;
        var stopwatch = Stopwatch.StartNew();
        _logger.Write(
            ApplicationLogLevel.Information,
            "import_started",
            "A leitura e a validação dos arquivos foram iniciadas.",
            properties: new Dictionary<string, object?>
            {
                ["operationId"] = operationId,
                ["recursive"] = Recursive
            });

        try
        {
            var settings = _settingsService.Current;
            var batch = await _importUseCase.ExecuteAsync(
                new JsonFolderImportOptions(
                    FolderPath,
                    Recursive,
                    settings.ToImportLimits(),
                    new JsonEncodingOptions(settings.Utf8BomPolicy)),
                _importCancellation.Token);
            ApplyBatch(batch);
            _logger.Write(
                batch.IsValid ? ApplicationLogLevel.Information : ApplicationLogLevel.Warning,
                "import_completed",
                "A leitura e a validação dos arquivos foram concluídas.",
                properties: new Dictionary<string, object?>
                {
                    ["operationId"] = operationId,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds,
                    ["applications"] = TotalApplications,
                    ["validApplications"] = ValidApplications,
                    ["rules"] = RuleCount,
                    ["errors"] = ErrorCount,
                    ["warnings"] = WarningCount
                });
            _databaseService.RecordImport(new ImportExecutionRecord(
                operationId,
                startedUtc,
                DateTimeOffset.UtcNow,
                batch.IsValid ? "completed" : "completed_with_issues",
                "json_folder",
                TotalApplications,
                RuleCount,
                ErrorCount,
                WarningCount));
        }
        catch (OperationCanceledException)
        {
            StatusTitle = "Importação cancelada";
            StatusMessage = "Nenhum dado da execução cancelada foi aplicado.";
            AddActivity("WARN", "Importação cancelada pelo usuário.");
            _logger.Write(
                ApplicationLogLevel.Warning,
                "import_cancelled",
                "A importação foi cancelada pelo usuário.",
                properties: new Dictionary<string, object?>
                {
                    ["operationId"] = operationId,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds
                });
            _databaseService.RecordImport(new ImportExecutionRecord(
                operationId,
                startedUtc,
                DateTimeOffset.UtcNow,
                "cancelled",
                "json_folder",
                Applications: 0,
                Rules: 0,
                Errors: 0,
                Warnings: 0));
        }
        catch (Exception exception)
        {
            StatusTitle = "Falha inesperada";
            StatusMessage = exception.Message;
            AddActivity("ERROR", exception.Message);
            _logger.Write(
                ApplicationLogLevel.Error,
                "import_failed",
                "A importação terminou com uma falha inesperada.",
                exception,
                new Dictionary<string, object?>
                {
                    ["operationId"] = operationId,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds
                });
            _databaseService.RecordImport(new ImportExecutionRecord(
                operationId,
                startedUtc,
                DateTimeOffset.UtcNow,
                "failed",
                "json_folder",
                Applications: 0,
                Rules: 0,
                Errors: 1,
                Warnings: 0));
        }
        finally
        {
            IsBusy = false;
            _importCancellation?.Dispose();
            _importCancellation = null;
        }
    }

    private void ApplyBatch(ImportBatch batch)
    {
        _batchDiagnostics = batch.Diagnostics;
        foreach (var application in Applications)
        {
            application.PropertyChanged -= OnApplicationPropertyChanged;
        }

        Applications.Clear();
        foreach (var application in batch.Applications)
        {
            var item = new ApplicationItemViewModel(application);
            item.PropertyChanged += OnApplicationPropertyChanged;
            Applications.Add(item);
        }

        ApplicationsView.Refresh();

        TotalApplications = batch.Applications.Count;
        ValidApplications = batch.Applications.Count(static application => application.IsValid);
        RuleCount = batch.RuleCount;

        var allDiagnostics = batch.Diagnostics
            .Concat(batch.Applications.SelectMany(static application => application.Diagnostics))
            .ToArray();
        ErrorCount = allDiagnostics.Count(static diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Error);
        WarningCount = allDiagnostics.Count(static diagnostic => diagnostic.Severity == ImportDiagnosticSeverity.Warning);
        ContinueCount = Applications.Count(static application => application.ShouldContinue);
        OnPropertyChanged(nameof(VisibleApplicationsText));

        SelectedApplication = ApplicationsView.Cast<ApplicationItemViewModel>().FirstOrDefault();
        if (SelectedApplication is null)
        {
            RebuildDetails();
        }

        StatusTitle = batch.IsValid ? "Importação concluída" : "Importação concluída com pendências";
        StatusMessage = $"{ValidApplications} de {TotalApplications} aplicações válidas · {RuleCount} regras · {ErrorCount} erros";
        AddActivity(batch.IsValid ? "INFO" : "WARN", StatusMessage);
        UpdateCommandStates();
    }

    private void RebuildDetails()
    {
        Rules.Clear();
        Diagnostics.Clear();

        foreach (var diagnostic in _batchDiagnostics)
        {
            Diagnostics.Add(new DiagnosticItemViewModel(diagnostic));
        }

        if (SelectedApplication is null)
        {
            return;
        }

        var application = SelectedApplication.Model;
        if (application.Document is not null)
        {
            foreach (var rule in application.Document.Rules)
            {
                Rules.Add(new RuleItemViewModel(rule));
            }
        }

        foreach (var diagnostic in application.Diagnostics)
        {
            Diagnostics.Add(new DiagnosticItemViewModel(diagnostic));
        }
    }

    private void CancelImport() => _importCancellation?.Cancel();

    private void SetContinuationForVisible(bool shouldContinue)
    {
        var changed = 0;
        foreach (var application in ApplicationsView.Cast<ApplicationItemViewModel>())
        {
            if (!application.CanContinue || application.ShouldContinue == shouldContinue)
            {
                continue;
            }

            application.ShouldContinue = shouldContinue;
            changed++;
        }

        if (changed > 0)
        {
            AddActivity("INFO", $"Marcação alterada em {changed} aplicações visíveis: {(shouldContinue ? "Sim" : "Não")}.");
        }
    }

    private void OnApplicationPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ApplicationItemViewModel.ShouldContinue))
        {
            return;
        }

        ContinueCount = Applications.Count(static application => application.ShouldContinue);
        UpdateCommandStates();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FolderPath))
        {
            Recursive = _settingsService.Current.RecursiveByDefault;
        }
    }

    private void AddActivity(string level, string message)
    {
        Activity.Insert(0, new ActivityItemViewModel(DateTimeOffset.Now, level, message));
        while (Activity.Count > 200)
        {
            Activity.RemoveAt(Activity.Count - 1);
        }
    }

    private void UpdateCommandStates()
    {
        BrowseCommand.RaiseCanExecuteChanged();
        ReimportCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        SelectAllVisibleCommand.RaiseCanExecuteChanged();
        ClearAllVisibleCommand.RaiseCanExecuteChanged();
    }
}
