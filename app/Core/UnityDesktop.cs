using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SaberBridge.Core;

/// <summary>
/// Windows plumbing for keeping Unity out of sight: a desktop of its own (like a second virtual desktop that is
/// never switched to: windows there are never drawn over the user's), reading which windows Unity opened and the
/// text of its dialogs, and hiding windows when the own desktop cannot be used. Every call is safe on failure.
/// </summary>
internal static class UnityDesktop
{
    private const string DesktopName = "SaberBridgeUnity";
    private const uint GenericAll = 0x10000000;
    private const int StartfUseShowWindow = 0x1;
    private const short SwHide = 0;

    private static IntPtr _desktop;

    public sealed record Win(IntPtr Handle, int Pid, string Process, string Class, string Title, string Text, bool Visible, bool Progress)
    {
        /// <summary>A Windows dialog that asks something (not one of Unity's progress bars, which are dialogs too).</summary>
        public bool IsQuestion => Class == "#32770" && !Progress && Text.Length > 0;

        /// <summary>Input-method and system helper windows every process has; not worth a log line.</summary>
        public bool IsNoise => Class is "IME" or "MSCTFIME UI" or "CicLoaderWndClass" or "CiceroUIWndFrame" or "UAC Input Indicator"
            or "UAC_InputIndicatorOverlayWnd" or "Touch Tooltip Window";
    }

    /// <summary>Starts Unity on its own desktop; null when Windows would not (the caller falls back to hidden windows).</summary>
    public static Process? Start(string exe, IEnumerable<string> quotedArgs)
    {
        try
        {
            if (_desktop == IntPtr.Zero)
            {
                // kept open for the app's lifetime: the desktop goes away with the last handle and the last window
                _desktop = CreateDesktopW(DesktopName, IntPtr.Zero, IntPtr.Zero, 0, GenericAll, IntPtr.Zero);
                if (_desktop == IntPtr.Zero)
                {
                    AppLog.Write("unity", $"CreateDesktop failed: {Marshal.GetLastWin32Error()}");
                    return null;
                }
            }

            StartupInfo si = new()
            {
                cb = Marshal.SizeOf<StartupInfo>(),
                lpDesktop = $@"{WindowStation()}\{DesktopName}",
                dwFlags = StartfUseShowWindow,
                wShowWindow = SwHide,
            };
            StringBuilder cmd = new($"\"{exe}\" {string.Join(" ", quotedArgs)}");
            if (!CreateProcessW(null, cmd, IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, null, ref si, out ProcessInformation pi))
            {
                AppLog.Write("unity", $"CreateProcess on the own desktop failed: {Marshal.GetLastWin32Error()}");
                return null;
            }

            try
            {
                Process p = Process.GetProcessById(pi.dwProcessId);
                _ = p.Handle; // hold a handle, so the exit code stays readable after Unity quits
                return p;
            }
            finally
            {
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
        }
        catch (Exception e)
        {
            AppLog.Write("unity", $"own desktop: {e.Message}");
            return null;
        }
    }

    private static string WindowStation()
    {
        try
        {
            IntPtr ws = GetProcessWindowStation();
            byte[] buf = new byte[512];
            if (GetUserObjectInformationW(ws, 2 /* UOI_NAME */, buf, buf.Length, out int needed) && needed > 2)
                return Encoding.Unicode.GetString(buf, 0, needed - 2);
        }
        catch (Exception)
        {
            // the default below
        }

        return "WinSta0";
    }

    /// <summary>Every top-level window on Unity's own desktop.</summary>
    public static List<Win> Windows()
    {
        List<Win> list = new();
        if (_desktop == IntPtr.Zero) return list;
        try
        {
            EnumDesktopWindows(_desktop, (h, _) =>
            {
                list.Add(Describe(h));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception)
        {
            // nothing to report then
        }

        return list;
    }

    /// <summary>The top-level windows on the user's desktop that belong to these processes (visible or with text).</summary>
    public static List<Win> WindowsOf(HashSet<int> pids)
    {
        List<Win> list = new();
        try
        {
            EnumWindows((h, _) =>
            {
                GetWindowThreadProcessId(h, out uint pid);
                if (pids.Contains((int)pid) && (IsWindowVisible(h) || GetWindowTextLengthW(h) > 0)) list.Add(Describe(h));
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception)
        {
            // nothing to report then
        }

        return list;
    }

    public static void Hide(IntPtr h)
    {
        try { ShowWindow(h, SwHide); } catch (Exception) { /* cosmetic */ }
    }

    /// <summary>A process and all its descendants (Unity starts the licensing client, shader compilers, import workers).</summary>
    public static HashSet<int> Tree(int root)
    {
        HashSet<int> set = new() { root };
        try
        {
            List<(int pid, int parent)> all = new();
            IntPtr snap = CreateToolhelp32Snapshot(0x2 /* TH32CS_SNAPPROCESS */, 0);
            if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return set;
            try
            {
                ProcessEntry e = new() { dwSize = Marshal.SizeOf<ProcessEntry>() };
                for (bool ok = Process32FirstW(snap, ref e); ok; ok = Process32NextW(snap, ref e)) all.Add(((int)e.th32ProcessID, (int)e.th32ParentProcessID));
            }
            finally
            {
                CloseHandle(snap);
            }

            for (bool grew = true; grew;)
            {
                grew = false;
                foreach (var (pid, parent) in all)
                {
                    if (set.Contains(parent) && set.Add(pid)) grew = true;
                }
            }
        }
        catch (Exception)
        {
            // the root alone then
        }

        return set;
    }

    public static bool HubRunning()
    {
        try
        {
            Process[] hubs = Process.GetProcessesByName("Unity Hub");
            foreach (Process h in hubs) h.Dispose();
            return hubs.Length > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Closes a Unity Hub whose windows are on Unity's own desktop (started there by Unity for the license).</summary>
    public static void CloseHubOnOwnDesktop()
    {
        foreach (int pid in Windows().Where(w => w.Process.Equals("Unity Hub", StringComparison.OrdinalIgnoreCase)).Select(w => w.Pid).Distinct())
        {
            try
            {
                using Process hub = Process.GetProcessById(pid);
                AppLog.Write("unity", $"closing Unity Hub (pid {pid}) that Unity opened on its own desktop");
                hub.Kill(true);
            }
            catch (Exception e)
            {
                AppLog.Write("unity", $"could not close Unity Hub {pid}: {e.Message}");
            }
        }
    }

    private static Win Describe(IntPtr h)
    {
        GetWindowThreadProcessId(h, out uint pid);
        string name = "?";
        try
        {
            using Process p = Process.GetProcessById((int)pid);
            name = p.ProcessName;
        }
        catch (Exception)
        {
            // gone already
        }

        StringBuilder cls = new(256);
        GetClassNameW(h, cls, cls.Capacity);
        string text = "";
        bool progress = false;
        if (cls.ToString() == "#32770")
        {
            // a dialog: its message and buttons are child windows; one with a progress bar is Unity's "Importing…"
            List<string> parts = new();
            EnumChildWindows(h, (c, _) =>
            {
                StringBuilder childClass = new(64);
                GetClassNameW(c, childClass, childClass.Capacity);
                if (childClass.ToString() == "msctls_progress32") progress = true;
                string t = TextOf(c).Trim();
                if (t.Length > 0) parts.Add(t);
                return true;
            }, IntPtr.Zero);
            text = string.Join(" | ", parts);
        }

        return new Win(h, (int)pid, name, cls.ToString(), TextOf(h), text, IsWindowVisible(h), progress);
    }

    private static string TextOf(IntPtr h)
    {
        int len = GetWindowTextLengthW(h);
        if (len <= 0) return "";
        StringBuilder sb = new(len + 1);
        GetWindowTextW(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // ------------------------------------------------------------------ Win32

    private delegate bool EnumProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int cb;
        public string? lpReserved;
        public string? lpDesktop;
        public string? lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public int dwSize;
        public uint cntUsage, th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID, cntThreads, th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateDesktopW(string name, IntPtr device, IntPtr devmode, int flags, uint access, IntPtr sa);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetProcessWindowStation();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetUserObjectInformationW(IntPtr obj, int index, byte[] info, int length, out int needed);

    [DllImport("user32.dll")]
    private static extern bool EnumDesktopWindows(IntPtr desktop, EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumProc proc, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hwnd, int cmd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(IntPtr hwnd, StringBuilder name, int max);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder text, int max);

    [DllImport("user32.dll")]
    private static extern int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcessW(string? app, StringBuilder cmd, IntPtr pa, IntPtr ta, bool inherit, uint flags, IntPtr env, string? cwd, ref StartupInfo si, out ProcessInformation pi);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32FirstW(IntPtr snap, ref ProcessEntry e);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Process32NextW(IntPtr snap, ref ProcessEntry e);
}
