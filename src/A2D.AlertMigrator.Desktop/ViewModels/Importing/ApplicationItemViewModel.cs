using A2D.AlertMigrator.Desktop.Common;
using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Desktop.ViewModels.Importing;

public sealed class ApplicationItemViewModel : ObservableObject
{
    private bool _shouldContinue;

    public ApplicationItemViewModel(ImportedApplication application)
    {
        Model = application ?? throw new ArgumentNullException(nameof(application));
        _shouldContinue = application.IsValid;
    }

    public ImportedApplication Model { get; }

    public string Name => Model.Document?.Application.Name ?? "Arquivo inválido";

    public string Id => Model.Document?.Application.Id ?? "—";

    public string SourcePath => Model.Source.RelativePath;

    public int RuleCount => Model.Document?.Rules.Count ?? 0;

    public int ErrorCount => Model.Diagnostics.Count(static item => item.Severity == ImportDiagnosticSeverity.Error);

    public string Status => Model.IsValid ? "Válida" : "Bloqueada";

    public string StatusSymbol => Model.IsValid ? "✓" : "✕";

    public bool CanContinue => Model.IsValid;

    public bool ShouldContinue
    {
        get => _shouldContinue;
        set
        {
            var acceptedValue = CanContinue && value;
            if (SetProperty(ref _shouldContinue, acceptedValue))
            {
                OnPropertyChanged(nameof(ContinueLabel));
            }
        }
    }

    public string ContinueLabel => ShouldContinue ? "Sim" : "Não";

    public bool Matches(string searchText)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return true;
        }

        var term = searchText.Trim();
        return Contains(Name, term)
            || Contains(Id, term)
            || Contains(SourcePath, term)
            || Contains(Status, term)
            || (Model.Document?.Rules.Any(rule => Contains(rule.Name, term) || Contains(rule.Id, term)) ?? false);
    }

    private static bool Contains(string value, string term) =>
        value.Contains(term, StringComparison.CurrentCultureIgnoreCase);
}
