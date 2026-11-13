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

    /// <summary>What the user keeps: Documents\SaberBridge\PC\Avatars, Quest\Sabers, Quest\Notes.</summary>
    public static string UserRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SaberBridge");
    public static string PcAvatars => Path.Combine(UserRoot, "PC", "Avatars");
    public static string QuestRoot => Path.Combine(UserRoot, "Quest");
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
    public int LastTab { get; set; }
    public int LastQuestKind { get; set; }

    /// <summary>Unity has to run as a hidden normal editor here (no batch-mode license); see UnityRunner.</summary>
    public bool UnityHiddenWindow { get; set; }

    /// <summary>The hidden editor could not work on a desktop of its own here; it runs with hidden windows instead.</summary>
    public bool UnityOwnDesktopFailed { get; set; }

    /// <summary>The first-run setup window was shown once (it opens by itself on the very first start).</summary>
    public bool FirstRunGuideShown { get; set; }

    /// <summary>Unity Hub.exe picked by hand, when the app cannot find it by itself.</summary>
    public string? UnityHubPath { get; set; }

    /// <summary>Update check: when GitHub was last asked, the newest version it named, and one the user chose to skip.</summary>
    public DateTime? UpdateCheckedAt { get; set; }
    public string? UpdateLatest { get; set; }
    public string? UpdateSkipped { get; set; }

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
