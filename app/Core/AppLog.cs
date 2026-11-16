using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace SaberBridge.Core;

/// <summary>
/// The app's own journal: logs\app-YYYYMMDD.log, one line per event with the time and the area. Everything that can
/// explain a "it hangs" report goes here (each Unity start with its mode and arguments, heartbeats while it runs,
/// windows Unity opened, exit codes, statuses shown to the user, errors). Never throws.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string File => Path.Combine(AppPaths.Logs, $"app-{DateTime.Now:yyyyMMdd}.log");

    public static void Write(string area, string message)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(AppPaths.Logs);
                System.IO.File.AppendAllText(File, $"{DateTime.Now:HH:mm:ss.fff} [{area}] {message}{Environment.NewLine}", Encoding.UTF8);
            }
        }
        catch (Exception)
        {
            // logging must never break the app
        }
    }

    /// <summary>The first lines of every run: version, system, paths; and old logs cleaned up.</summary>
    public static void Start(string version)
    {
        Write("app", new string('-', 60));
        Write("app", $"SaberBridge {version}, {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}, .NET {Environment.Version}");
        Write("app", $"exe {Environment.ProcessPath}; data {AppPaths.Data}; culture {System.Globalization.CultureInfo.CurrentUICulture.Name}");
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Write("crash", e.ExceptionObject?.ToString() ?? "unknown");
        TaskScheduler.UnobservedTaskException += (_, e) => Write("task", e.Exception.ToString());
        CleanUp();
        if (OperatingSystem.IsWindows()) Task.Run(Diagnostics.LogOwnModules);
    }

    // keep two weeks of journals and the last 40 Unity logs; a Unity log is a few MB each
    private static void CleanUp()
    {
        try
        {
            if (!Directory.Exists(AppPaths.Logs)) return;
            DirectoryInfo dir = new(AppPaths.Logs);
            foreach (FileInfo f in dir.GetFiles("app-*.log").Where(f => f.LastWriteTime < DateTime.Now.AddDays(-14))) f.Delete();
            foreach (FileInfo f in dir.GetFiles("unity-*.log").OrderByDescending(f => f.LastWriteTime).Skip(40)) f.Delete();
        }
        catch (Exception)
        {
            // a locked file stays for next time
        }
    }

    /// <summary>
    /// Packs what is needed to understand a problem into one zip on the desktop: the journals and setup logs of the
    /// last week, the latest Unity logs, the settings and a short system summary. Returns the zip's path.
    /// </summary>
    public static string Collect(string version)
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!Directory.Exists(desktop)) desktop = AppPaths.Data;
        string zipPath = Path.Combine(desktop, $"SaberBridge-logs-{DateTime.Now:yyyyMMdd-HHmmss}.zip");
        using ZipArchive zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        void Add(string file, string name)
        {
            try
            {
                // the app may be writing to it: copy through a shared read
                using FileStream src = new(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using Stream dst = zip.CreateEntry(name, CompressionLevel.Optimal).Open();
                src.CopyTo(dst);
            }
            catch (Exception e)
            {
                Write("logs", $"could not pack {file}: {e.Message}");
            }
        }

        if (Directory.Exists(AppPaths.Logs))
        {
            DirectoryInfo dir = new(AppPaths.Logs);
            DateTime week = DateTime.Now.AddDays(-7);
            foreach (FileInfo f in dir.GetFiles("*.log").Where(f => !f.Name.StartsWith("unity-") && f.LastWriteTime > week)) Add(f.FullName, f.Name);
            foreach (FileInfo f in dir.GetFiles("unity-*.log").OrderByDescending(f => f.LastWriteTime).Take(6)) Add(f.FullName, "unity/" + f.Name);
        }

        if (System.IO.File.Exists(AppPaths.SettingsFile)) Add(AppPaths.SettingsFile, "settings.json");
        foreach (string project in new[] { AppPaths.UnityProject, AppPaths.QuestProject })
        {
            string editorLog = Path.Combine(project, "Logs", "AssetImportWorker0.log");
            if (System.IO.File.Exists(editorLog)) Add(editorLog, $"projects/{Path.GetFileName(project)}-AssetImportWorker0.log");
        }

        using (StreamWriter w = new(zip.CreateEntry("system.txt").Open()))
        {
            w.WriteLine($"SaberBridge {version}");
            w.WriteLine($"{RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
            w.WriteLine($"Unity: {UnityLocator.Find(null) ?? "not found"}");
            w.WriteLine($"Unity Hub: {Tools.UnityHub() ?? "not found"}");
            w.WriteLine($"Unity license file: {(Tools.UnityLicenseFound() ? "found" : "none")}");
            w.WriteLine($"Quest project prepared: {ProjectSetup.IsQuestImported}; PC project prepared: {!ProjectSetup.IsFirstImport}");
            foreach (DriveInfo d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                w.WriteLine($"Drive {d.Name} free {d.AvailableFreeSpace / 1073741824.0:0.0} GB of {d.TotalSize / 1073741824.0:0.0} GB");
        }

        if (OperatingSystem.IsWindows())
        {
            void Text(string name, Func<string> make)
            {
                try
                {
                    using StreamWriter w = new(zip.CreateEntry(name).Open(), new UTF8Encoding(true));
                    w.Write(make());
                }
                catch (Exception e)
                {
                    Write("logs", $"could not write {name}: {e.Message}");
                }
            }

            // Unity's own logs of its last editor runs and of its Package Manager (packages that "cannot be found")
            string unityLogs = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "Editor");
            foreach (string name in new[] { "Editor.log", "Editor-prev.log", "upm.log" })
            {
                string f = Path.Combine(unityLogs, name);
                if (System.IO.File.Exists(f)) Add(f, "unity-editor/" + name);
            }

            // Windows' own crash records (why Unity's helpers die) and what else is loaded into processes
            Text("windows-crashes.txt", () => Diagnostics.CrashReport(150));
            Text("system-programs.txt", Diagnostics.SystemReport);
            if (Directory.Exists(Diagnostics.DumpFolder))
            {
                foreach (FileInfo f in new DirectoryInfo(Diagnostics.DumpFolder).GetFiles("*.dmp").OrderByDescending(f => f.LastWriteTime).Take(3))
                    Add(f.FullName, "dumps/" + f.Name);
            }
        }

        Write("logs", $"collected into {zipPath}");
        return zipPath;
    }
}
