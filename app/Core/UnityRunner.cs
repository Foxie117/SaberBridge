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

    /// <summary>The converter printed at least one line, so Unity itself started and opened the project.</summary>
    public bool ConverterStarted { get; set; }

    public const string Stuck = "Unity stopped responding (no progress for 20 minutes). See the log.";
}

/// <summary>Runs Unity (batch mode, or a hidden editor out of sight) and streams the converter's "[VRM2BS]" / "[SB]" log lines.</summary>
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

    // what Unity is busy with before the converter starts, read from its own log (Unity 2021.3 wording)
    private static readonly (string Needle, string Phase)[] Phases =
    {
        ("[Licensing::", "license"),
        ("LicensingClient", "license"),
        ("Initialize engine version", "engine"),
        ("Refreshing native plugins", "import"),
        ("Asset Pipeline Refresh", "import"),
        ("Start importing", "import"),
        ("Starting script compilation", "scripts"),
        ("Begin MonoManager ReloadAssembly", "scripts"),
        ("Compiling shader", "shaders"),
        ("Launching external process", "import"),
        ("Invoking ", "method"),
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

    /// <summary>
    /// The hidden-window editor runs on a desktop of its own that is never shown, so its splash, progress bars and
    /// anything it opens (Unity Hub for the license included) never pop up over the user's windows. Turned off by
    /// itself (and remembered) when Unity could not work there.
    /// </summary>
    public static bool OwnDesktop { get; set; } = true;

    /// <summary>Raised when Unity failed on its own desktop but worked with hidden windows (to save it in the settings).</summary>
    public static event Action? OwnDesktopFailed;

    /// <summary>
    /// Until the converter prints its first line: what Unity is doing ("license", "import", "scripts", ...) and for how
    /// long it has been running, about once a second; so "Starting Unity" never looks frozen.
    /// </summary>
    public static event Action<string, TimeSpan>? Activity;

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
                AppLog.Write("unity", "no license in batch mode while a license file exists: trying the hidden editor");
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

    private static async Task<UnityResult> RunLockedAsync(string unityExe, string method, List<string> extraArgs, Action<string> onLine, Action<string> onRaw, CancellationToken ct, string projectPath, bool hiddenWindow)
    {
        bool ownDesktop = hiddenWindow && OwnDesktop && OperatingSystem.IsWindows();
        UnityResult r = await RunOnceAsync(unityExe, method, extraArgs, onLine, onRaw, ct, projectPath, hiddenWindow, ownDesktop);
        // Unity died on its own desktop before the converter said anything (a GPU or driver that wants the visible
        // desktop): once more with plain hidden windows, and stay with that if it works
        if (ownDesktop && !r.ConverterStarted && !r.LicenseProblem && r.Problem is null or UnityResult.Stuck)
        {
            AppLog.Write("unity", $"nothing from the converter on Unity's own desktop (exit {r.ExitCode}, problem {r.Problem ?? "none"}): retrying with hidden windows");
            UnityResult plain = await RunOnceAsync(unityExe, method, extraArgs, onLine, onRaw, ct, projectPath, true, false);
            if (plain.ConverterStarted)
            {
                AppLog.Write("unity", "hidden windows worked where the own desktop did not: keeping hidden windows");
                OwnDesktop = false;
                OwnDesktopFailed?.Invoke();
            }

            return plain;
        }

        return r;
    }

    private static async Task<UnityResult> RunOnceAsync(string unityExe, string method, IEnumerable<string> extraArgs, Action<string> onLine, Action<string> onRaw, CancellationToken ct, string projectPath, bool hiddenWindow, bool ownDesktop)
    {
        Directory.CreateDirectory(AppPaths.Logs);
        string log = Path.Combine(AppPaths.Logs, $"unity-{DateTime.Now:yyyyMMdd-HHmmss-fff}.log");

        List<string> args = new();
        if (!hiddenWindow) args.Add("-batchmode");
        args.AddRange(new[] { "-quit", "-projectPath", projectPath, "-logFile", log, "-executeMethod", method });
        args.AddRange(extraArgs);

        string mode = !hiddenWindow ? "batch" : ownDesktop ? "hidden editor on its own desktop" : "hidden editor windows";
        AppLog.Write("unity", $"start ({mode}): {unityExe} {string.Join(" ", args.Select(Quote))}");
        bool hubWasRunning = UnityDesktop.HubRunning();

        Process p;
        if (ownDesktop && UnityDesktop.Start(unityExe, args.Select(Quote)) is { } started)
        {
            p = started;
        }
        else
        {
            if (ownDesktop)
            {
                AppLog.Write("unity", "could not start Unity on its own desktop: using hidden windows");
                ownDesktop = false;
            }

            ProcessStartInfo psi = hiddenWindow
                // a normal editor start, its window hidden; -quit still closes it after the method
                ? new ProcessStartInfo(unityExe, string.Join(" ", args.Select(Quote))) { UseShellExecute = true, WindowStyle = ProcessWindowStyle.Hidden }
                : new ProcessStartInfo(unityExe) { UseShellExecute = false, CreateNoWindow = true };
            if (!hiddenWindow)
            {
                foreach (string a in args) psi.ArgumentList.Add(a);
            }

            p = Process.Start(psi) ?? throw new InvalidOperationException("Unity did not start");
        }

        using Process proc = p;
        AppLog.Write("unity", $"pid {proc.Id}, log {log}");
        DateTime started0 = DateTime.UtcNow;
        UnityResult result = new() { ExitCode = -1 };
        long pos = 0;
        string partial = "";
        string phase = "start";
        string lastRaw = "";
        HashSet<string> seenWindows = new();
        HashSet<string> seenPhases = new();

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
                if (line.Length > 0) lastRaw = line;
                onRaw(line);
                foreach (var (needle, message) in KnownProblems)
                {
                    if (result.Problem == null && line.Contains(needle, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Problem = message;
                        AppLog.Write("unity", $"problem: {message} (line: {Cut(line)})");
                    }
                }

                foreach (var (needle, ph) in Phases)
                {
                    if (line.Contains(needle, StringComparison.Ordinal) && ph != phase)
                    {
                        phase = ph;
                        // Unity goes back and forth between importing and compiling: the first time of each is enough
                        if (seenPhases.Add(ph)) AppLog.Write("unity", $"phase {ph} at {Elapsed()}");
                        break;
                    }
                }

                int i = line.IndexOf("[VRM2BS] ", StringComparison.Ordinal);
                int q = line.IndexOf("[SB] ", StringComparison.Ordinal);
                string? ours = i >= 0 ? line[(i + 9)..] : q >= 0 ? line[(q + 5)..] : null;
                if (ours == null) continue;
                if (!result.ConverterStarted) AppLog.Write("unity", $"converter started at {Elapsed()}");
                result.ConverterStarted = true;
                if (ours.StartsWith("OK ") || ours.StartsWith("FAIL ") || ours.StartsWith("BEGIN ")) AppLog.Write("unity", Cut(ours));
                onLine(ours);
            }
        }

        string Elapsed() => (DateTime.UtcNow - started0).ToString(@"m\:ss");

        // the windows Unity has open: hide them (hidden-window mode) and write down each new one; a Windows dialog
        // (#32770) is Unity asking something nobody can answer, its text goes to the log and to the user
        string? Look()
        {
            if (!OperatingSystem.IsWindows() || !hiddenWindow) return null;
            string? dialog = null;
            foreach (UnityDesktop.Win w in ownDesktop ? UnityDesktop.Windows() : UnityDesktop.WindowsOf(UnityDesktop.Tree(proc.Id)))
            {
                if (!ownDesktop && w.Visible) UnityDesktop.Hide(w.Handle);
                if (w.IsNoise) continue;
                // a progress window retitles itself every second ("busy for 12s"): one line per kind of window
                string key = $"{w.Process}|{w.Class}|{(w.Progress ? "progress" : w.Title)}";
                if (seenWindows.Add(key)) AppLog.Write("unity", $"window: {w.Process} \"{w.Title}\" [{w.Class}{(w.Progress ? ", progress" : "")}]{(w.Text.Length > 0 ? " text: " + Cut(w.Text) : "")}");
                if (w.IsQuestion) dialog = (w.Title + ": " + w.Text).Trim(' ', ':');
            }

            return dialog;
        }

        try
        {
            // nothing new in the log for a long time: Unity waits for something. A dialog gets 2 minutes (nobody can
            // see it), anything else 20 (a first import can be quiet for a long while)
            DateTime lastGrowth = DateTime.UtcNow, lastBeat = DateTime.UtcNow, lastActivity = DateTime.MinValue;
            long lastPos = 0;
            int tick = 0;
            while (!proc.HasExited)
            {
                // fast at first so a window shown with hidden windows is put away before it is noticed
                await Task.Delay(tick++ < 100 ? 100 : 400, ct);
                string? dialog = Look();
                Pump();
                if (pos != lastPos)
                {
                    lastPos = pos;
                    lastGrowth = DateTime.UtcNow;
                }

                if (!result.ConverterStarted && DateTime.UtcNow - lastActivity > TimeSpan.FromSeconds(1))
                {
                    lastActivity = DateTime.UtcNow;
                    Activity?.Invoke(phase, DateTime.UtcNow - started0);
                }

                if (DateTime.UtcNow - lastBeat > TimeSpan.FromSeconds(30))
                {
                    lastBeat = DateTime.UtcNow;
                    AppLog.Write("unity", $"running {Elapsed()}, phase {phase}, log {pos / 1024} KB, quiet {(int)(DateTime.UtcNow - lastGrowth).TotalSeconds} s, last: {Cut(lastRaw)}");
                }

                TimeSpan quiet = DateTime.UtcNow - lastGrowth;
                if (dialog != null && quiet > TimeSpan.FromMinutes(2))
                {
                    AppLog.Write("unity", $"stuck on a dialog: {dialog}");
                    try { proc.Kill(true); } catch (Exception) { /* already gone */ }
                    result.Problem ??= L.UnityDialog(dialog);
                    break;
                }

                if (hiddenWindow && quiet > TimeSpan.FromMinutes(20))
                {
                    AppLog.Write("unity", $"no progress for 20 minutes, giving up; last: {Cut(lastRaw)}");
                    try { proc.Kill(true); } catch (Exception) { /* already gone */ }
                    result.Problem ??= UnityResult.Stuck;
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            AppLog.Write("unity", "cancelled");
            try { proc.Kill(true); } catch (Exception) { /* already gone */ }
            throw;
        }
        finally
        {
            // Unity Hub started by this run on Unity's own desktop would stay there, unseen, and keep the real one
            // from opening: close it (Unity started it only to hand over the license)
            if (ownDesktop && !hubWasRunning) UnityDesktop.CloseHubOnOwnDesktop();
        }

        Pump();
        result.ExitCode = proc.HasExited ? proc.ExitCode : -1;
        AppLog.Write("unity", $"finished in {Elapsed()}, exit {result.ExitCode}, converter {(result.ConverterStarted ? "ran" : "never started")}, problem {result.Problem ?? "none"}; last: {Cut(lastRaw)}");
        return result;
    }

    private static string Cut(string s) => s.Length > 300 ? s[..300] + "…" : s;
}
