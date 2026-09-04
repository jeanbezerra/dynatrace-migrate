using A2D.AlertMigrator.Application.Alerting;

namespace A2D.AlertMigrator.Desktop.ViewModels.Alerting;

public sealed class DynatraceProblemItemViewModel
{
    public DynatraceProblemItemViewModel(StoredDynatraceProblem problem)
    {
        Model = problem ?? throw new ArgumentNullException(nameof(problem));
    }

    public StoredDynatraceProblem Model { get; }

    public string DisplayId => Model.DisplayId;

    public string Name => Model.Name;

    public string Category => EmptyAsDash(Model.Category);

    public bool IsActive => Model.Status.Equals("ACTIVE", StringComparison.OrdinalIgnoreCase);

    public bool HasRootCause => !string.IsNullOrWhiteSpace(Model.RootCauseEntityId)
        || !string.IsNullOrWhiteSpace(Model.RootCauseEntityName);

    public string StatusText => IsActive ? "Ativo" : "Encerrado";

    public string RootCauseText => !string.IsNullOrWhiteSpace(Model.RootCauseEntityName)
        ? Model.RootCauseEntityName
        : !string.IsNullOrWhiteSpace(Model.RootCauseEntityId)
            ? Model.RootCauseEntityId
            : "Em análise";

    public string ImpactText => Model.AffectedUsersCount > 0
        ? $"{Model.AffectedUsersCount:N0} usuários"
        : Model.AffectedEntityCount > 0
            ? $"{Model.AffectedEntityCount:N0} entidades"
            : "Não informado";

    public string StartedText => (Model.Start ?? Model.Timestamp)?.ToLocalTime()
        .ToString("dd/MM/yyyy HH:mm:ss") ?? "Não informado";

    public DateTimeOffset? EffectiveStart => Model.Start ?? Model.Timestamp;

    public bool Matches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        return DisplayId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || Name.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Category.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || RootCauseText.Contains(searchText, StringComparison.CurrentCultureIgnoreCase)
            || Model.EventId.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || Model.AffectedEntityIds.Any(id => id.Contains(searchText, StringComparison.OrdinalIgnoreCase))
            || Model.AffectedServiceIds.Any(id => id.Contains(searchText, StringComparison.OrdinalIgnoreCase));
    }

    private static string EmptyAsDash(string value) => string.IsNullOrWhiteSpace(value) ? "—" : value;
}
