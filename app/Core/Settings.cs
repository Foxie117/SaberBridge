using System.Text.Json;

namespace SaberBridge.Core;

public static class AppPaths
{
    // %LOCALAPPDATA%\SaberBridge on Windows, ~/.local/share/SaberBridge on Linux
    // SABERBRIDGE_DATA points the app at another data folder (used for clean demo screenshots)
    public static string Data { get; } = Environment.GetEnvironmentVariable("SABERBRIDGE_DATA") is { Length: > 0 } custom
        ? custom
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaberBridge");
    public static string UnityProject => Path.Combine(Data, "unity");
    public static string QuestProject => Path.Combine(Data, "unity-quest");
    public static string Output => Path.Combine(Data, "output");
    public static string Temp => Path.Combine(Data, "temp");
    public static string Logs => Path.Combine(Data, "logs");
    public static string SettingsFile => Path.Combine(Data, "settings.json");

    /// <summary>The app was called BSAvatarConverter before 0.3; move its data (settings, previews, the imported Unity project) once.</summary>
    public static void MigrateOldData()
    {
        string old = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BSAvatarConverter");
        try
        {
            if (Directory.Exists(old) && !Directory.Exists(Data)) Directory.Move(old, Data);
        }
        catch (Exception)
        {
            // a locked or cross-drive folder just means starting fresh; the old one stays where it is
        }
    }
}

public sealed class Settings
{
    public string? UnityPath { get; set; }
    public string? GamePath { get; set; }
    public bool AutoInstall { get; set; } = true;
    public string? Language { get; set; }
    public string? QuestOutputDir { get; set; }

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
