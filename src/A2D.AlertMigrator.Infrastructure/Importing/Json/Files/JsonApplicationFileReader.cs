using System.Security.Cryptography;
using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;
using A2D.AlertMigrator.Infrastructure.Importing.Json.Parsing;
using static A2D.AlertMigrator.Infrastructure.Importing.Json.JsonImportDiagnosticFactory;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Files;

internal sealed class JsonApplicationFileReader : IJsonApplicationFileReader
{
    private readonly IJsonDocumentParser _parser;

    public JsonApplicationFileReader(IJsonDocumentParser parser)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
    }

    public async Task<ImportedApplication> ReadAsync(
        string rootPath,
        string filePath,
        ImportLimits limits,
        JsonEncodingOptions encoding,
        CancellationToken cancellationToken)
    {
        var relativePath = JsonPathBoundary.NormalizeRelativePath(rootPath, filePath);
        var emptySnapshot = new SourceSnapshot(relativePath, 0, DateTimeOffset.MinValue, string.Empty);

        if (!JsonPathBoundary.IsInsideRoot(rootPath, filePath))
        {
            return Invalid(emptySnapshot, Error(
                "JSON_PATH_OUTSIDE_ROOT",
                "O caminho do arquivo não está dentro da pasta selecionada.",
                relativePath));
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            fileInfo.Refresh();
            if (!fileInfo.Exists)
            {
                return Invalid(emptySnapshot, Error(
                    "JSON_FILE_NOT_FOUND",
                    "O arquivo não existe mais.",
                    relativePath));
            }

            var initialSnapshot = new SourceSnapshot(
                relativePath,
                fileInfo.Length,
                fileInfo.LastWriteTimeUtc,
                string.Empty);

            if ((fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return Invalid(initialSnapshot, Error(
                    "JSON_REPARSE_POINT",
                    "Links e reparse points não são importados.",
                    relativePath));
            }

            if (fileInfo.Length > limits.MaxFileBytes)
            {
                return Invalid(initialSnapshot, TooLarge(fileInfo.Length, limits.MaxFileBytes, relativePath));
            }

            return await ReadContentAsync(fileInfo, initialSnapshot, limits, encoding, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Invalid(emptySnapshot, Error(
                "JSON_FILE_READ_ERROR",
                $"Não foi possível ler o arquivo: {exception.Message}",
                relativePath));
        }
    }

    private async Task<ImportedApplication> ReadContentAsync(
        FileInfo fileInfo,
        SourceSnapshot initialSnapshot,
        ImportLimits limits,
        JsonEncodingOptions encoding,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            fileInfo.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        if (stream.Length > limits.MaxFileBytes || stream.Length > int.MaxValue)
        {
            return Invalid(initialSnapshot, TooLarge(stream.Length, limits.MaxFileBytes, initialSnapshot.RelativePath));
        }

        var content = new byte[(int)stream.Length];
        await stream.ReadExactlyAsync(content, cancellationToken).ConfigureAwait(false);
        var sha256 = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        var snapshot = initialSnapshot with
        {
            Length = content.Length,
            LastWriteTimeUtc = File.GetLastWriteTimeUtc(fileInfo.FullName),
            Sha256 = sha256
        };
        var normalizedContent = Utf8JsonContent.Normalize(content, encoding, initialSnapshot.RelativePath);
        if (normalizedContent.Diagnostic is not null)
        {
            return Invalid(snapshot, normalizedContent.Diagnostic);
        }

        var result = _parser.Parse(normalizedContent.Content.Span, initialSnapshot.RelativePath, limits);
        return new ImportedApplication(snapshot, result.Document, result.Diagnostics);
    }

    private static ImportDiagnostic TooLarge(long length, long limit, string relativePath) =>
        Error("JSON_FILE_TOO_LARGE", $"O arquivo possui {length} bytes; o limite é {limit}.", relativePath);

    private static ImportedApplication Invalid(SourceSnapshot source, params ImportDiagnostic[] diagnostics) =>
        new(source, null, diagnostics);
}
