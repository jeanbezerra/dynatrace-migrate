using A2D.AlertMigrator.Application.Importing;
using A2D.AlertMigrator.Domain.Importing;
using static A2D.AlertMigrator.Infrastructure.Importing.Json.JsonImportDiagnosticFactory;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Files;

internal sealed class JsonFileDiscovery : IJsonFileDiscovery
{
    public JsonFileDiscoveryResult Discover(
        JsonFolderImportOptions source,
        ImportLimits limits,
        CancellationToken cancellationToken)
    {
        var diagnostics = new List<ImportDiagnostic>();
        var rootPath = ValidateRoot(source.RootPath, diagnostics);
        if (rootPath is null)
        {
            return new JsonFileDiscoveryResult(null, [], diagnostics);
        }

        var files = EnumerateFiles(rootPath, source.Recursive, diagnostics, cancellationToken);
        if (files.Count > limits.MaxFiles)
        {
            diagnostics.Add(Error(
                "JSON_FILE_LIMIT_EXCEEDED",
                $"A pasta contém {files.Count} arquivos JSON; o limite é {limits.MaxFiles}."));
            files = files.Take(limits.MaxFiles).ToList();
        }

        if (files.Count == 0)
        {
            diagnostics.Add(new ImportDiagnostic(
                "JSON_NO_FILES",
                ImportDiagnosticSeverity.Warning,
                "Nenhum arquivo JSON foi encontrado na pasta selecionada."));
        }

        return new JsonFileDiscoveryResult(rootPath, files, diagnostics);
    }

    private static string? ValidateRoot(string root, ICollection<ImportDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            diagnostics.Add(Error("JSON_ROOT_INVALID", "A pasta de importação é obrigatória."));
            return null;
        }

        string rootPath;
        try
        {
            rootPath = Path.GetFullPath(root);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            diagnostics.Add(Error("JSON_ROOT_INVALID", $"Pasta inválida: {exception.Message}"));
            return null;
        }

        if (!Directory.Exists(rootPath))
        {
            diagnostics.Add(Error("JSON_ROOT_NOT_FOUND", $"Pasta não encontrada: {rootPath}"));
            return null;
        }

        try
        {
            if ((File.GetAttributes(rootPath) & FileAttributes.ReparsePoint) != 0)
            {
                diagnostics.Add(Error("JSON_REPARSE_POINT", "A pasta raiz não pode ser um link ou reparse point."));
                return null;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Error("JSON_ROOT_READ_ERROR", $"Não foi possível inspecionar a pasta: {exception.Message}"));
            return null;
        }

        return rootPath;
    }

    private static List<string> EnumerateFiles(
        string rootPath,
        bool recursive,
        ICollection<ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = pending.Pop();
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(directory).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    "JSON_DIRECTORY_READ_ERROR",
                    $"Não foi possível enumerar a pasta: {exception.Message}",
                    JsonPathBoundary.NormalizeRelativePath(rootPath, directory)));
                continue;
            }

            InspectEntries(rootPath, recursive, diagnostics, cancellationToken, entries, pending, files);
        }

        files.Sort((left, right) => StringComparer.Ordinal.Compare(
            JsonPathBoundary.NormalizeRelativePath(rootPath, left),
            JsonPathBoundary.NormalizeRelativePath(rootPath, right)));
        return files;
    }

    private static void InspectEntries(
        string rootPath,
        bool recursive,
        ICollection<ImportDiagnostic> diagnostics,
        CancellationToken cancellationToken,
        IEnumerable<string> entries,
        Stack<string> pending,
        ICollection<string> files)
    {
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileAttributes attributes;
            try
            {
                attributes = File.GetAttributes(entry);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Error(
                    "JSON_PATH_READ_ERROR",
                    $"Não foi possível inspecionar o caminho: {exception.Message}",
                    JsonPathBoundary.NormalizeRelativePath(rootPath, entry)));
                continue;
            }

            var isDirectory = (attributes & FileAttributes.Directory) != 0;
            var isReparsePoint = (attributes & FileAttributes.ReparsePoint) != 0;
            if (isDirectory)
            {
                if (recursive && isReparsePoint)
                {
                    diagnostics.Add(Error(
                        "JSON_REPARSE_POINT",
                        "Subpasta link/reparse point ignorada.",
                        JsonPathBoundary.NormalizeRelativePath(rootPath, entry)));
                }
                else if (recursive)
                {
                    pending.Push(entry);
                }

                continue;
            }

            if (string.Equals(Path.GetExtension(entry), ".json", StringComparison.OrdinalIgnoreCase))
            {
                files.Add(Path.GetFullPath(entry));
            }
        }
    }
}
