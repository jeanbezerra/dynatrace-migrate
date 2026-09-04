namespace A2D.AlertMigrator.Application.Persistence;

public sealed record ImportExecutionRecord(
    string Id,
    DateTimeOffset StartedUtc,
    DateTimeOffset CompletedUtc,
    string Status,
    string SourceType,
    int Applications,
    int Rules,
    int Errors,
    int Warnings);
