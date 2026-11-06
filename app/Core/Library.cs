using System.Security.Cryptography;
using System.Text.Json;

namespace SaberBridge.Core;

public sealed record CheckResult(string Code, string Level, string Value);

/// <summary>What the Unity preview renderer wrote for one .avatar (see AvatarPreview.cs).</summary>
public sealed class PreviewData
{
    public required string Dir { get; init; }
    public string? Name { get; init; }
    public string? Author { get; init; }
    public double Height { get; init; }
    public int Width { get; init; }
    public int HeightPx { get; init; }
    public int Frames { get; init; }
    public List<CheckResult> Checks { get; init; } = new();

    // animation -> frame -> segment [x1, y1, x2, y2, kind]
    public Dictionary<string, float[][][]> Anims { get; init; } = new();

    public string Frame(string anim, int i) => Path.Combine(Dir, $"{anim}_{i:00}.jpg");

    public string WorstLevel => Checks.Any(c => c.Level == "fail") ? "fail" : Checks.Any(c => c.Level == "warn") ? "warn" : Checks.Count > 0 ? "ok" : "none";

    public static PreviewData? Load(string dir)
    {
        string file = Path.Combine(dir, "preview.json");
        if (!File.Exists(file)) return null;
        try
        {
            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement r = doc.RootElement;
            Dictionary<string, float[][][]> anims = new();
            if (r.TryGetProperty("anims", out JsonElement a))
            {
                foreach (JsonProperty p in a.EnumerateObject())
                {
                    anims[p.Name] = p.Value.EnumerateArray()
                        .Select(f => f.EnumerateArray().Select(s => s.EnumerateArray().Select(v => v.GetSingle()).ToArray()).ToArray()).ToArray();
                }
            }

            return new PreviewData
            {
                Dir = dir,
                Name = r.TryGetProperty("name", out JsonElement n) ? n.GetString() : null,
                Author = r.TryGetProperty("author", out JsonElement au) ? au.GetString() : null,
                Height = r.TryGetProperty("height", out JsonElement h) ? h.GetDouble() : 0,
                Width = r.GetProperty("width").GetInt32(),
                HeightPx = r.GetProperty("height_px").GetInt32(),
                Frames = r.GetProperty("frames").GetInt32(),
                Checks = r.GetProperty("checks").EnumerateArray()
                    .Select(c => new CheckResult(c.GetProperty("code").GetString()!, c.GetProperty("level").GetString()!, c.GetProperty("value").GetString() ?? "")).ToList(),
                Anims = anims,
            };
        }
        catch (Exception)
        {
            return null;
        }
    }
}

public static class PreviewCache
{
    public static string Root => Path.Combine(AppPaths.Data, "previews");
    public static string DirFor(string hash) => Path.Combine(Root, hash);
    public static string? Cover(string hash) => File.Exists(Path.Combine(DirFor(hash), "cover.png")) ? Path.Combine(DirFor(hash), "cover.png") : null;
    public static bool Has(string hash) => File.Exists(Path.Combine(DirFor(hash), "preview.json"));

    // same key as AvatarPreview.Hash in Unity: SHA1 of the file, upper-case hex
    public static string Hash(string file)
    {
        using FileStream fs = File.OpenRead(file);
        return Convert.ToHexString(SHA1.HashData(fs));
    }

    public static async Task RenderAsync(string unity, IReadOnlyList<string> files, Action<string> onLine, CancellationToken ct)
    {
        Directory.CreateDirectory(AppPaths.Temp);
        string list = Path.Combine(AppPaths.Temp, $"preview-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(list, files, ct);
        UnityResult r = await UnityRunner.RunAsync(unity, "VRM2BS.AvatarPreview.Run", new[] { "-avatarList", list, "-previewDir", Root }, onLine, _ => { }, ct);
        if (r.Problem != null) throw new InvalidOperationException(r.Problem);
    }
}

/// <summary>Deleting avatars always goes through the recycle bin, so a wrong click can be undone.</summary>
public static class Trash
{
    public static void Move(string file)
    {
        if (!File.Exists(file)) return;
        if (OperatingSystem.IsWindows())
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(file, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
            return;
        }

        // freedesktop.org trash: ~/.local/share/Trash/{files,info}
        string trash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "Trash");
        Directory.CreateDirectory(Path.Combine(trash, "files"));
        Directory.CreateDirectory(Path.Combine(trash, "info"));
        string name = Path.GetFileName(file);
        for (int i = 2; File.Exists(Path.Combine(trash, "files", name)); i++) name = $"{Path.GetFileNameWithoutExtension(file)}.{i}{Path.GetExtension(file)}";
        File.WriteAllText(Path.Combine(trash, "info", name + ".trashinfo"),
            $"[Trash Info]\nPath={Uri.EscapeDataString(Path.GetFullPath(file)).Replace("%2F", "/")}\nDeletionDate={DateTime.Now:yyyy-MM-ddTHH:mm:ss}\n");
        File.Move(file, Path.Combine(trash, "files", name));
    }
}
