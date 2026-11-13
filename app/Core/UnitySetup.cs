using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Win32;

namespace SaberBridge.Core;

public enum SetupStep { Checking, Downloading, NeedsPermission, Installing, Verifying, NeedsSignIn, Preparing, Ready, Failed, Manual }

/// <summary>One snapshot of the automatic Unity setup, for the UI.</summary>
public sealed record SetupStatus(SetupStep Step, string Text, double? Percent = null, string? Error = null, string? Detail = null)
{
    /// <summary>A step the user may skip (only .NET Framework 3.5, which Unity does not need to work).</summary>
    public bool CanSkip { get; init; }

    /// <summary>Unity Hub's installer reported success but Hub is nowhere to be found: let the user point at it.</summary>
    public bool CanPickHub { get; init; }
}

/// <summary>Bytes per second over the last few seconds, and the time left at that pace.</summary>
public sealed class RateMeter
{
    private readonly Queue<(DateTime at, long bytes)> _samples = new();

    public void Add(long bytes)
    {
        DateTime now = DateTime.UtcNow;
        _samples.Enqueue((now, bytes));
        while (_samples.Count > 2 && (now - _samples.Peek().at).TotalSeconds > 8) _samples.Dequeue();
    }

    public double PerSecond
    {
        get
        {
            if (_samples.Count < 2) return 0;
            (DateTime t0, long b0) = _samples.Peek();
            (DateTime t1, long b1) = _samples.Last();
            double s = (t1 - t0).TotalSeconds;
            return s > 0.5 ? Math.Max(0, (b1 - b0) / s) : 0;
        }
    }

    public TimeSpan? Left(long done, long total) => PerSecond > 1 && total > done ? TimeSpan.FromSeconds((total - done) / PerSecond) : null;
}

/// <summary>
/// Gets Unity ready without the user doing anything but two things Windows and Unity insist on: one admin prompt
/// (the Unity installers require it) and, the first time, signing in to a free Unity account in Unity Hub.
/// Steps: find what is already there, check disk space and the network, download what is missing (resumable,
/// signature checked), install it silently behind one prompt, verify the result, wait for the license, and open
/// the Quest project once so the first conversion is quick and the license is proven to work. Every step retries
/// what can be retried and says plainly what is wrong otherwise. Everything goes to logs\setup.log.
/// </summary>
public sealed class UnitySetup
{
    public const string VcRedistUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe";
    private const long Gb = 1L << 30;

    private readonly Settings _settings;
    private readonly Action<SetupStatus> _report;
    private readonly string _log = Path.Combine(AppPaths.Logs, "setup.log");

    public UnitySetup(Settings settings, Action<SetupStatus> report)
    {
        _settings = settings;
        _report = report;
    }

    public static string EditorTarget => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor", Compatibility.UnityVersion);

    /// <summary>What still has to be installed; empty when everything is there.</summary>
    public sealed record Missing(bool Hub, bool Editor, bool Android, bool VcRedist)
    {
        public bool Any => Hub || Editor || Android || VcRedist;
    }

    public Missing Detect()
    {
        string? unity = UnityLocator.Find(_settings.UnityPath);
        bool editor = unity == null;
        // a .whacker / .cyoob needs the Android module, and a later switch to the Quest tab should not cost a second prompt
        bool android = editor || !UnityLocator.HasAndroid(unity!);
        return new Missing(HubNeeded, editor, android, OperatingSystem.IsWindows() && !VcRedistInstalled());
    }

    /// <summary>Unity Hub only serves the sign-in for the license; with a license on the machine it is not needed.</summary>
    public static bool HubNeeded => Tools.UnityHub() == null && !Tools.UnityLicenseFound();

    // Hub's installer said "done" but Hub was not found afterwards: installing it again would only loop (it reuses an
    // old folder we cannot see), so the next runs ask the user where it is instead. Kept for the app's lifetime.
    private static bool _hubInstalledButNotFound;

    /// <summary>Runs every step that is still needed. Returns true when Unity is ready to convert.</summary>
    public async Task<bool> RunAsync(CancellationToken ct)
    {
        Log("---- setup run");
        if (!OperatingSystem.IsWindows())
        {
            // Linux: Unity Hub's own installer; the system check explains it
            Report(new SetupStatus(SetupStep.Manual, L.SetupLinux));
            return false;
        }

        Report(new SetupStatus(SetupStep.Checking, L.SetupChecking));
        Missing need = Detect();
        if (need.Hub && _hubInstalledButNotFound)
        {
            Log("hub: installed earlier in this session but still not found; not installing it again");
            need = need with { Hub = false };
        }

        Log($"missing: {need}; hub at {Tools.UnityHub() ?? "?"}; license {(Tools.UnityLicenseFound() ? "found" : "none")}");

        if (need.Any)
        {
            if (!CheckSpace(need)) return false;

            List<(string name, string file, string args)> installers = await DownloadAsync(need, ct);
            if (!await InstallAsync(installers, need, ct)) return false;
            if (need.Hub && HubNeeded) _hubInstalledButNotFound = true;
        }

        if (!Verify()) return false;

        if (!await LicenseAsync(ct)) return false;
        if (!await PrepareAsync(ct)) return false;

        Report(new SetupStatus(SetupStep.Ready, L.SetupReadyHint, 100));
        Log("ready");
        return true;
    }

    // ---------------------------------------------------------------- checks

    /// <summary>One line of the disk table in the guide: what goes on that drive, how much it needs, how much is free.</summary>
    public sealed record DiskRow(string Drive, string What, long Need, long Free)
    {
        public bool Ok => Free >= Need;
    }

    /// <summary>Where the editor goes and what each drive needs. Unity normally goes next to Unity Hub in Program Files;
    /// when that drive is short, the fixed drive with the most room takes the editor (and the downloads).</summary>
    public sealed record DiskPlan(string EditorDir, string DownloadDir, List<DiskRow> Rows)
    {
        public bool Ok => Rows.All(r => r.Ok);
    }

    // editor ~7 GB, Android ~2 GB, Hub ~0.6 GB once installed; the installers ~3 GB while downloading (deleted afterwards)
    private static long EditorBytes(Missing n) => (n.Editor ? 7 * Gb : 0) + (n.Android ? 2 * Gb : 0) + Gb;
    private static long HubBytes(Missing n) => (n.Hub ? Gb : 0) + (n.VcRedist ? Gb / 10 : 0);
    private static long DownloadBytes(Missing n) => (n.Editor ? 3 * Gb : 0) + (n.Android ? Gb / 2 : 0) + (n.Hub ? Gb / 4 : 0) + (n.VcRedist ? Gb / 20 : 0);

    public DiskPlan PlanDisks(Missing need)
    {
        string system = Path.GetPathRoot(EditorTarget)!;
        string? existing = UnityLocator.Find(_settings.UnityPath);
        string editorDir = need.Editor ? EditorTarget : Path.GetDirectoryName(Path.GetDirectoryName(existing!)!)!;
        string downloadDir = Path.Combine(AppPaths.Temp, "downloads");

        long editor = need.Editor || need.Android ? EditorBytes(need) : 0;
        long hub = HubBytes(need);
        long download = DownloadBytes(need);

        // the editor can only move when it is being installed fresh; a missing module has to go into the existing editor
        if (need.Editor && Free(system) < editor + hub + download)
        {
            DriveInfo? best = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady && !d.Name.Equals(system, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(d => d.AvailableFreeSpace)
                .FirstOrDefault(d => d.AvailableFreeSpace >= editor + download);
            if (best != null)
            {
                editorDir = Path.Combine(best.Name, "Unity", "Editor", Compatibility.UnityVersion);
                downloadDir = Path.Combine(best.Name, "Unity", "SaberBridge-downloads");
            }
        }

        Dictionary<string, (long need, List<string> what)> drives = new(StringComparer.OrdinalIgnoreCase);
        void Add(string dir, long bytes, string what)
        {
            if (bytes <= 0) return;
            string d = Path.GetPathRoot(Path.GetFullPath(dir))!;
            if (!drives.TryGetValue(d, out var e)) e = (0, new List<string>());
            e.need += bytes;
            e.what.Add(what);
            drives[d] = e;
        }

        Add(editorDir, editor, L.DiskEditor);
        Add(system, hub, L.DiskHub);
        Add(downloadDir, download, L.DiskDownloads);
        List<DiskRow> rows = drives.Select(kv => new DiskRow(kv.Key, string.Join(", ", kv.Value.what), kv.Value.need, Free(kv.Key))).ToList();
        return new DiskPlan(editorDir, downloadDir, rows);
    }

    private static long Free(string drive)
    {
        try
        {
            return new DriveInfo(drive).AvailableFreeSpace;
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>The plan the current run uses; the guide window shows it.</summary>
    public DiskPlan? Plan { get; private set; }

    private bool CheckSpace(Missing need)
    {
        Plan = PlanDisks(need);
        foreach (DiskRow r in Plan.Rows) Log($"space {r.Drive}: free {r.Free / Gb} GB, need {r.Need / Gb} GB ({r.What})");
        Log($"editor goes to {Plan.EditorDir}, downloads to {Plan.DownloadDir}");
        Report(new SetupStatus(SetupStep.Checking, L.SetupChecking));
        DiskRow? short_ = Plan.Rows.FirstOrDefault(r => !r.Ok);
        if (short_ == null) return true;
        Report(new SetupStatus(SetupStep.Failed, L.SetupTitleFailed, null, L.SetupNoSpace(short_.Drive, (short_.Need - short_.Free) / (double)Gb + 0.1)));
        return false;
    }

    public static bool VcRedistInstalled()
    {
        try
        {
            using RegistryKey? k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64");
            return k?.GetValue("Installed") is int i && i == 1;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- download

    private async Task<List<(string name, string file, string args)>> DownloadAsync(Missing need, CancellationToken ct)
    {
        // order matters for the install: runtime, Hub, editor, then the module that goes into the editor's folder
        List<(string name, string[] urls, string signer, string args)> plan = new();
        if (need.VcRedist) plan.Add(("Visual C++ Runtime", Mirrors.VcRedist, "Microsoft Corporation", "/install /quiet /norestart"));
        if (need.Hub) plan.Add(("Unity Hub", new[] { Downloads.UnityHubWindows }, Downloads.UnityPublisher, "/S"));
        string editorDir = Plan!.EditorDir;
        if (need.Editor) plan.Add(($"Unity {Compatibility.UnityVersion}", Mirrors.Unity($"Windows64EditorInstaller/UnitySetup64-{Compatibility.UnityVersion}.exe"), Downloads.UnityPublisher, $"/S /D={editorDir}"));
        if (need.Android) plan.Add(("Android Build Support", Mirrors.Unity($"TargetSupportInstaller/UnitySetup-Android-Support-for-Editor-{Compatibility.UnityVersion}.exe"), Downloads.UnityPublisher, $"/S /D={editorDir}"));

        // the real size of every file, asked from the server before anything is downloaded
        Report(new SetupStatus(SetupStep.Downloading, L.SetupSizing));
        long[] sizes = new long[plan.Count];
        for (int i = 0; i < plan.Count; i++)
        {
            string[] urls = plan[i].urls;
            sizes[i] = await Retry($"size of {plan[i].name}", async attempt => await Downloads.LengthAsync(Pick(urls, attempt), ct, throwOnNetwork: true) ?? 0, ct);
        }

        long total = sizes.Sum();
        Log($"download sizes: {string.Join(", ", plan.Select((p, i) => $"{p.name} {sizes[i]}"))}; total {total}");
        RateMeter rate = new();

        List<(string, string, string)> files = new();
        for (int i = 0; i < plan.Count; i++)
        {
            (string name, string[] urls, string signer, string args) = plan[i];
            string saveAs = Path.GetFileName(new Uri(urls[0]).LocalPath);
            int index = i;
            long before = sizes.Take(i).Sum();
            string file = await Retry($"download {name}", async attempt =>
            {
                Progress<(long done, long? total)> progress = new(p =>
                {
                    // a server that did not tell the size up front tells it once the download starts
                    if (sizes[index] == 0 && p.total is > 0) sizes[index] = p.total.Value;
                    long fileTotal = p.total ?? sizes[index];
                    long all = before + p.done;
                    long sum = Math.Max(sizes.Sum(), all);
                    rate.Add(all);
                    Report(new SetupStatus(SetupStep.Downloading,
                        L.SetupDownloadingFile(name, index + 1, plan.Count, p.done, fileTotal),
                        sum > 0 ? 100.0 * all / sum : null, null,
                        L.SetupBytes(all, sum, rate.PerSecond, rate.Left(all, sum))));
                });
                string url = Pick(urls, attempt);
                if (attempt > 1) Log($"{name}: trying {new Uri(url).Host}");
                return await Downloads.FetchAsync(url, progress, ct, signer, Plan.DownloadDir, saveAs, sizes[index]);
            }, ct);
            Log($"downloaded {name}: {file} ({new FileInfo(file).Length} bytes, signed by {signer})");
            files.Add((name, file, args));
        }

        return files;
    }

    /// <summary>Network and disk hiccups are retried with growing pauses; the status line shows the countdown.</summary>
    // attempt 1 uses the first host, attempt 2 the second, and so on round the list
    private static string Pick(string[] urls, int attempt) => urls[(attempt - 1) % urls.Length];

    private async Task<T> Retry<T>(string what, Func<int, Task<T>> action, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                return await action(attempt);
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                string why = Describe(ex);
                bool network = IsNetwork(ex);
                Log($"{what} failed (attempt {attempt}, {(network ? "network" : "other")}): {why}");
                // a network that does not work right now (offline, Wi-Fi gone, a site blocked until a VPN is on) is
                // waited out for as long as the app is open; anything else (bad signature, disk full) stops after a few tries
                if (!network && attempt >= 4) throw new SetupException(L.SetupDownloadFailed(why));
                int wait = network ? Math.Min(60, 10 * attempt) : 10 * attempt;
                string hint = network ? (IsBlockedLike(ex) ? L.SetupBlocked : L.SetupOffline) : why;
                for (int s = wait; s > 0; s--)
                {
                    Report(new SetupStatus(SetupStep.Downloading, L.SetupRetryIn(s), null, hint));
                    await Task.Delay(1000, ct);
                }
            }
        }
    }

    private static IEnumerable<Exception> Chain(Exception ex)
    {
        for (Exception? e = ex; e != null; e = e.InnerException) yield return e;
    }

    // the innermost messages say what really happened ("connection reset", "host not known"), the outer ones don't
    private static string Describe(Exception ex) => string.Join(" → ", Chain(ex).Select(e => e.Message).Distinct());

    private static bool IsNetwork(Exception ex) => Chain(ex).Any(e =>
        e is HttpRequestException or System.Net.Sockets.SocketException or System.Security.Authentication.AuthenticationException
            or TimeoutException or TaskCanceledException
        || (e is IOException && e.Message.Contains("transport", StringComparison.OrdinalIgnoreCase)));

    // the TLS handshake cut off or reset right away while the name resolves: what blocking looks like, a VPN helps
    private static bool IsBlockedLike(Exception ex) => Chain(ex).Any(e =>
        e is System.Security.Authentication.AuthenticationException
        || (e is System.Net.Sockets.SocketException se && se.SocketErrorCode is System.Net.Sockets.SocketError.ConnectionReset or System.Net.Sockets.SocketError.ConnectionAborted)
        || e.Message.Contains("SSL", StringComparison.OrdinalIgnoreCase));

    // ---------------------------------------------------------------- install

    // what the 2021.3.16f1 installers write, measured on a real install (always the same for this exact version)
    private const long EditorInstalledBytes = 5_553_484_133;
    private const long AndroidInstalledBytes = 2_016_036_130;

    private static long DirBytes(string dir)
    {
        if (!Directory.Exists(dir)) return 0;
        long sum = 0;
        try
        {
            foreach (FileInfo f in new DirectoryInfo(dir).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 }))
                sum += f.Length;
        }
        catch (Exception)
        {
            // files appear and move while the installer writes; the next count catches up
        }

        return sum;
    }

    private async Task<bool> InstallAsync(List<(string name, string file, string args)> installers, Missing need, CancellationToken ct)
    {
        string resultFile = Path.Combine(AppPaths.Temp, "install-result.txt");
        File.Delete(resultFile);
        string script = Path.Combine(AppPaths.Temp, "install.ps1");
        System.Text.StringBuilder sb = new();
        TryDelete(_netFxStatus);
        TryDelete(_netFxSkip);
        sb.AppendLine("$ErrorActionPreference = 'Continue'");
        sb.AppendLine($"$out = '{Esc(resultFile)}'");
        // the first line proves the elevated script ran at all (an empty result file means it never started)
        sb.AppendLine("Add-Content -LiteralPath $out 'started'");
        sb.AppendLine($"$fx = '{Esc(_netFxStatus)}'");
        sb.AppendLine($"$skip = '{Esc(_netFxSkip)}'");
        // While an installer runs: if it started DISM (Unity's installer turns on .NET Framework 3.5 that way, which
        // Windows downloads through Windows Update), report DISM's download percent from CBS.log and stop DISM when
        // the app asks to. Unity itself runs without .NET 3.5.
        sb.AppendLine(@"function Watch($p) {
  while (-not $p.HasExited) {
    $d = Get-Process Dism -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($d) {
      $pct = ''
      try { $m = Get-Content 'C:\Windows\Logs\CBS\CBS.log' -Tail 2000 -ErrorAction Stop | Select-String 'DownloadProgress: \[(\d+) / 100\]' | Select-Object -Last 1; if ($m) { $pct = $m.Matches[0].Groups[1].Value } } catch {}
      Set-Content -LiteralPath $fx ('dism|' + $pct + '|' + [int]((Get-Date) - $d.StartTime).TotalSeconds)
      if (Test-Path -LiteralPath $skip) { Get-Process Dism, DismHost -ErrorAction SilentlyContinue | Stop-Process -Force; Add-Content -LiteralPath $out 'NetFx3=skipped'; Remove-Item -LiteralPath $skip }
    } elseif (Test-Path -LiteralPath $fx) { Remove-Item -LiteralPath $fx }
    Start-Sleep -Seconds 2
  }
  if (Test-Path -LiteralPath $fx) { Remove-Item -LiteralPath $fx }
}");
        foreach ((string name, string file, string args) in installers)
        {
            sb.AppendLine($"try {{ $p = Start-Process -FilePath '{Esc(file)}' -ArgumentList '{Esc(args)}' -PassThru; Watch $p; $p.WaitForExit(); Add-Content -LiteralPath $out ('{Esc(name)}=' + $p.ExitCode) }}");
            sb.AppendLine($"catch {{ Add-Content -LiteralPath $out ('{Esc(name)}=error ' + $_.Exception.Message) }}");
        }

        sb.AppendLine("Add-Content -LiteralPath $out 'done'");
        await File.WriteAllTextAsync(script, sb.ToString(), ct);

        while (true)
        {
            Report(new SetupStatus(SetupStep.NeedsPermission, L.SetupAskPermission));
            Log("asking for admin rights");
            Process? p;
            try
            {
                p = Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"{script}\"")
                {
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden,
                });
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
            {
                // "No" on the prompt: wait for the user to try again (the banner has the button); nothing else can go on
                Log("admin prompt declined");
                Report(new SetupStatus(SetupStep.NeedsPermission, L.SetupPermissionDeclined, null, L.SetupPermissionWhy));
                await _permissionRetry.WaitAsync(ct);
                continue;
            }

            if (p == null) throw new SetupException("could not start the installer");
            int exitCode = 0;
            DateTime started = DateTime.Now;
            // the installers report nothing, so progress is what has landed in the editor's folder so far
            string editorDir = Plan!.EditorDir;
            long baseline = DirBytes(editorDir);
            long expected = (need.Editor ? EditorInstalledBytes : 0) + (need.Android ? AndroidInstalledBytes : 0);
            long written = 0;
            RateMeter rate = new();
            using CancellationTokenSource counting = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _ = Task.Run(async () =>
            {
                while (!counting.IsCancellationRequested)
                {
                    written = Math.Max(0, DirBytes(editorDir) - baseline);
                    rate.Add(written);
                    await Task.Delay(3000, counting.Token).ContinueWith(_ => { });
                }
            });

            using (p)
            {
                while (!p.HasExited)
                {
                    if (NetFxReport() is { } fx)
                    {
                        Report(fx);
                        await Task.Delay(1000, ct);
                        continue;
                    }

                    TimeSpan t = DateTime.Now - started;
                    string name = Current(resultFile, installers);
                    bool intoEditor = name.StartsWith("Unity 20") || name.StartsWith("Android");
                    long w = Math.Min(written, expected);
                    Report(intoEditor && expected > 0
                        ? new SetupStatus(SetupStep.Installing, L.SetupInstallingName(name, (int)t.TotalMinutes, t.Seconds), 100.0 * w / expected, null,
                            L.SetupWritten(w, expected, rate.PerSecond, rate.Left(w, expected)))
                        : new SetupStatus(SetupStep.Installing, L.SetupInstallingName(name, (int)t.TotalMinutes, t.Seconds)));
                    await Task.Delay(1000, ct);
                }

                try { exitCode = p.ExitCode; } catch (Exception) { exitCode = -1; }
            }

            counting.Cancel();
            Log($"installer script exited with {exitCode}");

            break;
        }

        string[] results = File.Exists(resultFile) ? await File.ReadAllLinesAsync(resultFile, ct) : Array.Empty<string>();
        Log("installer results: " + string.Join("; ", results));
        if (!results.Contains("started"))
        {
            // PowerShell never got to its first line: blocked by an antivirus or a policy, or closed right away
            Report(new SetupStatus(SetupStep.Failed, L.SetupTitleFailed, null, L.SetupScriptBlocked));
            return false;
        }
        foreach ((string name, _, _) in installers)
        {
            string? line = results.FirstOrDefault(r => r.StartsWith(name + "="));
            string code = line?[(name.Length + 1)..] ?? "not run";
            // vc_redist: 1638 = a newer one is already there, 3010 = fine after a restart
            bool ok = code is "0" or "1638" or "3010";
            if (!ok)
            {
                Report(new SetupStatus(SetupStep.Failed, L.SetupTitleFailed, null, L.SetupInstallerFailed(name, code)));
                return false;
            }
        }

        // the installers wrote shortcuts and files elsewhere; just make sure our deletions of the big downloads don't fail the run
        foreach ((_, string file, _) in installers) TryDelete(file);
        return true;
    }

    private readonly SemaphoreSlim _permissionRetry = new(0);
    private readonly string _netFxStatus = Path.Combine(AppPaths.Temp, "netfx-status.txt");
    private readonly string _netFxSkip = Path.Combine(AppPaths.Temp, "netfx-skip.flag");
    private bool _netFxSkipped;

    /// <summary>The "Skip" button while Windows tries to fetch .NET Framework 3.5.</summary>
    public void SkipNetFx() => SkipNetFx("user");

    private void SkipNetFx(string who)
    {
        if (_netFxSkipped) return;
        _netFxSkipped = true;
        Log($"skipping .NET Framework 3.5 ({who})");
        try
        {
            File.WriteAllText(_netFxSkip, "skip");
        }
        catch (IOException)
        {
            // the script picks it up on the next round anyway
        }
    }

    // While Windows fetches .NET 3.5 for Unity's installer: the real download percent, and an automatic skip when
    // Windows Update does not move (blocked or offline) for 3 minutes, or takes longer than 15 minutes in total.
    private SetupStatus? NetFxReport()
    {
        string[] parts;
        try
        {
            if (!File.Exists(_netFxStatus)) return null;
            parts = File.ReadAllText(_netFxStatus).Trim().Split('|');
        }
        catch (IOException)
        {
            return null;
        }

        if (parts.Length < 3 || parts[0] != "dism") return null;
        int? pct = int.TryParse(parts[1], out int v) ? v : null;
        int seconds = int.TryParse(parts[2], out int s) ? s : 0;
        bool stuck = (pct is null or 0 && seconds > 180) || seconds > 900;
        if (stuck && !_netFxSkipped)
        {
            Log($".NET 3.5 download stuck at {pct?.ToString() ?? "?"}% after {seconds} s");
            SkipNetFx("automatic");
        }

        return new SetupStatus(SetupStep.Installing, L.SetupNetFx(pct, seconds / 60, seconds % 60), pct, null,
            _netFxSkipped ? L.SetupNetFxSkipping : (pct is null or 0 && seconds > 45 ? L.SetupNetFxSlow : L.SetupNetFxWhy))
        { CanSkip = !_netFxSkipped };
    }

    /// <summary>The banner's "Allow" button after a declined prompt.</summary>
    public void RetryPermission()
    {
        if (_permissionRetry.CurrentCount == 0) _permissionRetry.Release();
    }

    private static string Current(string resultFile, List<(string name, string file, string args)> installers)
    {
        int done = 0;
        try
        {
            if (File.Exists(resultFile)) done = File.ReadAllLines(resultFile).Count(l => installers.Any(i => l.StartsWith(i.name + "=")));
        }
        catch (IOException)
        {
            // the script is writing it
        }

        return done < installers.Count ? installers[done].name : installers[^1].name;
    }

    // ---------------------------------------------------------------- verify

    private bool Verify()
    {
        Report(new SetupStatus(SetupStep.Verifying, L.SetupVerifying));
        string placed = Path.Combine(Plan?.EditorDir ?? EditorTarget, "Editor", "Unity.exe");
        if (UnityLocator.Find(_settings.UnityPath) == null && File.Exists(placed))
        {
            _settings.UnityPath = placed;
            _settings.Save();
        }

        string? unity = UnityLocator.Find(_settings.UnityPath);
        string? problem = unity == null ? L.SetupNoEditor(EditorTarget)
            : !Tools.HasWindowsBuildSupport(unity) ? L.SetupNoModule("Windows")
            : !UnityLocator.HasAndroid(unity) ? L.SetupNoModule("Android")
            : HubNeeded ? (_hubInstalledButNotFound ? L.SetupHubNotFound : L.SetupNoHub)
            : null;
        Log($"verify: unity={unity} hub={Tools.UnityHub() ?? "none"} problem={problem ?? "none"}");
        if (problem == null) return true;
        Report(new SetupStatus(SetupStep.Failed, L.SetupTitleFailed, null, problem) { CanPickHub = HubNeeded });
        return false;
    }

    // ---------------------------------------------------------------- license

    private async Task<bool> LicenseAsync(CancellationToken ct)
    {
        if (Tools.UnityLicenseFound()) return true;

        Log("no license: opening Unity Hub for sign-in");
        if (Tools.UnityHub() is { } hub && !Process.GetProcessesByName("Unity Hub").Any()) Tools.Open(hub);
        // the sign-in can take as long as it takes; the app keeps watching while it is open
        while (!Tools.UnityLicenseFound())
        {
            Report(new SetupStatus(SetupStep.NeedsSignIn, L.WaitingForLicense));
            await Task.Delay(3000, ct);
        }

        Log("license file appeared");
        return true;
    }

    // after a license failure: wait until Unity Hub writes something new into its license files (the user added the
    // Personal license), or a minute at most, then Unity is tried again
    private async Task WaitForLicenseChangeAsync(CancellationToken ct)
    {
        DateTime Stamp() => LicenseFolders().Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*", SearchOption.AllDirectories))
            .Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max();

        DateTime before = Stamp();
        DateTime until = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < until && Stamp() == before)
        {
            Report(new SetupStatus(SetupStep.NeedsSignIn, L.SetupAddPersonalLicense, null, null, L.SetupAddPersonalLicenseHow));
            await Task.Delay(3000, ct);
        }
    }

    private static IEnumerable<string> LicenseFolders()
    {
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "licenses");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Unity");
    }

    public static void OpenHub()
    {
        if (Tools.UnityHub() is { } hub) Tools.Open(hub);
    }

    // ---------------------------------------------------------------- first project import

    // Opening the Quest project once both proves the license works and imports it, so the first real conversion is quick.
    private async Task<bool> PrepareAsync(CancellationToken ct)
    {
        if (ProjectSetup.IsQuestImported) return true;
        string? unity = UnityLocator.Find(_settings.UnityPath);
        if (unity == null) return false;

        Report(new SetupStatus(SetupStep.Preparing, L.SetupPreparing));
        await Task.Run(ProjectSetup.EnsureQuest, ct);
        Directory.CreateDirectory(AppPaths.Temp);
        string jobs = Path.Combine(AppPaths.Temp, "warmup-jobs.txt");
        await File.WriteAllTextAsync(jobs, "", ct);
        DateTime started = DateTime.Now;
        using CancellationTokenSource tick = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _ = Task.Run(async () =>
        {
            while (!tick.IsCancellationRequested)
            {
                TimeSpan t = DateTime.Now - started;
                Report(new SetupStatus(SetupStep.Preparing, L.SetupPreparingTime((int)t.TotalMinutes, t.Seconds)));
                await Task.Delay(1000, tick.Token).ContinueWith(_ => { });
            }
        });

        // The license file appears as soon as Unity Hub is signed in, before a license is really there; only Unity
        // starting for real proves it. Without one it waits for the user to add the free Personal license and tries again.
        while (true)
        {
            UnityResult r = await UnityRunner.RunAsync(unity, "SaberBridge.QuestJobs.Run", new[] { "-jobsFile", jobs }, _ => { }, _ => { }, ct, AppPaths.QuestProject);
            Log($"warm-up: exit {r.ExitCode}, problem {r.Problem ?? "none"}");
            if (r.ExitCode == 0 && r.Problem == null) break;
            if (!r.LicenseProblem)
            {
                tick.Cancel();
                Report(new SetupStatus(SetupStep.Failed, L.SetupTitleFailed, null, r.Problem ?? L.SetupUnityFailed(r.ExitCode)));
                return false;
            }

            tick.Cancel();
            if (Tools.UnityHub() is { } hub && !Process.GetProcessesByName("Unity Hub").Any()) Tools.Open(hub);
            await WaitForLicenseChangeAsync(ct);
            Report(new SetupStatus(SetupStep.Preparing, L.SetupCheckingLicense));
        }

        tick.Cancel();
        ProjectSetup.MarkQuestImported();
        return true;
    }

    // ---------------------------------------------------------------- helpers

    private void Report(SetupStatus s) => _report(s);

    private static string Esc(string s) => s.Replace("'", "''");

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception)
        {
            // a locked download is cleaned up next time
        }
    }

    private void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.AppendAllText(_log, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
            AppLog.Write("setup", line);
        }
        catch (Exception)
        {
            // logging must never break the setup
        }
    }
}

/// <summary>A setup problem with a message meant for the user.</summary>
public sealed class SetupException : Exception
{
    public SetupException(string message) : base(message)
    {
    }
}
