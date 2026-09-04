using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using A2D.AlertMigrator.Application.Alerting;
using A2D.AlertMigrator.Application.Logging;
using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Desktop.Services;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceAlertingProfilesViewModel : ObservableObject
{
    private readonly SyncDynatraceAlertingProfilesUseCase _syncUseCase;
    private readonly IDynatraceAlertingProfileStore _store;
    private readonly IUserSettingsService _settingsService;
    private readonly IApplicationLogger _logger;
    private readonly IAlertingProfileDetailsDialog _detailsDialog;
    private CancellationTokenSource? _syncCancellation;
    private DynatraceTenantOptionViewModel? _selectedTenant;
    private DynatraceAlertingProfileItemViewModel? _selectedProfile;
    private string _searchText = string.Empty;
    private string _statusTitle = "Inventário local";
    private string _statusMessage = "Selecione um tenant para consultar os perfis já armazenados.";
    private bool _isBusy;
    private bool _hasError;
    private bool _requestAdminAccess = true;
    private bool _includeMissing;
    private string _lastSyncText = "Nunca sincronizado";
    private string _lastSyncDetail = "O banco local ainda não possui uma execução para este tenant.";
    private string _lastResultText = "Sem execução";

    public DynatraceAlertingProfilesViewModel(
        SyncDynatraceAlertingProfilesUseCase syncUseCase,
        IDynatraceAlertingProfileStore store,
        IUserSettingsService settingsService,
        IApplicationLogger logger,
        IAlertingProfileDetailsDialog detailsDialog)
    {
        _syncUseCase = syncUseCase ?? throw new ArgumentNullException(nameof(syncUseCase));
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _detailsDialog = detailsDialog ?? throw new ArgumentNullException(nameof(detailsDialog));

        ProfilesView = CollectionViewSource.GetDefaultView(Profiles);
        ProfilesView.Filter = item => item is DynatraceAlertingProfileItemViewModel profile
            && (IncludeMissing || profile.IsPresent)
            && profile.Matches(SearchText);
        ProfilesView.SortDescriptions.Add(new SortDescription(nameof(DynatraceAlertingProfileItemViewModel.Name), ListSortDirection.Ascending));

        SyncCommand = new AsyncRelayCommand(SynchronizeAsync, CanSynchronize);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        RefreshLocalCommand = new RelayCommand(LoadLocalProfiles, () => !IsBusy && SelectedTenant is not null);
        OpenDetailsCommand = new RelayCommand<DynatraceAlertingProfileItemViewModel>(
            profile => _detailsDialog.Show(profile.Model),
            _ => !IsBusy);
        _settingsService.SettingsChanged += OnSettingsChanged;
        ReloadTenants();
    }

    public ObservableCollection<DynatraceTenantOptionViewModel> Tenants { get; } = [];

    public ObservableCollection<DynatraceAlertingProfileItemViewModel> Profiles { get; } = [];

    public ICollectionView ProfilesView { get; }

    public AsyncRelayCommand SyncCommand { get; }

    public RelayCommand CancelCommand { get; }

    public RelayCommand RefreshLocalCommand { get; }

    public RelayCommand<DynatraceAlertingProfileItemViewModel> OpenDetailsCommand { get; }

    public DynatraceTenantOptionViewModel? SelectedTenant
    {
        get => _selectedTenant;
        set
        {
            if (SetProperty(ref _selectedTenant, value))
            {
                OnPropertyChanged(nameof(SelectedTenantAddress));
                OnPropertyChanged(nameof(SelectedTenantReadiness));
                LoadLocalProfiles();
                UpdateCommandStates();
            }
        }
    }

    public DynatraceAlertingProfileItemViewModel? SelectedProfile
    {
        get => _selectedProfile;
        set
        {
            if (SetProperty(ref _selectedProfile, value))
            {
                OpenDetailsCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string SelectedTenantAddress => SelectedTenant?.BaseAddress ?? "Nenhum tenant configurado";

    public string SelectedTenantReadiness => SelectedTenant?.ReadinessText ?? "Cadastre um ambiente em Configurações > Dynatrace.";

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ProfilesView.Refresh();
                OnPropertyChanged(nameof(VisibleCountText));
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
                OnPropertyChanged(nameof(InventoryModeText));
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
                ProfilesView.Refresh();
                if (SelectedProfile is not null && !ProfilesView.Contains(SelectedProfile))
                {
                    SelectedProfile = ProfilesView.Cast<DynatraceAlertingProfileItemViewModel>().FirstOrDefault();
                }

                OnPropertyChanged(nameof(VisibleCountText));
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

    public string LastSyncText
    {
        get => _lastSyncText;
        private set => SetProperty(ref _lastSyncText, value);
    }

    public string LastSyncDetail
    {
        get => _lastSyncDetail;
        private set => SetProperty(ref _lastSyncDetail, value);
    }

    public string LastResultText
    {
        get => _lastResultText;
        private set => SetProperty(ref _lastResultText, value);
    }

    public string InventoryModeText => RequestAdminAccess
        ? "Inventário completo. Requer settings:objects:read e settings:objects:admin."
        : "Inventário permitido. Objetos sem acesso não serão marcados como ausentes.";

    public string InventoryModeShortText => RequestAdminAccess ? "Completo" : "Permitido";

    public string ActiveProfileCountText => Profiles.Count(static profile => profile.IsPresent).ToString("N0");

    public string MissingProfileCountText
    {
        get
        {
            var missing = Profiles.Count(static profile => !profile.IsPresent);
            return missing == 0 ? "Nenhum ausente" : $"{missing:N0} ausentes";
        }
    }

    public string VisibleCountText => $"{ProfilesView.Cast<object>().Count():N0} exibidos · {Profiles.Count:N0} armazenados";

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
        StatusTitle = "Sincronizando perfis";
        StatusMessage = "Consultando todas as páginas da Settings API do Dynatrace…";
        var stopwatch = Stopwatch.StartNew();
        var source = SelectedTenant.CreateSource(RequestAdminAccess);

        _logger.Write(
            ApplicationLogLevel.Information,
            "dynatrace_alerting_profiles_sync_started",
            "O sincronismo dos perfis de alertas foi iniciado.",
            properties: new Dictionary<string, object?>
            {
                ["environment"] = source.Environment,
                ["tenantAlias"] = source.TenantAlias,
                ["requestAdminAccess"] = source.RequestAdminAccess
            });

        try
        {
            var result = await _syncUseCase.ExecuteAsync(source, _syncCancellation.Token);
            LoadLocalProfiles();
            StatusTitle = "Sincronismo concluído";
            StatusMessage = $"{result.Received:N0} recebidos · {result.Inserted:N0} novos · " +
                $"{result.Updated:N0} alterados · {result.Unchanged:N0} sem alteração · {result.Missing:N0} ausentes";
            _logger.Write(
                ApplicationLogLevel.Information,
                "dynatrace_alerting_profiles_sync_completed",
                "O sincronismo dos perfis de alertas foi concluído.",
                properties: new Dictionary<string, object?>
                {
                    ["runId"] = result.RunId,
                    ["environment"] = source.Environment,
                    ["received"] = result.Received,
                    ["inserted"] = result.Inserted,
                    ["updated"] = result.Updated,
                    ["unchanged"] = result.Unchanged,
                    ["missing"] = result.Missing,
                    ["completeInventory"] = result.IsCompleteInventory,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds
                });
        }
        catch (OperationCanceledException) when (_syncCancellation?.IsCancellationRequested == true)
        {
            StatusTitle = "Sincronismo cancelado";
            StatusMessage = "O inventário local não foi alterado.";
            _logger.Write(
                ApplicationLogLevel.Warning,
                "dynatrace_alerting_profiles_sync_cancelled",
                "O sincronismo dos perfis de alertas foi cancelado.",
                properties: new Dictionary<string, object?>
                {
                    ["environment"] = source.Environment,
                    ["elapsedMilliseconds"] = stopwatch.ElapsedMilliseconds
                });
        }
        catch (Exception exception)
        {
            HasError = true;
            StatusTitle = "Não foi possível sincronizar";
            StatusMessage = exception.Message;
            LoadLatestSync(source.TenantKey);
            _logger.Write(
                ApplicationLogLevel.Error,
                "dynatrace_alerting_profiles_sync_failed",
                "O sincronismo dos perfis de alertas falhou.",
                exception,
                new Dictionary<string, object?>
                {
                    ["environment"] = source.Environment,
                    ["requestAdminAccess"] = source.RequestAdminAccess,
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

    private void LoadLocalProfiles()
    {
        Profiles.Clear();
        SelectedProfile = null;
        if (SelectedTenant is null)
        {
            LastSyncText = "Nunca sincronizado";
            LastSyncDetail = "Cadastre um ambiente Dynatrace para iniciar.";
            LastResultText = "Sem execução";
            NotifyProfileSummaryChanged();
            OnPropertyChanged(nameof(VisibleCountText));
            return;
        }

        try
        {
            foreach (var profile in _store.GetProfiles(SelectedTenant.TenantKey, includeMissing: true))
            {
                Profiles.Add(new DynatraceAlertingProfileItemViewModel(profile));
            }

            ProfilesView.Refresh();
            SelectedProfile = ProfilesView.Cast<DynatraceAlertingProfileItemViewModel>().FirstOrDefault();
            LoadLatestSync(SelectedTenant.TenantKey);
            NotifyProfileSummaryChanged();
        }
        catch (Exception exception)
        {
            HasError = true;
            StatusTitle = "Banco local indisponível";
            StatusMessage = exception.Message;
        }
    }

    private void LoadLatestSync(string tenantKey)
    {
        var latest = _store.GetLatestSync(tenantKey);
        if (latest is null)
        {
            LastSyncText = "Nunca sincronizado";
            LastSyncDetail = "O banco local ainda não possui uma execução para este tenant.";
            LastResultText = "Sem execução";
            return;
        }

        var timestamp = (latest.CompletedAt ?? latest.StartedAt).ToLocalTime();
        LastSyncText = timestamp.ToString("dd/MM/yyyy HH:mm:ss");
        LastSyncDetail = latest.Status == "success"
            ? $"{latest.Received:N0} recebidos · {latest.Inserted:N0} novos · {latest.Updated:N0} alterados" +
              (latest.IsCompleteInventory ? " · inventário completo" : " · inventário permitido")
            : $"Falha: {latest.ErrorMessage}";
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
        OnPropertyChanged(nameof(SelectedTenantAddress));
        OnPropertyChanged(nameof(SelectedTenantReadiness));
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => ReloadTenants();

    private void UpdateCommandStates()
    {
        SyncCommand.RaiseCanExecuteChanged();
        CancelCommand.RaiseCanExecuteChanged();
        RefreshLocalCommand.RaiseCanExecuteChanged();
        OpenDetailsCommand.RaiseCanExecuteChanged();
    }

    private void NotifyProfileSummaryChanged()
    {
        OnPropertyChanged(nameof(VisibleCountText));
        OnPropertyChanged(nameof(ActiveProfileCountText));
        OnPropertyChanged(nameof(MissingProfileCountText));
    }
}
