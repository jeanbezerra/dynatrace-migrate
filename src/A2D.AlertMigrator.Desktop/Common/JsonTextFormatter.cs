using System.Text.Encodings.Web;
using System.Text.Json;

namespace A2D.AlertMigrator.Desktop.Common;

public static class JsonTextFormatter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Format(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(rawJson);
            return JsonSerializer.Serialize(document.RootElement, Options);
        }
        catch (JsonException)
        {
            return rawJson;
        }
    }
}
