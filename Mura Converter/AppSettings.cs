using System.IO;
using System.Text.Json;

namespace Mura_Converter;

internal sealed class AppSettings
{
    private static readonly string SettingsDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MuraConverter");

    private static readonly string SettingsPath = Path.Combine(SettingsDir, "settings.json");

    public string OutputFolder { get; set; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Mura");

    public MediaKind Kind { get; set; } = MediaKind.Video;
    public AudioFormat AudioFormat { get; set; } = AudioFormat.Mp3;
    public int VideoHeight { get; set; } = 1080;
    public int AudioBitrate { get; set; } = 320;
    public DateTime LastUpdateCheck { get; set; } = DateTime.MinValue;

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath)) ?? new AppSettings();
        }
        catch
        {
            // Corrupt or old-format settings: fall back to defaults.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(SettingsDir);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this));
        }
        catch
        {
            // Settings are a convenience; never crash over them.
        }
    }
}
