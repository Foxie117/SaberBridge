using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SaberBridge.Core;

/// <summary>Which Unity editor builds avatars for which Beat Saber / Custom Avatars version.</summary>
public static class Compatibility
{
    // the converter scripts are written against this Custom Avatars release (its Unity project ships inside the app)
    public static readonly Version BundledCustomAvatars = new(5, 4, 11);
    public const string UnityVersion = "2021.3.16f1";
    public const string UnityChangeset = "4016570cf34f";

    public sealed record Row(string Game, string GameUnity, string CustomAvatars, string EditorUnity, bool Supported);

    // shown in the system check; Beat Saber moved to Unity 2021 with 1.30 and to 2022 with 1.38,
    // but Custom Avatars 5.x keeps building avatars with Unity 2021.3.16f1 for all of them
    public static readonly Row[] Table =
    {
        new("1.29.x and older", "2019.4", "5.2 and older", "2019.4.28f1", false),
        new("1.30 – 1.37", "2021.3", "5.3 – 5.4", UnityVersion, true),
        new("1.38 – 1.40", "2022.3", "5.4", UnityVersion, true),
    };

    public static string? GameVersion(string gameDir)
    {
        try
        {
            string file = Path.Combine(gameDir, "BeatSaberVersion.txt");
            if (File.Exists(file)) return File.ReadAllText(file).Trim().Split('_')[0];
        }
        catch (IOException)
        {
        }

        return null;
    }

    // the Unity version is in the header of globalgamemanagers, readable on any OS
    public static string? GameUnityVersion(string gameDir)
    {
        try
        {
            string file = Path.Combine(gameDir, "Beat Saber_Data", "globalgamemanagers");
            if (!File.Exists(file)) return null;
            byte[] head = new byte[4096];
            using FileStream fs = File.OpenRead(file);
            int n = fs.Read(head, 0, head.Length);
            Match m = Regex.Match(System.Text.Encoding.ASCII.GetString(head, 0, n), @"20\d\d\.\d+\.\d+[abfp]\d+");
            return m.Success ? m.Value : null;
        }
        catch (IOException)
        {
            return null;
        }
    }

    public static Version? CustomAvatarsVersion(string gameDir)
    {
        string dll = Path.Combine(gameDir, "Plugins", "CustomAvatar.dll");
        try
        {
            return File.Exists(dll) ? AssemblyName.GetAssemblyName(dll).Version : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public static class Tools
{
    public static string? UnityHub()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity Hub", "Unity Hub.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Unity Hub", "Unity Hub.exe"),
            }
            : new[] { "/usr/bin/unityhub", "/opt/unityhub/unityhub", Path.Combine(home, "Applications", "Unity Hub.AppImage"), Path.Combine(home, "Applications", "UnityHub.AppImage") };
        return candidates.FirstOrDefault(File.Exists);
    }

    public static string? BSManager()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] candidates = OperatingSystem.IsWindows()
            ? new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "bs-manager", "BSManager.exe") }
            : new[] { "/usr/bin/bs-manager", "/opt/BSManager/bs-manager", Path.Combine(home, "Applications", "BSManager.AppImage") };
        return candidates.FirstOrDefault(File.Exists);
    }

    // BSManager remembers where it keeps its game copies ("installation-folder" + /BSManager/BSInstances)
    public static string? BSManagerInstances()
    {
        string config = OperatingSystem.IsWindows()
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "bs-manager", "config.json")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "bs-manager", "config.json");
        try
        {
            if (File.Exists(config))
            {
                using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(config));
                if (doc.RootElement.TryGetProperty("installation-folder", out JsonElement f) && f.GetString() is { } dir)
                    return Path.Combine(dir, "BSManager", "BSInstances");
            }
        }
        catch (Exception)
        {
            // unreadable config: fall back to the default location
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "BSManager", "BSInstances");
    }

    public static bool UnityLicenseFound()
    {
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string[] files = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Unity", "Unity_lic.ulf"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Unity", "licenses", "UnityEntitlementLicense.xml"),
            }
            : new[]
            {
                Path.Combine(home, ".local", "share", "unity3d", "Unity", "Unity_lic.ulf"),
                Path.Combine(home, ".config", "unity3d", "Unity", "licenses", "UnityEntitlementLicense.xml"),
                Path.Combine(home, ".local", "share", "unity3d", "licenses", "UnityEntitlementLicense.xml"),
            };
        return files.Any(File.Exists);
    }

    // avatars are Windows asset bundles; the Linux editor needs the "Windows Build Support (Mono)" module for that
    public static bool HasWindowsBuildSupport(string unityExe)
    {
        string engines = Path.Combine(Path.GetDirectoryName(unityExe)!, "Data", "PlaybackEngines");
        return Directory.Exists(engines) && Directory.GetDirectories(engines).Any(d => Path.GetFileName(d).Equals("WindowsStandaloneSupport", StringComparison.OrdinalIgnoreCase));
    }

    public static void Open(string pathOrUrl)
    {
        try
        {
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(pathOrUrl) { UseShellExecute = true });
            else Process.Start("xdg-open", pathOrUrl);
        }
        catch (Exception)
        {
            // nothing useful to do if the desktop cannot open it
        }
    }
}

/// <summary>Official download locations; files go to the app's temp folder and the installer is started for the user.</summary>
public static class Downloads
{
    public const string UnityHubWindows = "https://public-cdn.cloud.unity3d.com/hub/prod/UnityHubSetup-x64.exe";
    public const string UnityHubLinuxDocs = "https://docs.unity3d.com/hub/manual/InstallHub.html#install-hub-linux";
    public const string UnityEditorWindows = $"https://download.unity3d.com/download_unity/{Compatibility.UnityChangeset}/Windows64EditorInstaller/UnitySetup64-{Compatibility.UnityVersion}.exe";
    public const string UnityAndroidWindows = $"https://download.unity3d.com/download_unity/{Compatibility.UnityChangeset}/TargetSupportInstaller/UnitySetup-Android-Support-for-Editor-{Compatibility.UnityVersion}.exe";
    public const string UnityPublisher = "Unity Technologies";
    public const string UnityArchive = "https://unity.com/releases/editor/whats-new/2021.3.16";
    public const string BSManagerReleases = "https://github.com/Zagrios/bs-manager/releases/latest";
    public const string CustomAvatarsPage = "https://github.com/nicoco007/BeatSaberCustomAvatars";

    // unityhub:// links open Unity Hub's install dialog for this exact editor (modules can be ticked there)
    public static string UnityHubInstallLink => $"unityhub://{Compatibility.UnityVersion}/{Compatibility.UnityChangeset}";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        HttpClient c = new() { Timeout = TimeSpan.FromHours(2) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SaberBridge", "0.2"));
        return c;
    }

    public static async Task<string> BSManagerInstallerUrl(CancellationToken ct)
    {
        using HttpResponseMessage r = await Http.GetAsync("https://api.github.com/repos/Zagrios/bs-manager/releases/latest", ct);
        r.EnsureSuccessStatusCode();
        using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
        foreach (JsonElement a in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            string name = a.GetProperty("name").GetString() ?? "";
            // the full installer, not the web stub ("BSManager-Web-Setup-x.exe")
            if (name.StartsWith("BSManager-Setup", StringComparison.OrdinalIgnoreCase) && name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                return a.GetProperty("browser_download_url").GetString()!;
        }

        throw new InvalidOperationException("no Windows installer in the latest BSManager release");
    }

    private const long Piece = 16L << 20;

    /// <summary>
    /// Downloads into the app's temp folder. Big files come in 16 MB ranges, each retried on its own, and a download
    /// cut off halfway resumes from its ".part" file, so a flaky network that drops long connections still gets there.
    /// With <paramref name="signer"/>, a Windows file must carry a valid signature from that publisher.
    /// </summary>
    public static async Task<string> FetchAsync(string url, IProgress<(long done, long? total)> progress, CancellationToken ct, string? signer = null, string? folder = null, string? saveAs = null, long expectedLength = 0)
    {
        string dir = folder ?? Path.Combine(AppPaths.Temp, "downloads");
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, saveAs ?? Path.GetFileName(new Uri(url).LocalPath));
        string part = file + ".part";

        long? total = await LengthAsync(url, ct);
        // a mirror must serve the very same file, or a resumed ".part" would mix two files
        if (expectedLength > 0 && total != null && total != expectedLength)
            throw new HttpRequestException($"{new Uri(url).Host} has a different file ({total} bytes, expected {expectedLength})");
        bool done = File.Exists(file) && total != null && new FileInfo(file).Length == total;
        if (!done)
        {
            if (total == null) await FetchWholeAsync(url, part, progress, ct);
            else await FetchRangesAsync(url, part, total.Value, progress, ct);
            File.Move(part, file, true);
        }

        if (signer != null && OperatingSystem.IsWindows() && !await SignedByAsync(file, signer))
        {
            File.Delete(file);
            throw new InvalidOperationException($"{Path.GetFileName(file)} is not signed by {signer}; deleted it");
        }

        return file;
    }

    // the size, but only when the server can send pieces of the file
    // throwOnNetwork: a network failure is an error to retry, not "this server sends no size"
    public static async Task<long?> LengthAsync(string url, CancellationToken ct, bool throwOnNetwork = false)
    {
        try
        {
            // a one-byte range request: more reliable than HEAD on Unity's CDN, and it proves ranges work
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                using HttpRequestMessage probe = new(HttpMethod.Get, url);
                probe.Headers.Range = new RangeHeaderValue(0, 0);
                using HttpResponseMessage r = await Http.SendAsync(probe, HttpCompletionOption.ResponseHeadersRead, ct);
                if (r.StatusCode == System.Net.HttpStatusCode.PartialContent && r.Content.Headers.ContentRange?.Length is long len) return len;
                if (r.IsSuccessStatusCode) return null;
                await Task.Delay(1000 * attempt, ct);
            }

            return null;
        }
        catch (HttpRequestException) when (!throwOnNetwork)
        {
            return null;
        }
    }

    private static async Task FetchRangesAsync(string url, string part, long total, IProgress<(long done, long? total)> progress, CancellationToken ct)
    {
        long have = File.Exists(part) ? new FileInfo(part).Length : 0;
        if (have > total) have = 0;
        await using FileStream dst = new(part, have > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write);
        byte[] buf = new byte[1 << 20];
        DateTime last = DateTime.MinValue;
        int failures = 0;

        while (have < total)
        {
            long end = Math.Min(have + Piece, total) - 1;
            try
            {
                using HttpRequestMessage req = new(HttpMethod.Get, url);
                req.Headers.Range = new RangeHeaderValue(have, end);
                using HttpResponseMessage r = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                r.EnsureSuccessStatusCode();
                if (r.StatusCode != System.Net.HttpStatusCode.PartialContent) throw new HttpRequestException("server ignored the range");
                await using Stream src = await r.Content.ReadAsStreamAsync(ct);
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    have += n;
                    if ((DateTime.Now - last).TotalMilliseconds > 200)
                    {
                        progress.Report((have, total));
                        last = DateTime.Now;
                    }
                }

                await dst.FlushAsync(ct);
                failures = 0;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException || (ex is TaskCanceledException && !ct.IsCancellationRequested))
            {
                // keep what arrived; a piece that broke halfway continues from the current length
                await dst.FlushAsync(ct);
                if (++failures > 12) throw;
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(30, 2 * failures)), ct);
            }
        }

        progress.Report((have, total));
    }

    private static async Task FetchWholeAsync(string url, string part, IProgress<(long done, long? total)> progress, CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                using HttpResponseMessage r = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
                r.EnsureSuccessStatusCode();
                long? total = r.Content.Headers.ContentLength;
                await using Stream src = await r.Content.ReadAsStreamAsync(ct);
                await using FileStream dst = File.Create(part);
                byte[] buf = new byte[1 << 20];
                long done = 0;
                int n;
                DateTime last = DateTime.MinValue;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if ((DateTime.Now - last).TotalMilliseconds > 200)
                    {
                        progress.Report((done, total));
                        last = DateTime.Now;
                    }
                }

                progress.Report((done, total));
                return;
            }
            catch (Exception ex) when (attempt < 4 && (ex is HttpRequestException or IOException || (ex is TaskCanceledException && !ct.IsCancellationRequested)))
            {
                await Task.Delay(TimeSpan.FromSeconds(3 * attempt), ct);
            }
        }
    }

    /// <summary>True when Windows reports a valid Authenticode signature whose signer names <paramref name="publisher"/>.</summary>
    public static async Task<bool> SignedByAsync(string file, string publisher)
    {
        string path = file.Replace("'", "''");
        ProcessStartInfo psi = new("powershell.exe")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            CreateNoWindow = true,
        };
        string command = "$s = Get-AuthenticodeSignature -LiteralPath '" + path + "'; \"$($s.Status)|$($s.SignerCertificate.Subject)\"";
        foreach (string a in new[] { "-NoProfile", "-NonInteractive", "-Command", command }) psi.ArgumentList.Add(a);
        using Process? p = Process.Start(psi);
        if (p == null) return false;
        string output = (await p.StandardOutput.ReadToEndAsync()).Trim();
        await p.WaitForExitAsync();
        string[] parts = output.Split('|', 2);
        return parts.Length == 2 && parts[0] == "Valid" && parts[1].Contains(publisher, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Runs several silent installers one after another behind a single admin prompt: a small script starts each with
    /// its raw arguments and waits for it. Returns false when the user declined the prompt.
    /// </summary>
    public static async Task<bool> RunInstallersElevatedAsync(IReadOnlyList<(string file, string args)> installers)
    {
        if (installers.Count == 0) return true;
        string script = Path.Combine(AppPaths.Temp, "downloads", "install.ps1");
        System.Text.StringBuilder sb = new();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        foreach ((string file, string args) in installers)
            sb.AppendLine("Start-Process -FilePath '" + file.Replace("'", "''") + "' -ArgumentList '" + args.Replace("'", "''") + "' -Wait");
        await File.WriteAllTextAsync(script, sb.ToString());

        ProcessStartInfo psi = new("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\"")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        try
        {
            using Process? p = Process.Start(psi);
            if (p != null) await p.WaitForExitAsync();
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // the user said no to the admin prompt
            return false;
        }
    }

    /// <summary>Starts an installer and waits for it; Windows asks for admin rights where the installer needs them.</summary>
    // arguments are passed raw: NSIS wants "/D=C:\Program Files\..." last and unquoted
    public static async Task RunInstallerAsync(string file, string? rawArgs = null)
    {
        ProcessStartInfo psi = new(file) { UseShellExecute = true, Arguments = rawArgs ?? "" };
        if (rawArgs != null) psi.Verb = "runas";
        using Process? p = Process.Start(psi);
        if (p != null) await p.WaitForExitAsync();
    }
}
