using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Services;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceAnomalyDetectorsViewModel : ObservableObject
{
    private readonly SyncDynatraceAnomalyDetectorsUseCase _syncUseCase;
    private readonly IDynatraceAnomalyDetectorStore _store;
    private readonly IUserSettingsService _settingsService;
    private readonly IApplicationLogger _logger;
    private readonly IAnomalyDetectorDetailsDialog _detailsDialog;
    private CancellationTokenSource? _syncCancellation;
    private DynatraceTenantOptionViewModel? _selectedTenant;
    private DynatraceAnomalyDetectorItemViewModel? _selectedDetector;
    private string _searchText = string.Empty;
    private string _statusMessage = "Selecione um tenant e sincronize os detectores.";
    private string _lastSyncText = "Nunca sincronizado";
    private string _lastResultText = "Sem execução";
    private bool _isBusy;
    private bool _hasError;
    private bool _requestAdminAccess = true;
    private bool _includeMissing;
    private bool _onlyOutsideTimeseries;

    public DynatraceAnomalyDetectorsViewModel(
        SyncDynatraceAnomalyDetectorsUseCase syncUseCase,
        IDynatraceAnomalyDetectorStore store,
        IUserSettingsService settingsService,
        IApplicationLogger logger,
        IAnomalyDetectorDetailsDialog detailsDialog)
    {
        _syncUseCase = syncUseCase ?? throw new ArgumentNullException(nameof(syncUseCase));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _detailsDialog = detailsDialog ?? throw new ArgumentNullException(nameof(detailsDialog));

        DetectorsView = CollectionViewSource.GetDefaultView(Detectors);
        DetectorsView.Filter = FilterDetector;
        DetectorsView.SortDescriptions.Add(new SortDescription(
            nameof(DynatraceAnomalyDetectorItemViewModel.Title),
            ListSortDirection.Ascending));

        SyncCommand = new AsyncRelayCommand(SynchronizeAsync, CanSynchronize);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenDetailsCommand = new RelayCommand<DynatraceAnomalyDetectorItemViewModel>(
            detector => _detailsDialog.Show(detector.Model),
            _ => !IsBusy);
        _settingsService.SettingsChanged += OnSettingsChanged;
        ReloadTenants();
    }

    public ObservableCollection<DynatraceTenantOptionViewModel> Tenants { get; } = [];

    public ObservableCollection<DynatraceAnomalyDetectorItemViewModel> Detectors { get; } = [];

    public ICollectionView DetectorsView { get; }

    public AsyncRelayCommand SyncCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand<DynatraceAnomalyDetectorItemViewModel> OpenDetailsCommand { get; }

    public DynatraceTenantOptionViewModel? SelectedTenant
    {
        get => _selectedTenant;
        set
        {
            if (SetProperty(ref _selectedTenant, value))
            {
                LoadLocalDetectors();
                UpdateCommandStates();
            }
        }
    }

    public DynatraceAnomalyDetectorItemViewModel? SelectedDetector
    {
        get => _selectedDetector;
        set
        {
            if (SetProperty(ref _selectedDetector, value))
            {
                OpenDetailsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                RefreshView();
            }
        }
    }

    public bool RequestAdminAccess
    {
        get => _requestAdminAccess;
        set
        {
            if (SetProperty(ref _requestAdminAccess, value))
            {
                OnPropertyChanged(nameof(InventoryModeShortText));
            }
        }
    }

    public bool IncludeMissing
    {
        get => _includeMissing;
        set
        {
            if (SetProperty(ref _includeMissing, value))
            {
                RefreshView();
            }
        }
    }

    public bool OnlyOutsideTimeseries
    {
        get => _onlyOutsideTimeseries;
        set
        {
            if (SetProperty(ref _onlyOutsideTimeseries, value))
            {
                RefreshView();
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
                OnPropertyChanged(nameof(IsNotBusy));
                UpdateCommandStates();
            }
        }
    }

    public bool IsNotBusy => !IsBusy;

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

    public string LastSyncText
    {
        get => _lastSyncText;
        private set => SetProperty(ref _lastSyncText, value);
    }

    public string LastResultText
    {
        get => _lastResultText;
        private set => SetProperty(ref _lastResultText, value);
    }

    public string InventoryModeShortText => RequestAdminAccess ? "Completo" : "Permitido";

    public string ActiveDetectorCountText => Detectors.Count(
        static detector => detector.IsPresent && detector.IsEnabled).ToString("N0");

    public string OutsideTimeseriesCountText
    {
        get
        {
            var count = Detectors.Count(static detector => detector.IsPresent && !detector.UsesTimeseries);
            return count == 0 ? "Todos no padrão" : $"{count:N0} fora do padrão";
        }
    }

    public string VisibleCountText => $"{DetectorsView.Cast<object>().Count():N0} de {Detectors.Count:N0}";

    private bool FilterDetector(object item) =>
        item is DynatraceAnomalyDetectorItemViewModel detector
        && (IncludeMissing || detector.IsPresent)
        && (!OnlyOutsideTimeseries || !detector.UsesTimeseries)
        && detector.Matches(SearchText);

    private bool CanSynchronize() => !IsBusy && SelectedTenant?.IsReady == true;

    private async Task SynchronizeAsync()
    {
        if (SelectedTenant is null)
        {
            return;
        }

        _syncCancellation?.Dispose();
        _syncCancellation = new CancellationTokenSource();
        IsBusy = true;
        HasError = false;
        StatusMessage = "Lendo todas as páginas da Settings API…";
        var stopwatch = Stopwatch.StartNew();
        var source = SelectedTenant.CreateAnomalyDetectorSource(RequestAdminAccess);

        _logger.Write(
            ApplicationLogLevel.Information,
            "dynatrace_anomaly_detectors_sync_started",
            "O sincronismo dos detectores de anomalia foi iniciado.",
            properties: new Dictionary<string, object?>
            {
                ["environment"] = source.Environment,
                ["tenantAlias"] = source.TenantAlias,
                ["requestAdminAccess"] = source.RequestAdminAccess
            });

        try
        {
            var result = await _syncUseCase.ExecuteAsync(source, _syncCancellation.Token);
            LoadLocalDetectors();
            StatusMessage = $"{result.Received:N0} lidos · {result.Inserted:N0} novos · " +
                $"{result.Updated:N0} alterados · {result.Missing:N0} ausentes";
            _logger.Write(
                ApplicationLogLevel.Information,
                "dynatrace_anomaly_detectors_sync_completed",
                "O sincronismo dos detectores de anomalia foi concluído.",
                properties: new Dictionary<string, object?>
                {
                    ["runId"] = result.RunId,
                    ["environment"] = source.Environment,
                    ["received"] = result.Received,
                    ["inserted"] = result.Inserted,
                    ["updated"] = result.Updated,
                    ["unchanged"] = result.Unchanged,
                    ["missing"] = result.Missing,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds
                });
        }
        catch (OperationCanceledException) when (_syncCancellation?.IsCancellationRequested == true)
        {
            StatusMessage = "Sincronismo cancelado. O inventário local não foi alterado.";
            _logger.Write(
                ApplicationLogLevel.Warning,
                "dynatrace_anomaly_detectors_sync_cancelled",
                "O sincronismo dos detectores de anomalia foi cancelado.");
        }
        catch (Exception exception)
        {
            HasError = true;
            StatusMessage = exception.Message;
            LoadLatestSync(source.TenantKey);
            _logger.Write(
                ApplicationLogLevel.Error,
                "dynatrace_anomaly_detectors_sync_failed",
                "O sincronismo dos detectores de anomalia falhou.",
                exception,
                new Dictionary<string, object?>
                {
                    ["environment"] = source.Environment,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds
                });
        }
        finally
        {
            IsBusy = false;
            _syncCancellation?.Dispose();
            _syncCancellation = null;
        }
    }

    private void Cancel() => _syncCancellation?.Cancel();

    private void LoadLocalDetectors()
    {
        Detectors.Clear();
        SelectedDetector = null;
        if (SelectedTenant is null)
        {
            LastSyncText = "Nunca sincronizado";
            LastResultText = "Sem execução";
            NotifySummaryChanged();
            return;
        }

        try
        {
            foreach (var detector in _store.GetAnomalyDetectors(SelectedTenant.TenantKey, includeMissing: true))
            {
                Detectors.Add(new DynatraceAnomalyDetectorItemViewModel(detector));
            }

            DetectorsView.Refresh();
            SelectedDetector = DetectorsView.Cast<DynatraceAnomalyDetectorItemViewModel>().FirstOrDefault();
            LoadLatestSync(SelectedTenant.TenantKey);
            NotifySummaryChanged();
        }
        catch (Exception exception)
        {
            HasError = true;
            StatusMessage = exception.Message;
        }
    }

    private void LoadLatestSync(string tenantKey)
    {
        var latest = _store.GetLatestAnomalyDetectorSync(tenantKey);
        if (latest is null)
        {
            LastSyncText = "Nunca sincronizado";
            LastResultText = "Sem execução";
            return;
        }

        LastSyncText = (latest.CompletedAt ?? latest.StartedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        LastResultText = latest.Status == "success"
            ? $"{latest.Received:N0} lidos · {latest.Inserted + latest.Updated:N0} mudanças"
            : "Falhou";
    }

    private void ReloadTenants()
    {
        var selectedKey = SelectedTenant?.TenantKey;
        Tenants.Clear();
        foreach (var settings in _settingsService.Current.EffectiveIntegrations.EffectiveDynatrace.Environments)
        {
            Tenants.Add(new DynatraceTenantOptionViewModel(settings));
        }

        SelectedTenant = Tenants.FirstOrDefault(tenant => tenant.TenantKey == selectedKey)
            ?? Tenants.FirstOrDefault(static tenant => tenant.IsReady)
            ?? Tenants.FirstOrDefault();
    }

    private void RefreshView()
    {
        DetectorsView.Refresh();
        if (SelectedDetector is not null && !DetectorsView.Contains(SelectedDetector))
        {
            SelectedDetector = DetectorsView.Cast<DynatraceAnomalyDetectorItemViewModel>().FirstOrDefault();
        }

        OnPropertyChanged(nameof(VisibleCountText));
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(ActiveDetectorCountText));
        OnPropertyChanged(nameof(OutsideTimeseriesCountText));
        OnPropertyChanged(nameof(VisibleCountText));
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => ReloadTenants();

    private void UpdateCommandStates()
    {
        SyncCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OpenDetailsCommand.RaiseCanExecuteChanged();
    }
}
