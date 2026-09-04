namespace A2D.AlertMigrator.Domain.Importing;

public sealed record SourceSnapshot(
    string RelativePath,
    long Length,
    DateTimeOffset LastWriteTimeUtc,
    string Sha256);
