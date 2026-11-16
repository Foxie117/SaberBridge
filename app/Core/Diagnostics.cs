using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SaberBridge.Core;

/// <summary>
/// Finds what else runs inside Unity's processes and why they crash: foreign DLLs loaded into them, Windows' crash
/// records (event 1000) for them, the usual places a program uses to put its code into every process, and Windows'
/// own crash dumps. For machines where "everything crashes with 0xC0000005". Every call is safe on failure.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Diagnostics
{
    /// <summary>Processes Windows should keep a crash dump of (Unity, its build helpers, the app itself).</summary>
    public static readonly string[] DumpedProcesses = { "Unity.exe", "netcorerun.exe", "bee_backend.exe", "UnityShaderCompiler.exe", "SaberBridge.exe" };

    private const string LocalDumpsKey = @"SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps";

    public static string DumpFolder => Path.Combine(AppPaths.Logs, "dumps");

    // ------------------------------------------------------------------ foreign modules

    /// <summary>
    /// The DLLs loaded into a process that are not Windows', not under one of the <paramref name="own"/> folders (Unity,
    /// the app, the project): overlays, hooks, "boosters", antivirus and the like.
    /// </summary>
    public static List<string> ForeignModules(int pid, IReadOnlyList<string> own)
    {
        List<string> list = new();
        IntPtr snap = CreateToolhelp32Snapshot(0x8 | 0x10 /* TH32CS_SNAPMODULE | SNAPMODULE32 */, (uint)pid);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return list;
        try
        {
            ModuleEntry e = new() { dwSize = Marshal.SizeOf<ModuleEntry>() };
            for (bool ok = Module32FirstW(snap, ref e); ok; ok = Module32NextW(snap, ref e))
            {
                if (!IsOurs(e.szExePath, own)) list.Add(e.szExePath);
            }
        }
        catch (Exception)
        {
            // what we have
        }
        finally
        {
            CloseHandle(snap);
        }

        return list;
    }

    private static readonly string WinDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

    private static bool IsOurs(string path, IReadOnlyList<string> own)
    {
        if (path.StartsWith(WinDir, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (string o in own)
        {
            if (o.Length > 0 && path.StartsWith(o.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    /// <summary>"path (company, signed by X)" for one file.</summary>
    public static string Describe(string path)
    {
        string company = "";
        try
        {
            company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim() ?? "";
        }
        catch (Exception)
        {
            // no version info
        }

        return $"{path} ({(company.Length > 0 ? company : "no company")}, {Signer(path)})";
    }

    private static string Signer(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057
            using X509Certificate c = X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            Match m = Regex.Match(c.Subject, "CN=\"?([^\",]+)");
            return "signed by " + (m.Success ? m.Groups[1].Value : c.Subject);
        }
        catch (Exception)
        {
            // Windows' own files are signed through catalogs, not inside the file
            return "no signature in the file";
        }
    }

    /// <summary>Our own process at start: anything foreign is already loaded into the app.</summary>
    public static void LogOwnModules()
    {
        try
        {
            // the single-file exe unpacks its native libraries (Skia, HarfBuzz, ANGLE) into %TEMP%\.net\SaberBridge
            string extracted = Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") is { Length: > 0 } b ? b : Path.Combine(Path.GetTempPath(), ".net");
            string[] own = { AppContext.BaseDirectory, Path.GetDirectoryName(Environment.ProcessPath) ?? "", RuntimeEnvironment(), extracted };
            List<string> foreign = ForeignModules(Environment.ProcessId, own);
            AppLog.Write("diag", foreign.Count == 0 ? "no foreign modules in the app" : $"foreign modules in the app: {foreign.Count}");
            foreach (string f in foreign) AppLog.Write("diag", "  " + Describe(f));
        }
        catch (Exception e)
        {
            AppLog.Write("diag", $"module check failed: {e.Message}");
        }
    }

    private static string RuntimeEnvironment() => Path.GetDirectoryName(typeof(object).Assembly.Location) is { Length: > 0 } d
        ? Path.GetFullPath(Path.Combine(d, "..", "..", ".."))
        : "";

    // ------------------------------------------------------------------ Windows crash records

    public sealed record Crash(DateTime Time, string App, string Module, string Code, string Offset, string AppPath, string ModulePath);

    /// <summary>Windows' "Application Error" records (event 1000) newer than <paramref name="since"/>, newest first.</summary>
    public static List<Crash> CrashesSince(DateTime since, int max = 50)
    {
        List<Crash> list = new();
        int ms = (int)Math.Clamp((DateTime.Now - since).TotalMilliseconds, 1000, int.MaxValue);
        string xml = Wevtutil($"qe Application /q:\"*[System[(EventID=1000) and TimeCreated[timediff(@SystemTime) <= {ms}]]]\" /rd:true /c:{max} /f:xml");
        foreach (Match ev in Regex.Matches(xml, "<Event .*?</Event>", RegexOptions.Singleline))
        {
            string t = Regex.Match(ev.Value, "SystemTime='([^']+)'").Groups[1].Value;
            // Windows 10/11 name the fields; take them in order, which is the same on every version
            List<string> d = Regex.Matches(ev.Value, "<Data(?: Name='[^']*')?>(.*?)</Data>", RegexOptions.Singleline).Select(x => System.Net.WebUtility.HtmlDecode(x.Groups[1].Value)).ToList();
            string F(int i) => i < d.Count ? d[i] : "";
            DateTime time = DateTime.TryParse(t, null, System.Globalization.DateTimeStyles.RoundtripKind, out DateTime tt) ? tt.ToLocalTime() : DateTime.MinValue;
            list.Add(new Crash(time, F(0), F(3), F(6), F(7), F(10), F(11)));
        }

        return list;
    }

    public static string Format(Crash c) =>
        $"{c.Time:yyyy-MM-dd HH:mm:ss} {c.App} crashed: code 0x{c.Code.Replace("0x", "")}, module {c.Module} at +{c.Offset.Replace("0x", "")} ({(c.ModulePath.Length > 0 ? c.ModulePath : "no file")})";

    /// <summary>Everything in Windows' application log about crashes (events 1000, 1001, 1026) as text.</summary>
    public static string CrashReport(int max) =>
        Wevtutil($"qe Application /q:\"*[System[(EventID=1000 or EventID=1001 or EventID=1026)]]\" /rd:true /c:{max} /f:text");

    private static string Wevtutil(string args)
    {
        try
        {
            ProcessStartInfo psi = new(Path.Combine(Environment.SystemDirectory, "wevtutil.exe"), args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            using Process p = Process.Start(psi)!;
            Task<string> err = p.StandardError.ReadToEndAsync();
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(20000))
            {
                try { p.Kill(); } catch (Exception) { /* gone */ }
            }

            return output.Length > 0 ? output : $"(wevtutil: exit {(p.HasExited ? p.ExitCode : -1)} {err.Result.Trim()})";
        }
        catch (Exception e)
        {
            return $"(wevtutil failed: {e.Message})";
        }
    }

    // ------------------------------------------------------------------ the system

    /// <summary>
    /// The places programs use to load their code into other processes, third-party services and drivers, and the
    /// running programs that are not Windows'.
    /// </summary>
    public static string SystemReport()
    {
        StringBuilder sb = new();
        void Section(string title) => sb.AppendLine().AppendLine("== " + title + " ==");

        Section("code loaded into every process");
        foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            Try(() =>
            {
                using RegistryKey? k = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view).OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Windows");
                sb.AppendLine($"AppInit_DLLs ({view}): \"{k?.GetValue("AppInit_DLLs")}\", LoadAppInit_DLLs={k?.GetValue("LoadAppInit_DLLs")}");
            });
        }

        Try(() =>
        {
            using RegistryKey? k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\AppCertDlls");
            sb.AppendLine("AppCertDlls: " + (k == null ? "none" : string.Join("; ", k.GetValueNames().Select(n => $"{n}={k.GetValue(n)}"))));
        });

        Section("Image File Execution Options with a debugger or verifier");
        Try(() =>
        {
            using RegistryKey? ifeo = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\Image File Execution Options");
            if (ifeo == null) return;
            foreach (string name in ifeo.GetSubKeyNames())
            {
                try
                {
                    using RegistryKey? k = ifeo.OpenSubKey(name);
                    if (k == null) continue;
                    string[] interesting = k.GetValueNames().Where(v => v is "Debugger" or "VerifierDlls" or "GlobalFlag").ToArray();
                    if (interesting.Length > 0) sb.AppendLine($"{name}: " + string.Join("; ", interesting.Select(v => $"{v}={k.GetValue(v)}")));
                }
                catch (Exception)
                {
                    // a few keys are closed to users
                }
            }
        });

        Section("services and drivers not from Microsoft");
        Try(() =>
        {
            using RegistryKey? services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null) return;
            foreach (string name in services.GetSubKeyNames())
            {
                using RegistryKey? k = services.OpenSubKey(name);
                if (k?.GetValue("ImagePath") is not string image) continue;
                string file = ImageFile(image);
                if (file.Length == 0 || !File.Exists(file) || IsMicrosoft(file)) continue;
                // drivers that come with Windows itself (signed as part of Windows) are not interesting
                if (file.StartsWith(WinDir, StringComparison.OrdinalIgnoreCase) && Signer(file).Contains("Microsoft Windows")) continue;
                int start = k.GetValue("Start") is int s ? s : -1;
                string startText = start switch { 0 => "boot", 1 => "system", 2 => "auto", 3 => "manual", 4 => "disabled", _ => "?" };
                sb.AppendLine($"{name} [{startText}] {Describe(file)}");
            }
        });

        Section("running programs not from Microsoft");
        Try(() =>
        {
            foreach (Process p in Process.GetProcesses().OrderBy(p => p.ProcessName))
            {
                using (p)
                {
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch (Exception) { /* elevated or protected */ }
                    if (path == null) path = QueryImage(p.Id);
                    if (path == null || IsMicrosoft(path)) continue;
                    sb.AppendLine($"{p.ProcessName} ({p.Id}) {Describe(path)}");
                }
            }
        });

        Section("crash dumps");
        sb.AppendLine(DumpsEnabled() ? $"enabled for {string.Join(", ", DumpedProcesses)} into {DumpFolder}" : "off");
        return sb.ToString();

        void Try(Action a)
        {
            try { a(); }
            catch (Exception e) { sb.AppendLine($"(failed: {e.Message})"); }
        }
    }

    private static bool IsMicrosoft(string file)
    {
        // only Windows' own files count: a third-party driver in System32\drivers or anything elsewhere is listed
        try
        {
            return file.StartsWith(WinDir, StringComparison.OrdinalIgnoreCase)
                && FileVersionInfo.GetVersionInfo(file).CompanyName?.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>The file of a service's ImagePath: \SystemRoot\..., system32\..., \??\C:\..., quoted, with arguments.</summary>
    private static string ImageFile(string image)
    {
        string s = Environment.ExpandEnvironmentVariables(image.Trim());
        if (s.StartsWith('"'))
        {
            int close = s.IndexOf('"', 1);
            s = s[1..(close > 0 ? close : s.Length)];
        }
        else
        {
            int exe = s.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            int sys = s.IndexOf(".sys", StringComparison.OrdinalIgnoreCase);
            int end = exe >= 0 ? exe + 4 : sys >= 0 ? sys + 4 : s.Length;
            s = s[..end];
        }

        if (s.StartsWith(@"\??\")) s = s[4..];
        if (s.StartsWith(@"\SystemRoot\", StringComparison.OrdinalIgnoreCase)) s = Path.Combine(WinDir, s[12..]);
        else if (s.StartsWith(@"system32\", StringComparison.OrdinalIgnoreCase)) s = Path.Combine(WinDir, s);
        return s;
    }

    private static string? QueryImage(int pid)
    {
        IntPtr h = OpenProcess(0x1000 /* PROCESS_QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            StringBuilder sb = new(1024);
            int size = sb.Capacity;
            return QueryFullProcessImageNameW(h, 0, sb, ref size) ? sb.ToString() : null;
        }
        finally
        {
            CloseHandle(h);
        }
    }

    // ------------------------------------------------------------------ crash dumps

    public static bool DumpsEnabled()
    {
        try
        {
            using RegistryKey? k = Registry.LocalMachine.OpenSubKey($@"{LocalDumpsKey}\netcorerun.exe");
            return k?.GetValue("DumpFolder") is string f && f.Equals(DumpFolder, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Runs elevated (the app starts itself with --dumps on|off): tells Windows to keep small crash dumps of Unity's
    /// processes in the app's logs folder, or stops it.
    /// </summary>
    public static int SetDumpsElevated(bool on, string folder)
    {
        try
        {
            foreach (string exe in DumpedProcesses)
            {
                if (!on)
                {
                    Registry.LocalMachine.DeleteSubKeyTree($@"{LocalDumpsKey}\{exe}", false);
                    continue;
                }

                using RegistryKey k = Registry.LocalMachine.CreateSubKey($@"{LocalDumpsKey}\{exe}");
                k.SetValue("DumpFolder", folder, RegistryValueKind.ExpandString);
                k.SetValue("DumpCount", 5, RegistryValueKind.DWord);
                k.SetValue("DumpType", 1, RegistryValueKind.DWord); // a mini dump: the stack and the loaded modules, a few MB
            }

            return 0;
        }
        catch (Exception)
        {
            return 1;
        }
    }

    /// <summary>Asks for administrator rights once and switches the dumps; true when they are now as asked.</summary>
    public static async Task<bool> SetDumpsAsync(bool on)
    {
        Directory.CreateDirectory(DumpFolder);
        ProcessStartInfo psi = new(Environment.ProcessPath!, $"--dumps {(on ? "on" : "off")} \"{DumpFolder}\"")
        {
            UseShellExecute = true,
            Verb = "runas",
        };
        try
        {
            using Process? p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
        }
        catch (Exception e)
        {
            AppLog.Write("diag", $"crash dumps not switched: {e.Message}");
        }

        bool now = DumpsEnabled();
        AppLog.Write("diag", $"crash dumps {(now ? "on" : "off")}");
        return now == on;
    }

    // ------------------------------------------------------------------ Win32

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry
    {
        public int dwSize;
        public uint th32ModuleID, th32ProcessID, GlblcntUsage, ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Module32FirstW(IntPtr snap, ref ModuleEntry e);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Module32NextW(IntPtr snap, ref ModuleEntry e);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, int flags, StringBuilder name, ref int size);
}
