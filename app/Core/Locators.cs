using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaberBridge.Core;

public sealed record GameInstall(string Label, string Path)
{
    public bool HasCustomAvatars => File.Exists(System.IO.Path.Combine(Path, "Plugins", "CustomAvatar.dll"));
    public string AvatarsDir => System.IO.Path.Combine(Path, "CustomAvatars");
    public override string ToString() => Label;
}

public static class UnityLocator
{
    public const string WantedVersion = "2021.3.16f1";

    private static string Exe => OperatingSystem.IsWindows() ? "Unity.exe" : "Unity";

    public static string? Find(string? configured)
    {
        if (!string.IsNullOrEmpty(configured) && File.Exists(configured)) return configured;

        List<string> editors = new();
        foreach (string root in HubRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string exe = Path.Combine(dir, "Editor", Exe);
                if (File.Exists(exe)) editors.Add(exe);
            }
        }

        // exact version first, then any 2021.3.x (the CustomAvatars project targets 2021.3)
        return editors.FirstOrDefault(e => e.Contains(WantedVersion))
               ?? editors.Where(e => e.Contains("2021.3.")).OrderByDescending(e => e).FirstOrDefault();
    }

    public const string QuestVersion = "2019.3.15f1";

    /// <summary>Unity for Quest sabers: Qosmetics bundles are built with 2019.3, and it needs the Android module.</summary>
    public static string? FindQuest()
    {
        List<string> editors = new();
        foreach (string root in HubRoots())
        {
            if (!Directory.Exists(root)) continue;
            foreach (string dir in Directory.GetDirectories(root))
            {
                string exe = Path.Combine(dir, "Editor", Exe);
                if (File.Exists(exe)) editors.Add(exe);
            }
        }

        return editors.FirstOrDefault(e => e.Contains(QuestVersion)) ?? editors.Where(e => e.Contains("2019.3.")).OrderByDescending(e => e).FirstOrDefault();
    }

    public static bool HasAndroid(string exe) =>
        Directory.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "Data", "PlaybackEngines", "AndroidPlayer"));

    public static string? VersionOf(string exe) => Regex.Match(exe, @"20\d\d\.\d+\.\d+[abfp]\d+").Value is { Length: > 0 } v ? v : null;

    private static IEnumerable<string> HubRoots()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsWindows())
        {
            yield return @"C:\Program Files\Unity\Hub\Editor";
            yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
        }
        else
        {
            yield return Path.Combine(home, "Unity", "Hub", "Editor");
        }

        // a custom install location chosen in Unity Hub
        string config = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "UnityHub", "secondaryInstallPath.json")
            : Path.Combine(home, ".config", "UnityHub", "secondaryInstallPath.json");
        string? secondary = null;
        try
        {
            if (File.Exists(config)) secondary = JsonSerializer.Deserialize<string>(File.ReadAllText(config));
        }
        catch (Exception)
        {
            // ignore an unreadable Hub config
        }

        if (!string.IsNullOrWhiteSpace(secondary)) yield return secondary;
    }
}

public static class GameLocator
{
    public static List<GameInstall> FindAll(string? configured)
    {
        List<GameInstall> found = new();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        void Add(string label, string path)
        {
            if (!File.Exists(System.IO.Path.Combine(path, "Beat Saber.exe"))) return;
            string full = System.IO.Path.GetFullPath(path);
            if (found.Any(g => string.Equals(g.Path, full, StringComparison.OrdinalIgnoreCase))) return;
            found.Add(new GameInstall(label, full));
        }

        if (!string.IsNullOrEmpty(configured)) Add(LabelFor(configured), configured);

        // BSManager keeps one folder per game version
        string bsm = System.IO.Path.Combine(home, "BSManager", "BSInstances");
        if (Directory.Exists(bsm))
        {
            foreach (string dir in Directory.GetDirectories(bsm).OrderByDescending(d => d))
                Add($"BSManager {System.IO.Path.GetFileName(dir)}", dir);
        }

        foreach (string lib in SteamLibraries(home))
            Add("Steam", System.IO.Path.Combine(lib, "steamapps", "common", "Beat Saber"));

        // modded installs with Custom Avatars first
        return found.OrderByDescending(g => g.HasCustomAvatars).ToList();
    }

    public static string LabelFor(string path)
    {
        string name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\'));
        return path.Contains("BSInstances") ? $"BSManager {name}" : path.Contains("steamapps") ? "Steam" : name;
    }

    private static IEnumerable<string> SteamLibraries(string home)
    {
        List<string> roots = new();
        if (OperatingSystem.IsWindows())
        {
            roots.Add(@"C:\Program Files (x86)\Steam");
            if (Microsoft.Win32.Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) is string reg)
                roots.Add(reg.Replace('/', '\\'));
        }
        else
        {
            roots.Add(Path.Combine(home, ".steam", "steam"));
            roots.Add(Path.Combine(home, ".local", "share", "Steam"));
            roots.Add(Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam"));
        }

        HashSet<string> libs = new(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists))
        {
            libs.Add(root);
            string vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                libs.Add(m.Groups[1].Value.Replace(@"\\", @"\"));
        }

        return libs;
    }
}
