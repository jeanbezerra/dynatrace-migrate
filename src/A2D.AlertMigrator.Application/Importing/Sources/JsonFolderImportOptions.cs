namespace A2D.AlertMigrator.Application.Importing;

public sealed record JsonFolderImportOptions(
    string RootPath,
    bool Recursive = false,
    ImportLimits? Limits = null,
    JsonEncodingOptions? Encoding = null);

public sealed record JsonEncodingOptions(
    Utf8BomPolicy BomPolicy = Utf8BomPolicy.Accept);

public enum Utf8BomPolicy
{
    Accept,
    Require,
    Reject
}
