using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Desktop.ViewModels.Importing;

public sealed class DiagnosticItemViewModel
{
    public DiagnosticItemViewModel(ImportDiagnostic diagnostic)
    {
        Severity = diagnostic.Severity switch
        {
            ImportDiagnosticSeverity.Error => "Erro",
            ImportDiagnosticSeverity.Warning => "Aviso",
            _ => "Informação"
        };
        Code = diagnostic.Code;
        Message = diagnostic.Message;
        Source = diagnostic.RelativePath ?? "Lote";
        Location = diagnostic.JsonPointer
            ?? (diagnostic.ByteOffset is not null ? $"byte {diagnostic.ByteOffset}" : "—");
    }

    public string Severity { get; }

    public string Code { get; }

    public string Message { get; }

    public string Source { get; }

    public string Location { get; }
}
