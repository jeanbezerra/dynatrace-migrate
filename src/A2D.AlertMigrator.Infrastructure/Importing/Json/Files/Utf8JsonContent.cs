using System.Text;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;
using static A2D.AlertMigrator.Infrastructure.Importing.Json.JsonImportDiagnosticFactory;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Files;

internal static class Utf8JsonContent
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static Utf8JsonContentResult Normalize(
        ReadOnlyMemory<byte> content,
        JsonEncodingOptions options,
        string relativePath)
    {
        var hasBom = content.Span.StartsWith(Utf8Bom);
        if (options.BomPolicy == Utf8BomPolicy.Require && !hasBom)
        {
            return Invalid("JSON_UTF8_BOM_REQUIRED", "O arquivo deve possuir BOM UTF-8.", relativePath);
        }

        if (options.BomPolicy == Utf8BomPolicy.Reject && hasBom)
        {
            return Invalid("JSON_UTF8_BOM_REJECTED", "O arquivo possui BOM UTF-8, mas a configuração exige UTF-8 sem BOM.", relativePath);
        }

        var normalized = hasBom ? content[Utf8Bom.Length..] : content;
        try
        {
            _ = StrictUtf8.GetCharCount(normalized.Span);
        }
        catch (DecoderFallbackException)
        {
            return Invalid("JSON_ENCODING_INVALID", "O arquivo não contém texto UTF-8 válido.", relativePath);
        }

        return new Utf8JsonContentResult(normalized, null);
    }

    private static Utf8JsonContentResult Invalid(string code, string message, string relativePath) =>
        new(ReadOnlyMemory<byte>.Empty, Error(code, message, relativePath));
}

internal sealed record Utf8JsonContentResult(
    ReadOnlyMemory<byte> Content,
    ImportDiagnostic? Diagnostic);
