namespace A2D.AlertMigrator.Desktop.ViewModels.Importing;

public sealed record ActivityItemViewModel(
    DateTimeOffset Timestamp,
    string Level,
    string Message)
{
    public string Time => Timestamp.ToLocalTime().ToString("HH:mm:ss");
}
