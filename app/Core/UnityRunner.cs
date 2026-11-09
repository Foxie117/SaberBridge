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

    /// <summary>
    /// The avatar project for reading .avatar files (.avatar -> .vrm). It needs Custom Avatars' FinalIK and DynamicBone from a PC game
    /// copy to load them; null when there is none (the caller explains).
    /// </summary>
    public static string? EnsureForAvatarFiles(GameInstall? game)
    {
        if (game == null) return L.AvatarNeedsGame;
        try
        {
            Ensure(game);
            return null;
        }
        catch (FileNotFoundException e)
        {
            return e.Message;
        }
    }

    /// <summary>The Unity project (opened with 2021.3, like Quest Beat Saber 1.29+) that turns PC sabers into Quest .whacker files.</summary>
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

    /// <summary>Unity stopped because it has no working license (as opposed to any other failure).</summary>
    public bool LicenseProblem => Problem != null && Problem.Contains("license", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Runs Unity in batch mode and streams the converter's "[VRM2BS]" log lines.</summary>
public static class UnityRunner
{
    private static readonly (string Needle, string Message)[] KnownProblems =
    {
        ("No valid Unity Editor license", "Unity has no active license. Open Unity Hub, sign in and add a free Personal license."),
        ("License is not active", "Unity has no active license. Open Unity Hub, sign in and add a free Personal license."),
        // signed in to Unity Hub, but no Personal license added yet (Hub 3 does not add one by itself)
        ("has not been activated with a valid License", "Unity has no active license. Open Unity Hub, sign in and add a free Personal license."),
        ("Failed to activate/update license", "Unity has no active license. Open Unity Hub, sign in and add a free Personal license."),
        ("another Unity instance is running with this project", "The converter's Unity project is open in another Unity window. Close it and try again."),
        ("Multiple Unity instances cannot open the same project", "The converter's Unity project is open in another Unity window. Close it and try again."),
        ("build target was unsupported", "Unity cannot build for Windows. In Unity Hub add the module \"Windows Build Support (Mono)\" to this editor."),
        ("error CS", "The converter scripts failed to compile (see the log)."),
    };

    // one Unity at a time: two editors cannot open the same project
    private static readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>
    /// Run Unity as a normal editor with its window hidden instead of -batchmode. Needed on machines that only have
    /// Unity Hub 3's entitlement license: Unity 2021.3 in batch mode cannot get the access token from Hub ("Access
    /// token is unavailable") and quits without a license, while the same editor started normally gets it. Machines
    /// with an old Unity_lic.ulf work in batch mode. Remembered in the settings once it was needed.
    /// </summary>
    public static bool HiddenWindowMode { get; set; }

    /// <summary>Raised when the runner switched to the hidden-window mode by itself (to save it in the settings).</summary>
    public static event Action<bool>? HiddenWindowModeChanged;

    public static async Task<UnityResult> RunAsync(string unityExe, string method, IEnumerable<string> extraArgs, Action<string> onLine, Action<string> onRaw, CancellationToken ct, string? projectPath = null)
    {
        await Lock.WaitAsync(ct);
        try
        {
            List<string> args = extraArgs.ToList();
            string project = projectPath ?? AppPaths.UnityProject;
            UnityResult r = await RunLockedAsync(unityExe, method, args, onLine, onRaw, ct, project, HiddenWindowMode);
            // no license in batch mode, though Unity Hub has one: try once as a hidden normal editor, and keep that mode
            if (!HiddenWindowMode && r.LicenseProblem && OperatingSystem.IsWindows() && Tools.UnityLicenseFound())
            {
                UnityResult hidden = await RunLockedAsync(unityExe, method, args, onLine, onRaw, ct, project, true);
                if (!hidden.LicenseProblem)
                {
                    HiddenWindowMode = true;
                    HiddenWindowModeChanged?.Invoke(true);
                }

                return hidden;
            }

            return r;
        }
        finally
        {
            Lock.Release();
        }
    }

    private static string Quote(string a) => a.Length > 0 && a.IndexOfAny(new[] { ' ', '"', '\t' }) < 0 ? a : "\"" + a.Replace("\"", "\\\"") + "\"";

    private static async Task<UnityResult> RunLockedAsync(string unityExe, string method, IEnumerable<string> extraArgs, Action<string> onLine, Action<string> onRaw, CancellationToken ct, string projectPath, bool hiddenWindow)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        string log = Path.Combine(AppPaths.Logs, $"unity-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

        List<string> args = new();
        if (!hiddenWindow) args.Add("-batchmode");
        args.AddRange(new[] { "-quit", "-projectPath", projectPath, "-logFile", log, "-executeMethod", method });
        args.AddRange(extraArgs);

        ProcessStartInfo psi = hiddenWindow
            // a normal editor start, its window hidden; -quit still closes it after the method
            ? new ProcessStartInfo(unityExe, string.Join(" ", args.Select(Quote))) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }
            : new ProcessStartInfo(unityExe) { UseShellExecute = false, CreateNoWindow = true };
        if (!hiddenWindow)
        {
            foreach (string a in args) psi.ArgumentList.Add(a);
        }

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
            // a hidden editor that sits on a dialog nobody can see would wait forever: give up after 20 quiet minutes
            DateTime lastGrowth = DateTime.UtcNow;
            long lastPos = 0;
            while (!p.HasExited)
            {
                await Task.Delay(400, ct);
                // a normal editor still shows its splash and progress windows even when started hidden
                if (hiddenWindow && OperatingSystem.IsWindows()) HiddenWindows.HideAll(p.Id);
                Pump();
                if (pos != lastPos)
                {
                    lastPos = pos;
                    lastGrowth = DateTime.UtcNow;
                }
                else if (hiddenWindow && DateTime.UtcNow - lastGrowth > TimeSpan.FromMinutes(20))
                {
                    try { p.Kill(true); } catch (Exception) { /* already gone */ }
                    result.Problem ??= "Unity stopped responding (no progress for 20 minutes). See the log.";
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch (Exception) { /* already gone */ }
            throw;
        }

        Pump();
        result.ExitCode = p.HasExited ? p.ExitCode : -1;
        return result;
    }
}

/// <summary>Hides every visible top-level window of a process (Unity's splash and progress windows).</summary>
internal static class HiddenWindows
{
    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    private const int SwHide = 0;

    public static void HideAll(int processId)
    {
        try
        {
            EnumWindows((hwnd, _) =>
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid == processId && IsWindowVisible(hwnd)) ShowWindow(hwnd, SwHide);
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception)
        {
            // cosmetic only
        }
    }
}
