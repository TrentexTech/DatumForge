using System.Text.Json;
using System.Text.Json.Serialization;

namespace DatumForge.Infrastructure.Persistence;

public sealed class UserSettingsStore
{
    public const string SettingsFileName = "settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public UserSettingsStore(string? dataDirectoryPath = null)
    {
        DataDirectoryPath = Path.GetFullPath(dataDirectoryPath ?? GetDefaultDataDirectory());
        SettingsFilePath = Path.Combine(DataDirectoryPath, SettingsFileName);
    }

    public string DataDirectoryPath { get; }
    public string SettingsFilePath { get; }
    public string? RecoveryNotice { get; private set; }

    public UserSettings Load()
    {
        RecoveryNotice = null;
        if (!File.Exists(SettingsFilePath)) return new UserSettings();

        try
        {
            using var stream = File.OpenRead(SettingsFilePath);
            var settings = JsonSerializer.Deserialize<UserSettings>(stream, JsonOptions)
                           ?? throw new InvalidDataException("The settings file contains no data.");
            return ValidateAndNormalize(settings);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            return RecoverInvalidSettings();
        }
    }

    public async Task<UserSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        RecoveryNotice = null;
        if (!File.Exists(SettingsFilePath)) return new UserSettings();

        try
        {
            await using var stream = File.OpenRead(SettingsFilePath);
            var settings = await JsonSerializer.DeserializeAsync<UserSettings>(stream, JsonOptions, cancellationToken)
                           ?? throw new InvalidDataException("The settings file contains no data.");
            return ValidateAndNormalize(settings);
        }
        catch (Exception exception) when (exception is JsonException or InvalidDataException or NotSupportedException)
        {
            return RecoverInvalidSettings();
        }
    }

    public async Task SaveAsync(UserSettings settings, CancellationToken cancellationToken = default)
    {
        PrepareForSave(settings);
        var temporaryPath = SettingsFilePath + ".tmp";
        await using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        {
            await JsonSerializer.SerializeAsync(stream, settings, JsonOptions, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        File.Move(temporaryPath, SettingsFilePath, true);
    }

    public void Save(UserSettings settings)
    {
        PrepareForSave(settings);
        var temporaryPath = SettingsFilePath + ".tmp";
        using (var stream = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            JsonSerializer.Serialize(stream, settings, JsonOptions);
            stream.Flush(true);
        }
        File.Move(temporaryPath, SettingsFilePath, true);
    }

    private void PrepareForSave(UserSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.FormatVersion = UserSettings.CurrentFormatVersion;
        settings.Normalize();
        Directory.CreateDirectory(DataDirectoryPath);
    }

    private static UserSettings ValidateAndNormalize(UserSettings settings)
    {
        if (settings.FormatVersion > UserSettings.CurrentFormatVersion)
            throw new InvalidDataException($"Settings format {settings.FormatVersion} is newer than the supported format {UserSettings.CurrentFormatVersion}.");
        settings.Normalize();
        return settings;
    }

    private UserSettings RecoverInvalidSettings()
    {
        var recoveryPath = Path.Combine(
            DataDirectoryPath,
            $"settings.invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmssfff}.json");
        File.Move(SettingsFilePath, recoveryPath, false);
        RecoveryNotice = $"Invalid user settings were preserved as {Path.GetFileName(recoveryPath)}.";
        return new UserSettings();
    }

    public static string GetDefaultDataDirectory()
    {
        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
            localApplicationData = AppContext.BaseDirectory;
        return Path.Combine(localApplicationData, "DatumForge");
    }
}
