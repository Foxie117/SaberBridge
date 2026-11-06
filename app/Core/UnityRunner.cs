using System.IO.Compression;
using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace SaberBridge.Core;

/// <summary>Keeps the converter's Unity project (shipped inside the app) unpacked and ready.</summary>
public static class ProjectSetup
{
    // set after the first Unity run that got as far as running the converter; an interrupted import does not count
    private static string ImportedMarker => Path.Combine(AppPaths.UnityProject, ".bsac-imported");

    public static bool IsFirstImport => !File.Exists(ImportedMarker);

    public static void MarkImported() => File.WriteAllText(ImportedMarker, DateTime.Now.ToString("O"));

    public static void Ensure(GameInstall game)
    {
        string proj = AppPaths.UnityProject;
        Extract("unity.zip", proj);

        // FinalIK and DynamicBone are paid assets: they are not shipped, the game's own copies are used
        string libs = Path.Combine(proj, "Assets", "Libraries");
        foreach (string dll in new[] { "FinalIK.dll", "DynamicBone.dll" })
        {
            string src = new[] { "Libs", "Plugins", Path.Combine("IPA", "Pending", "Libs") }
                .Select(d => Path.Combine(game.Path, d, dll)).FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException($"{dll} was not found in {game.Path}. Install Custom Avatars in this game copy first (BSManager → Mods).");
            string dst = Path.Combine(libs, dll);
            if (!File.Exists(dst) || new FileInfo(dst).Length != new FileInfo(src).Length) File.Copy(src, dst, true);
        }

        string import = Path.Combine(proj, "Assets", "VRC_Import");
        if (Directory.Exists(import)) Directory.Delete(import, true);
        Directory.CreateDirectory(import);
    }

    /// <summary>The Unity 2019.3 project that turns PC sabers into Quest .whacker files.</summary>
    public static void EnsureQuest() => Extract("unity-quest.zip", AppPaths.QuestProject);

    private static string QuestImportedMarker => Path.Combine(AppPaths.QuestProject, ".sb-imported");

    public static bool IsQuestImported => File.Exists(QuestImportedMarker);

    public static void MarkQuestImported() => File.WriteAllText(QuestImportedMarker, DateTime.Now.ToString("O"));

    private static void Extract(string resource, string proj)
    {
        using Stream zip = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                           ?? throw new InvalidOperationException($"{resource} is missing from the app");
        string hash = Convert.ToHexString(SHA1.HashData(zip));
        zip.Position = 0;

        string stamp = Path.Combine(proj, ".bsac-stamp");
        if (!File.Exists(stamp) || File.ReadAllText(stamp) != hash)
        {
            // replace the sources but keep Library/, so an app update does not trigger a full re-import
            foreach (string d in new[] { "Assets", "Packages", "ProjectSettings" })
            {
                string p = Path.Combine(proj, d);
                if (Directory.Exists(p)) Directory.Delete(p, true);
            }

            Directory.CreateDirectory(proj);
            using (ZipArchive archive = new(zip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue;
                    string dst = Path.Combine(proj, InputScanner.SafeRelative(entry.FullName));
                    Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                    entry.ExtractToFile(dst, true);
                }
            }

            File.WriteAllText(stamp, hash);
        }
    }
}

public sealed class UnityResult
{
    public int ExitCode { get; set; }
    public List<string> Lines { get; } = new();
    public string? Problem { get; set; }
}

/// <summary>Runs Unity in batch mode and streams the converter's "[VRM2BS]" log lines.</summary>
public static class UnityRunner
{
    private static readonly (string Needle, string Message)[] KnownProblems =
    {
        ("No valid Unity Editor license", "Unity has no active license. Open Unity Hub, sign in and add a free Personal license."),
        ("License is not active", "Unity has no active license. Open Unity Hub, sign in and add a free Personal license."),
        ("another Unity instance is running with this project", "The converter's Unity project is open in another Unity window. Close it and try again."),
        ("Multiple Unity instances cannot open the same project", "The converter's Unity project is open in another Unity window. Close it and try again."),
        ("build target was unsupported", "Unity cannot build for Windows. In Unity Hub add the module \"Windows Build Support (Mono)\" to this editor."),
        ("error CS", "The converter scripts failed to compile (see the log)."),
    };

    // one Unity at a time: two editors cannot open the same project
    private static readonly SemaphoreSlim Lock = new(1, 1);

    public static async Task<UnityResult> RunAsync(string unityExe, string method, IEnumerable<string> extraArgs, Action<string> onLine, Action<string> onRaw, CancellationToken ct, string? projectPath = null)
    {
        await Lock.WaitAsync(ct);
        try
        {
            return await RunLockedAsync(unityExe, method, extraArgs, onLine, onRaw, ct, projectPath ?? AppPaths.UnityProject);
        }
        finally
        {
            Lock.Release();
        }
    }

    private static async Task<UnityResult> RunLockedAsync(string unityExe, string method, IEnumerable<string> extraArgs, Action<string> onLine, Action<string> onRaw, CancellationToken ct, string projectPath)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        string log = Path.Combine(AppPaths.Logs, $"unity-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

        ProcessStartInfo psi = new(unityExe) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string a in new[] { "-batchmode", "-quit", "-projectPath", projectPath, "-logFile", log, "-executeMethod", method })
            psi.ArgumentList.Add(a);
        foreach (string a in extraArgs) psi.ArgumentList.Add(a);

        using Process p = Process.Start(psi) ?? throw new InvalidOperationException("Unity did not start");
        UnityResult result = new() { ExitCode = -1 };
        long pos = 0;
        string partial = "";

        void Pump()
        {
            if (!File.Exists(log)) return;
            using FileStream fs = new(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (fs.Length <= pos) return;
            fs.Position = pos;
            using StreamReader sr = new(fs);
            string chunk = partial + sr.ReadToEnd();
            pos = fs.Length;
            string[] lines = chunk.Split('\n');
            partial = lines[^1];
            foreach (string raw in lines[..^1])
            {
                string line = raw.TrimEnd('\r');
                result.Lines.Add(line);
                onRaw(line);
                foreach (var (needle, message) in KnownProblems)
                {
                    if (result.Problem == null && line.Contains(needle, StringComparison.OrdinalIgnoreCase)) result.Problem = message;
                }

                int i = line.IndexOf("[VRM2BS] ", StringComparison.Ordinal);
                int q = line.IndexOf("[SB] ", StringComparison.Ordinal);
                if (i >= 0) onLine(line[(i + 9)..]);
                else if (q >= 0) onLine(line[(q + 5)..]);
            }
        }

        try
        {
            while (!p.HasExited)
            {
                await Task.Delay(400, ct);
                Pump();
            }
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (Exception) { /* already gone */ }
            throw;
        }

        Pump();
        result.ExitCode = p.ExitCode;
        return result;
    }
}
