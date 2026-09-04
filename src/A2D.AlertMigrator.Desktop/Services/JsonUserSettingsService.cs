using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using A2D.AlertMigrator.Desktop.Configuration;

namespace A2D.AlertMigrator.Desktop.Services;

public sealed class JsonUserSettingsService : IUserSettingsService
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8WithoutBom = new(false, true);
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public JsonUserSettingsService(string? storagePath = null)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            StoragePath = Path.Combine(localData, "A2DAlertMigrator", "settings.json");
        }
        else
        {
            StoragePath = Path.GetFullPath(storagePath);
        }

        Current = Load();
    }

    public UserSettings Current { get; private set; }

    public event EventHandler? SettingsChanged;

    public string StoragePath { get; }

    public void Save(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings = settings.Normalize();
        settings.ToImportLimits().EnsureValid();
        if (!Enum.IsDefined(settings.Utf8BomPolicy))
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Política de BOM UTF-8 inválida.");
        }

        var directory = Path.GetDirectoryName(StoragePath)
            ?? throw new InvalidOperationException("Pasta de configurações inválida.");
        Directory.CreateDirectory(directory);

        var json = JsonSerializer.Serialize(settings, SerializerOptions);
        var bytes = StrictUtf8WithoutBom.GetBytes(json);
        var temporaryPath = Path.Combine(directory, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllBytes(temporaryPath, bytes);
            File.Move(temporaryPath, StoragePath, overwrite: true);
            Current = settings;
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    public void Reset() => Save(UserSettings.Default);

    private UserSettings Load()
    {
        try
        {
            if (!File.Exists(StoragePath))
            {
                return UserSettings.Default;
            }

            var bytes = File.ReadAllBytes(StoragePath).AsMemory();
            if (bytes.Span.StartsWith(Utf8Bom))
            {
                bytes = bytes[Utf8Bom.Length..];
            }

            var json = StrictUtf8WithoutBom.GetString(bytes.Span);
            var settings = JsonSerializer.Deserialize<UserSettings>(json, SerializerOptions)?.Normalize();
            if (settings is not null && !Enum.IsDefined(settings.Utf8BomPolicy))
            {
                return UserSettings.Default;
            }

            settings?.ToImportLimits().EnsureValid();
            return settings ?? UserSettings.Default;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or JsonException
            or DecoderFallbackException
            or ArgumentException
            or NotSupportedException
            or OverflowException)
        {
            return UserSettings.Default;
        }
    }
}
