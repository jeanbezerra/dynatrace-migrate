using A2D.AlertMigrator.Application.Importing;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Parsing;

internal interface IJsonDocumentParser
{
    ParseResult Parse(ReadOnlySpan<byte> utf8Json, string relativePath, ImportLimits limits);
}
