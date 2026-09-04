using A2D.AlertMigrator.Application.Remote;

namespace A2D.AlertMigrator.Desktop.ViewModels.Settings;

public sealed record AuthenticationModeOption(RemoteAuthenticationMode Mode, string Label, string Description);
