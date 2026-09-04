using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;
using A2D.AlertMigrator.Infrastructure.Importing.Json.Files;
using A2D.AlertMigrator.Infrastructure.Importing.Json.Parsing;
using A2D.AlertMigrator.Infrastructure.Importing.Json.Validation;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json;

public sealed class JsonFolderImportAdapter : IImportSourceAdapter<JsonFolderImportOptions>
{
    private readonly IJsonFileDiscovery _fileDiscovery;
    private readonly IJsonApplicationFileReader _fileReader;
    private readonly IJsonImportBatchValidator _batchValidator;

    public JsonFolderImportAdapter()
        : this(
            new JsonFileDiscovery(),
            new JsonApplicationFileReader(new CanonicalJsonV1Parser()),
            new JsonImportBatchValidator())
    {
    }

    internal JsonFolderImportAdapter(
        IJsonFileDiscovery fileDiscovery,
        IJsonApplicationFileReader fileReader,
        IJsonImportBatchValidator batchValidator)
    {
        _fileDiscovery = fileDiscovery ?? throw new ArgumentNullException(nameof(fileDiscovery));
        _fileReader = fileReader ?? throw new ArgumentNullException(nameof(fileReader));
        _batchValidator = batchValidator ?? throw new ArgumentNullException(nameof(batchValidator));
    }

    public async Task<ImportBatch> ReadAsync(
        JsonFolderImportOptions source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var limits = source.Limits ?? new ImportLimits();
        var encoding = source.Encoding ?? new JsonEncodingOptions();
        limits.EnsureValid();

        var discovery = _fileDiscovery.Discover(source, limits, cancellationToken);
        if (discovery.RootPath is null)
        {
            return new ImportBatch([], discovery.Diagnostics);
        }

        var applications = new List<ImportedApplication>(discovery.Files.Count);
        foreach (var filePath in discovery.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            applications.Add(await _fileReader
                .ReadAsync(discovery.RootPath, filePath, limits, encoding, cancellationToken)
                .ConfigureAwait(false));
        }

        return _batchValidator.Validate(applications, discovery.Diagnostics, limits);
    }
}
