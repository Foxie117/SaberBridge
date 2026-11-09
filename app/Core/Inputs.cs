using System.Formats.Tar;
using System.IO.Compression;

namespace SaberBridge.Core;

public enum JobKind { Vrm, Pmx, VrcPackage, Unsupported, Skipped, Saber, Note, Avatar }

public sealed record InputJob(JobKind Kind, string Name, string File, string Origin, string? Note = null);

/// <summary>Expands dropped files, folders and archives into conversion jobs.</summary>
public static class InputScanner
{
    private static readonly HashSet<string> Unsupported = new(StringComparer.OrdinalIgnoreCase) { ".blend", ".fbx", ".pmd", ".obj", ".glb", ".gltf", ".dae", ".bam" };

    public static List<InputJob> Scan(IEnumerable<string> paths)
    {
        List<InputJob> jobs = new();
        foreach (string path in paths) Visit(path, Path.GetFileName(path), jobs, 0, true);
        DropQuestDuplicates(jobs);
        return jobs;
    }

    private static void Visit(string path, string origin, List<InputJob> jobs, int depth, bool direct)
    {
        if (Directory.Exists(path))
        {
            foreach (string f in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories)) Visit(f, origin, jobs, depth, false);
            return;
        }

        if (!File.Exists(path)) return;
        string ext = Path.GetExtension(path).ToLowerInvariant();
        string name = Path.GetFileNameWithoutExtension(path);

        switch (ext)
        {
            case ".vrm":
                jobs.Add(new InputJob(JobKind.Vrm, name, path, origin));
                break;
            case ".pmx":
                jobs.Add(new InputJob(JobKind.Pmx, PmxName(path), path, origin));
                break;
            case ".saber":
                jobs.Add(new InputJob(JobKind.Saber, name, path, origin));
                break;
            case ".bloq":
                jobs.Add(new InputJob(JobKind.Note, name, path, origin));
                break;
            case ".avatar":
                // a PC avatar for the Quest (VRM Qavatars): .avatar -> .vrm
                jobs.Add(new InputJob(JobKind.Avatar, name, path, origin));
                break;
            case ".unitypackage":
                jobs.Add(new InputJob(JobKind.VrcPackage, name, path, origin));
                break;
            case ".zip" when depth < 3:
                string dir = Path.Combine(AppPaths.Temp, "zip-" + Guid.NewGuid().ToString("N")[..8]);
                ExtractZip(path, dir);
                int before = jobs.Count;
                Visit(dir, origin, jobs, depth + 1, false);
                if (jobs.Count == before && direct) jobs.Add(new InputJob(JobKind.Unsupported, name, path, origin, "no .vrm / .pmx / .unitypackage / .saber / .bloq inside"));
                break;
            default:
                // inside archives and folders other formats are just source files next to the real model
                if (direct && Unsupported.Contains(ext)) jobs.Add(new InputJob(JobKind.Unsupported, name, path, origin, ext + " is not supported yet"));
                break;
        }
    }

    // MMD models are often all called "model.pmx"; the folder name is more telling then
    private static string PmxName(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        string folder = Path.GetFileName(Path.GetDirectoryName(path)) ?? name;
        return name.Length <= 3 || name.Equals("model", StringComparison.OrdinalIgnoreCase) ? folder : name;
    }

    // VRChat archives usually carry a PC and a Quest package of the same avatar; the PC one converts better
    private static void DropQuestDuplicates(List<InputJob> jobs)
    {
        foreach (var group in jobs.Where(j => j.Kind == JobKind.VrcPackage).GroupBy(j => j.Origin).ToList())
        {
            var quest = group.Where(j => IsQuest(j.Name)).ToList();
            if (quest.Count == 0 || quest.Count == group.Count()) continue;
            foreach (InputJob j in quest)
            {
                jobs[jobs.IndexOf(j)] = j with { Kind = JobKind.Skipped, Note = "Quest version skipped (PC version used)" };
            }
        }
    }

    private static bool IsQuest(string name) => name.Contains("quest", StringComparison.OrdinalIgnoreCase) || name.Contains("android", StringComparison.OrdinalIgnoreCase);

    public static void ExtractZip(string zip, string dir)
    {
        string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
        using ZipArchive archive = ZipFile.OpenRead(zip);
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name)) continue;
            string dst = Path.GetFullPath(Path.Combine(root, SafeRelative(entry.FullName)));
            if (!dst.StartsWith(root, StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            entry.ExtractToFile(dst, true);
        }
    }

    internal static string SafeRelative(string p) =>
        Path.Combine(p.Replace('\\', '/').Split('/').Where(x => x is not ("" or "." or "..")).ToArray());
}

/// <summary>Unpacks the assets of a .unitypackage (never scripts, plugins or shaders) into a Unity project folder.</summary>
public static class UnityPackage
{
    private static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".dll", ".asmdef", ".asmref", ".shader", ".cginc", ".hlsl", ".compute", ".jslib", ".so",
        ".exe", ".bat", ".ps1", ".sh", ".py", ".js",
    };

    public static int Extract(string package, string assetsParent)
    {
        Dictionary<string, Dictionary<string, byte[]>> entries = new();
        using (FileStream fs = File.OpenRead(package))
        using (GZipStream gz = new(fs, CompressionMode.Decompress))
        using (TarReader tar = new(gz))
        {
            while (tar.GetNextEntry() is { } e)
            {
                if (e.DataStream == null || e.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile)) continue;
                string[] parts = e.Name.TrimStart('.', '/').Split('/', 2);
                if (parts.Length != 2 || parts[1] is not ("pathname" or "asset" or "asset.meta")) continue;
                using MemoryStream ms = new();
                e.DataStream.CopyTo(ms);
                if (!entries.TryGetValue(parts[0], out var map)) entries[parts[0]] = map = new();
                map[parts[1]] = ms.ToArray();
            }
        }

        string root = Path.GetFullPath(assetsParent) + Path.DirectorySeparatorChar;
        int n = 0;
        foreach (var map in entries.Values)
        {
            if (!map.TryGetValue("pathname", out byte[]? pn) || !map.TryGetValue("asset", out byte[]? asset)) continue;
            string path = System.Text.Encoding.UTF8.GetString(pn).Split('\n')[0].Trim().Replace('\\', '/');
            if (!path.StartsWith("Assets/") || Skip.Contains(Path.GetExtension(path))) continue;

            // "Assets/Foo/bar.png" lands in <assetsParent>/Foo/bar.png
            string dst = Path.GetFullPath(Path.Combine(root, InputScanner.SafeRelative(path["Assets/".Length..])));
            if (!dst.StartsWith(root, StringComparison.Ordinal)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.WriteAllBytes(dst, asset);
            if (map.TryGetValue("asset.meta", out byte[]? meta)) File.WriteAllBytes(dst + ".meta", meta);
            n++;
        }

        return n;
    }
}
