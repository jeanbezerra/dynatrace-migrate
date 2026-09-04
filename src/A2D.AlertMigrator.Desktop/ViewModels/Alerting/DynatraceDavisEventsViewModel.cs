using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Services;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceDavisEventsViewModel : ObservableObject
{
    private const int ResultLimit = 5_000;
    private readonly SyncDynatraceDavisEventsUseCase _syncUseCase;
    private readonly IDynatraceDavisEventStore _store;
    private readonly IUserSettingsService _settingsService;
    private readonly IApplicationLogger _logger;
    private readonly IDavisEventDetailsDialog _detailsDialog;
    private CancellationTokenSource? _syncCancellation;
    private DynatraceTenantOptionViewModel? _selectedTenant;
    private DynatraceDavisEventItemViewModel? _selectedEvent;
    private AlertingTimeRangeOption _selectedTimeRange;
    private string _searchText = string.Empty;
    private string _statusMessage = "Selecione um tenant e consulte os Davis Events.";
    private string _lastSyncText = "Nunca consultado";
    private string _lastResultText = "Sem execução";
    private bool _isBusy;
    private bool _hasError;
    private bool _activeOnly;
    private bool _highPriorityOnly;

    public DynatraceDavisEventsViewModel(
        SyncDynatraceDavisEventsUseCase syncUseCase,
        IDynatraceDavisEventStore store,
        IUserSettingsService settingsService,
        IApplicationLogger logger,
        IDavisEventDetailsDialog detailsDialog)
    {
        _syncUseCase = syncUseCase ?? throw new ArgumentNullException(nameof(syncUseCase));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _detailsDialog = detailsDialog ?? throw new ArgumentNullException(nameof(detailsDialog));

        TimeRanges =
        [
            new AlertingTimeRangeOption(24, "Últimas 24 horas"),
            new AlertingTimeRangeOption(168, "Últimos 7 dias"),
            new AlertingTimeRangeOption(720, "Últimos 30 dias")
        ];
        _selectedTimeRange = TimeRanges[0];
        EventsView = CollectionViewSource.GetDefaultView(Events);
        EventsView.Filter = FilterEvent;
        EventsView.SortDescriptions.Add(new SortDescription(
            nameof(DynatraceDavisEventItemViewModel.EffectiveStart),
            ListSortDirection.Descending));

        SyncCommand = new AsyncRelayCommand(SynchronizeAsync, CanSynchronize);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        OpenDetailsCommand = new RelayCommand<DynatraceDavisEventItemViewModel>(
            item => _detailsDialog.Show(item.Model),
            _ => !IsBusy);
        _settingsService.SettingsChanged += OnSettingsChanged;
        ReloadTenants();
    }

    public ObservableCollection<DynatraceTenantOptionViewModel> Tenants { get; } = [];

    public ObservableCollection<DynatraceDavisEventItemViewModel> Events { get; } = [];

    public IReadOnlyList<AlertingTimeRangeOption> TimeRanges { get; }

    public ICollectionView EventsView { get; }

    public AsyncRelayCommand SyncCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand<DynatraceDavisEventItemViewModel> OpenDetailsCommand { get; }

    public DynatraceTenantOptionViewModel? SelectedTenant
    {
        get => _selectedTenant;
        set
        {
            if (SetProperty(ref _selectedTenant, value))
            {
                LoadLocalEvents();
                UpdateCommandStates();
            }
        }
    }

    public DynatraceDavisEventItemViewModel? SelectedEvent
    {
        get => _selectedEvent;
        set
        {
            if (SetProperty(ref _selectedEvent, value))
            {
                OpenDetailsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public AlertingTimeRangeOption SelectedTimeRange
    {
        get => _selectedTimeRange;
        set
        {
            if (value is not null && SetProperty(ref _selectedTimeRange, value))
            {
                RefreshView();
                NotifySummaryChanged();
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

    public bool ActiveOnly
    {
        get => _activeOnly;
        set
        {
            if (SetProperty(ref _activeOnly, value))
            {
                RefreshView();
            }
        }
    }

    public bool HighPriorityOnly
    {
        get => _highPriorityOnly;
        set
        {
            if (SetProperty(ref _highPriorityOnly, value))
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

    public string ActiveCountText => EventsInSelectedRange().Count(static item => item.IsActive).ToString("N0");

    public string HighPriorityCountText => EventsInSelectedRange().Count(static item => item.IsHighPriority).ToString("N0");

    public string MaintenanceCountText => EventsInSelectedRange().Count(static item => item.Model.IsUnderMaintenance).ToString("N0");

    public string VisibleCountText => $"{EventsView.Cast<object>().Count():N0} de {Events.Count:N0}";

    private bool FilterEvent(object item)
    {
        if (item is not DynatraceDavisEventItemViewModel davisEvent
            || ActiveOnly && !davisEvent.IsActive
            || HighPriorityOnly && !davisEvent.IsHighPriority
            || !davisEvent.Matches(SearchText))
        {
            return false;
        }

        return IsWithinSelectedRange(davisEvent);
    }

    private bool CanSynchronize() => !IsBusy && SelectedTenant?.IsDavisEventReady == true;

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
        StatusMessage = "Executando DQL e aguardando o resultado do Grail…";
        var stopwatch = Stopwatch.StartNew();
        var source = SelectedTenant.CreateDavisEventSource(SelectedTimeRange.Hours, ResultLimit);

        _logger.Write(
            ApplicationLogLevel.Information,
            "dynatrace_davis_events_sync_started",
            "A consulta dos Davis Events foi iniciada.",
            properties: new Dictionary<string, object?>
            {
                ["environment"] = source.Environment,
                ["tenantAlias"] = source.TenantAlias,
                ["lookbackHours"] = source.LookbackHours,
                ["resultLimit"] = source.ResultLimit
            });

        try
        {
            var result = await _syncUseCase.ExecuteAsync(source, _syncCancellation.Token);
            LoadLocalEvents();
            StatusMessage = result.LimitReached
                ? $"{result.Received:N0} eventos recebidos. O limite foi atingido; reduza o período para evitar cortes."
                : $"{result.Received:N0} recebidos · {result.Inserted:N0} novos · {result.Updated:N0} atualizados";
            _logger.Write(
                result.LimitReached ? ApplicationLogLevel.Warning : ApplicationLogLevel.Information,
                "dynatrace_davis_events_sync_completed",
                "A consulta dos Davis Events foi concluída.",
                properties: new Dictionary<string, object?>
                {
                    ["runId"] = result.RunId,
                    ["environment"] = source.Environment,
                    ["received"] = result.Received,
                    ["inserted"] = result.Inserted,
                    ["updated"] = result.Updated,
                    ["unchanged"] = result.Unchanged,
                    ["limitReached"] = result.LimitReached,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds
                });
        }
        catch (OperationCanceledException) when (_syncCancellation?.IsCancellationRequested == true)
        {
            StatusMessage = "Consulta cancelada. O histórico local não foi alterado.";
            _logger.Write(
                ApplicationLogLevel.Warning,
                "dynatrace_davis_events_sync_cancelled",
                "A consulta dos Davis Events foi cancelada.");
        }
        catch (Exception exception)
        {
            HasError = true;
            StatusMessage = exception.Message;
            LoadLatestSync(source.TenantKey);
            _logger.Write(
                ApplicationLogLevel.Error,
                "dynatrace_davis_events_sync_failed",
                "A consulta dos Davis Events falhou.",
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

    private void LoadLocalEvents()
    {
        Events.Clear();
        SelectedEvent = null;
        if (SelectedTenant is null)
        {
            LastSyncText = "Nunca consultado";
            LastResultText = "Sem execução";
            NotifySummaryChanged();
            return;
        }

        try
        {
            foreach (var item in _store.GetDavisEvents(SelectedTenant.TenantKey))
            {
                Events.Add(new DynatraceDavisEventItemViewModel(item));
            }

            EventsView.Refresh();
            SelectedEvent = EventsView.Cast<DynatraceDavisEventItemViewModel>().FirstOrDefault();
            LoadLatestSync(SelectedTenant.TenantKey);
            NotifySummaryChanged();
            HasError = false;
            StatusMessage = Events.Count == 0
                ? "Nenhum evento armazenado para este tenant. Clique em Consultar."
                : "Histórico local carregado. Clique em Consultar para atualizar.";
            if (!SelectedTenant.IsDavisEventReady)
            {
                HasError = true;
                StatusMessage = SelectedTenant.DavisEventReadinessText;
            }
        }
        catch (Exception exception)
        {
            HasError = true;
            StatusMessage = exception.Message;
        }
    }

    private void LoadLatestSync(string tenantKey)
    {
        var latest = _store.GetLatestDavisEventSync(tenantKey);
        if (latest is null)
        {
            LastSyncText = "Nunca consultado";
            LastResultText = "Sem execução";
            return;
        }

        LastSyncText = (latest.CompletedAt ?? latest.StartedAt).ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");
        LastResultText = latest.Status == "success"
            ? latest.LimitReached
                ? $"{latest.Received:N0} lidos · limite atingido"
                : $"{latest.Received:N0} lidos · {latest.Inserted + latest.Updated:N0} mudanças"
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

        SelectedTenant = Tenants.FirstOrDefault(item => item.TenantKey == selectedKey)
            ?? Tenants.FirstOrDefault(static item => item.IsDavisEventReady)
            ?? Tenants.FirstOrDefault();
    }

    private void RefreshView()
    {
        EventsView.Refresh();
        if (SelectedEvent is not null && !EventsView.Contains(SelectedEvent))
        {
            SelectedEvent = EventsView.Cast<DynatraceDavisEventItemViewModel>().FirstOrDefault();
        }

        OnPropertyChanged(nameof(VisibleCountText));
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(ActiveCountText));
        OnPropertyChanged(nameof(HighPriorityCountText));
        OnPropertyChanged(nameof(MaintenanceCountText));
        OnPropertyChanged(nameof(VisibleCountText));
    }

    private IEnumerable<DynatraceDavisEventItemViewModel> EventsInSelectedRange() =>
        Events.Where(IsWithinSelectedRange);

    private bool IsWithinSelectedRange(DynatraceDavisEventItemViewModel item)
    {
        var cutoff = DateTimeOffset.UtcNow.AddHours(-SelectedTimeRange.Hours);
        return item.EffectiveStart is null || item.EffectiveStart >= cutoff;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => ReloadTenants();

    private void UpdateCommandStates()
    {
        SyncCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        OpenDetailsCommand.RaiseCanExecuteChanged();
    }
}
