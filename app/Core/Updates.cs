using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;

namespace SaberBridge.Core;

/// <summary>A newer release on GitHub: its version, page and the file for this platform (with GitHub's SHA-256).</summary>
public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string? FileUrl, long FileSize, string? Sha256);

/// <summary>
/// Looks for a newer SaberBridge release on GitHub (at most once a day, silently on errors) and,
/// for the portable Windows exe, replaces the running file with the new one and restarts.
/// </summary>
public static class Updates
{
    public const string Repo = "Foxie117/SaberBridge";
    public static string ReleasesPage => $"https://github.com/{Repo}/releases/latest";

    // declared before Http: the client's User-Agent reads it
    public static Version Current { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        HttpClient c = new() { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SaberBridge", Current.ToString(3)));
        c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return c;
    }

    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static string AssetName => OperatingSystem.IsWindows() ? "SaberBridge-win-x64.exe" : "SaberBridge-linux-x64.tar.gz";

    /// <summary>Reached: GitHub answered. Info: the latest release when it is newer than this build.</summary>
    public static async Task<(bool Reached, UpdateInfo? Info)> LatestAsync(CancellationToken ct)
    {
        try
        {
            using HttpResponseMessage r = await Http.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            if (!r.IsSuccessStatusCode)
            {
                Log($"GitHub answered {(int)r.StatusCode}");
                return (false, null);
            }
            using JsonDocument doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync(ct));
            JsonElement root = doc.RootElement;
            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!Version.TryParse(tag.TrimStart('v', 'V'), out Version? v)) return (true, null);
            v = Normalize(v);
            if (v <= Current) return (true, null);

            string page = root.TryGetProperty("html_url", out JsonElement h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
            foreach (JsonElement a in root.GetProperty("assets").EnumerateArray())
            {
                if (a.GetProperty("name").GetString() != AssetName) continue;
                string? digest = a.TryGetProperty("digest", out JsonElement d) ? d.GetString() : null;
                string? sha = digest != null && digest.StartsWith("sha256:") ? digest[7..].ToLowerInvariant() : null;
                return (true, new UpdateInfo(v, tag, page, a.GetProperty("browser_download_url").GetString(), a.GetProperty("size").GetInt64(), sha));
            }

            return (true, new UpdateInfo(v, tag, page, null, 0, null));
        }
        catch (Exception e)
        {
            // offline, blocked or GitHub changed its answer: no note, try again another day
            Log(e.GetType().Name + ": " + e.Message);
            return (false, null);
        }
    }

    /// <summary>
    /// The running file can be swapped in place: the portable Windows exe (not "dotnet run"), in a folder this user can write to,
    /// and the release has the file with its checksum.
    /// </summary>
    public static bool CanSelfUpdate(UpdateInfo info)
    {
        if (!OperatingSystem.IsWindows() || info.FileUrl == null || info.Sha256 == null) return false;
        string? exe = Environment.ProcessPath;
        if (exe == null || !exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(exe).Equals("dotnet.exe", StringComparison.OrdinalIgnoreCase)) return false;
        // a dev build next to its DLLs is not the single-file app
        if (File.Exists(Path.Combine(Path.GetDirectoryName(exe)!, "SaberBridge.dll"))) return false;
        return CanWrite(Path.GetDirectoryName(exe)!);
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            string probe = Path.Combine(dir, $".saberbridge-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Downloads the new exe, checks GitHub's SHA-256, puts it where the running exe is and starts it. The caller then quits.</summary>
    public static async Task InstallAsync(UpdateInfo info, IProgress<(long done, long? total)> progress, CancellationToken ct)
    {
        string exe = Environment.ProcessPath!;
        string dir = Path.Combine(AppPaths.Temp, "update");
        string file = await Downloads.FetchAsync(info.FileUrl!, progress, ct, folder: dir, saveAs: $"SaberBridge-{info.Version.ToString(3)}.exe", expectedLength: info.FileSize);

        string actual;
        await using (FileStream s = File.OpenRead(file))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(s, ct)).ToLowerInvariant();
        if (actual != info.Sha256)
        {
            File.Delete(file);
            throw new InvalidDataException(L.UpdateBadChecksum);
        }

        // Windows lets a running exe be renamed, not overwritten: move it aside, put the new one in its place
        string old = exe + ".old";
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(file, exe);
        }
        catch (Exception)
        {
            File.Move(old, exe);
            throw;
        }

        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe)! });
    }

    private static void Log(string line)
    {
        try
        {
            Directory.CreateDirectory(AppPaths.Logs);
            File.AppendAllText(Path.Combine(AppPaths.Logs, "update.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // logging must never break the check
        }
    }

    /// <summary>After an update: remove the previous exe once its process has exited.</summary>
    public static void CleanUpOld()
    {
        string? exe = Environment.ProcessPath;
        if (exe == null || !File.Exists(exe + ".old")) return;
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 30; i++)
            {
                try
                {
                    File.Delete(exe + ".old");
                    return;
                }
                catch (Exception)
                {
                    await Task.Delay(1000);
                }
            }
        });
    }
}
