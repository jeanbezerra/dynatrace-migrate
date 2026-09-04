using A2D.AlertMigrator.Application.Importing;

namespace A2D.AlertMigrator.Desktop.ViewModels.Settings;

public sealed record BomPolicyOption(
    Utf8BomPolicy Policy,
    string Label,
    string Description);
