namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Files;

internal static class JsonPathBoundary
{
    public static bool IsInsideRoot(string rootPath, string candidatePath)
    {
        var relative = Path.GetRelativePath(rootPath, Path.GetFullPath(candidatePath));
        return !Path.IsPathRooted(relative)
            && !string.Equals(relative, "..", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
            && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }

    public static string NormalizeRelativePath(string rootPath, string path) =>
        Path.GetRelativePath(rootPath, path).Replace(Path.DirectorySeparatorChar, '/');
}
