using System.Text.Json;

namespace AudioMix;

/// <summary>
/// Alle Einstellungen der Anwendung. Werden als JSON unter
/// %AppData%\AudioMix\settings.json gespeichert.
/// </summary>
public sealed class AppSettings
{
    public bool Enabled { get; set; } = true;

    public string? DeviceAId { get; set; }
    public string? DeviceAName { get; set; }   // nur zur Anzeige, falls das Gerät gerade nicht verbunden ist
    public string? DeviceBId { get; set; }
    public string? DeviceBName { get; set; }

    public bool SyncMute { get; set; } = true;
    public bool StartWithWindows { get; set; }

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AudioMix", "settings.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Datei defekt oder nicht lesbar -> mit Standardwerten starten
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);

            // Erst in eine Temp-Datei schreiben und dann ersetzen, damit die Datei nie halb geschrieben ist.
            string tempPath = FilePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(this, JsonOptions));
            File.Move(tempPath, FilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Speichern ist nicht kritisch für die Funktion – im Zweifel einfach weiterlaufen.
        }
    }
}