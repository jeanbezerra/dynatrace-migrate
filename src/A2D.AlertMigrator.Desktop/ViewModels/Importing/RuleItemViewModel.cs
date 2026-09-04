using A2D.AlertMigrator.Domain.Importing;

namespace A2D.AlertMigrator.Desktop.ViewModels.Importing;

public sealed class RuleItemViewModel
{
    public RuleItemViewModel(CanonicalAlertRule rule)
    {
        Id = rule.Id;
        Name = rule.Name;
        Signal = rule.Signal switch
        {
            MetricSignalDefinition metric => metric.MetricKey,
            DqlSignalDefinition => "DQL personalizada",
            _ => rule.Signal.Kind
        };
        Detector = $"{rule.Detector.Model} · {rule.Detector.Condition}";
        Targets = rule.Targets.Count == 1 ? "1 serviço" : $"{rule.Targets.Count} serviços";
        Schedule = rule.Schedule.Mode;
        Status = rule.Enabled ? "Ativa" : "Desativada";
    }

    public string Id { get; }

    public string Name { get; }

    public string Signal { get; }

    public string Detector { get; }

    public string Targets { get; }

    public string Schedule { get; }

    public string Status { get; }
}
