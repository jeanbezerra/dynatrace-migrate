using A2D.AlertMigrator.Application.Alerting;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceDavisEventItemViewModel
{
    public DynatraceDavisEventItemViewModel(StoredDynatraceDavisEvent item)
    {
        Model = item ?? throw new ArgumentNullException(nameof(item));
    }

    public StoredDynatraceDavisEvent Model { get; }

    public string Name => Model.Name;

    public string Category => EmptyAsDash(Model.Category);

    public string Provider => EmptyAsDash(Model.Provider);

    public string EntityText => string.IsNullOrWhiteSpace(Model.SourceEntityId)
        ? "—"
        : string.IsNullOrWhiteSpace(Model.SourceEntityType)
            ? Model.SourceEntityId
            : $"{Model.SourceEntityType} · {Model.SourceEntityId}";

    public bool IsActive => Model.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);

    public bool IsHighPriority => Model.Severity is 1 or 2;

    public int Severity => Model.Severity ?? 0;

    public string StatusText => IsActive ? "Ativo" : "Encerrado";

    public string SeverityText => Model.Severity switch
    {
        1 => "1 · Crítica",
        2 => "2 · Alta",
        3 => "3 · Média",
        4 => "4 · Baixa",
        5 => "5 · Informativa",
        int value => value.ToString(),
        _ => "—"
    };

    public string StartedText => (Model.Start ?? Model.Timestamp)?.ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss") ?? "Não informado";

    public DateTimeOffset? EffectiveStart => Model.Start ?? Model.Timestamp;

    public bool Matches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Category.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Provider.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || EntityText.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Model.AlertGroup.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Model.EventType.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Model.EventId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || Model.SettingsObjectId.Contains(searchText, StringComparison.OrdinalIgnoreCase);
    }

    private static string EmptyAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
