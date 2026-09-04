using System.Text.Json;

namespace A2D.AlertMigrator.Infrastructure.Importing.Json.Parsing;

internal static class JsonDuplicatePropertyScanner
{
    public static IReadOnlyList<DuplicateProperty> Scan(ReadOnlySpan<byte> utf8Json, int maxDepth)
    {
        var duplicates = new List<DuplicateProperty>();
        var scopes = new Stack<HashSet<string>?>(maxDepth);
        var reader = new Utf8JsonReader(
            utf8Json,
            new JsonReaderOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = maxDepth
            });

        while (reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject:
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    break;
                case JsonTokenType.StartArray:
                    scopes.Push(null);
                    break;
                case JsonTokenType.EndObject:
                case JsonTokenType.EndArray:
                    scopes.Pop();
                    break;
                case JsonTokenType.PropertyName when scopes.TryPeek(out var properties) && properties is not null:
                    var propertyName = reader.GetString() ?? string.Empty;
                    if (!properties.Add(propertyName))
                    {
                        duplicates.Add(new DuplicateProperty(propertyName, reader.TokenStartIndex));
                    }

                    break;
            }
        }

        return duplicates;
    }

    public static (long Line, long ByteInLine) Locate(ReadOnlySpan<byte> utf8Json, long offset)
    {
        long line = 0;
        long byteInLine = 0;
        var limit = Math.Min(offset, utf8Json.Length);

        for (var index = 0; index < limit; index++)
        {
            if (utf8Json[index] == (byte)'\n')
            {
                line++;
                byteInLine = 0;
            }
            else
            {
                byteInLine++;
            }
        }

        return (line, byteInLine);
    }
}

internal sealed record DuplicateProperty(string Name, long ByteOffset);
