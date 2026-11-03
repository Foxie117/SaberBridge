using System.Text.Json;

namespace BSAvatarConverter.Core;

public static class AppPaths
{
    // %LOCALAPPDATA%\BSAvatarConverter on Windows, ~/.local/share/BSAvatarConverter on Linux
    public static string Data { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BSAvatarConverter");
    public static string UnityProject => Path.Combine(Data, "unity");
    public static string Output => Path.Combine(Data, "output");
    public static string Temp => Path.Combine(Data, "temp");
    public static string Logs => Path.Combine(Data, "logs");
    public static string SettingsFile => Path.Combine(Data, "settings.json");
}

public sealed class Settings
{
    public string? UnityPath { get; set; }
    public string? GamePath { get; set; }
    public bool AutoInstall { get; set; } = true;
    public string? Language { get; set; }

    public static Settings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(AppPaths.SettingsFile)) ?? new Settings();
        }
        catch (Exception)
        {
            // a broken settings file just falls back to defaults
        }

        return new Settings();
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Data);
        File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
