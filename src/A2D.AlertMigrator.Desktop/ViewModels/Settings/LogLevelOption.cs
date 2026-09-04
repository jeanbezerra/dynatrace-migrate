using A2D.AlertMigrator.Application.Logging;

namespace A2D.AlertMigrator.Desktop.ViewModels.Settings;

public sealed record LogLevelOption(
    ApplicationLogLevel Level,
    string Label,
    string Description);
