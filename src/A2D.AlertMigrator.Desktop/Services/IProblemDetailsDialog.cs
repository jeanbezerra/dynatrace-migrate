using A2D.AlertMigrator.Application.Alerting;

namespace A2D.AlertMigrator.Desktop.Services;

public interface IProblemDetailsDialog
{
    void Show(StoredDynatraceProblem problem);
}
