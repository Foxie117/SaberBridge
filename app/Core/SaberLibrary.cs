using System.Text.Json;

namespace SaberBridge.Core;

public enum SaberState { NotConverted, Converted, OutputMissing }

/// <summary>What a Quest entry is: a saber (.saber -> .whacker), a note set (.bloq -> .cyoob) or an avatar (.avatar -> .vrm for VRM Qavatars).</summary>
public enum QuestKind { Saber, Note, Avatar }

/// <summary>One PC saber the user added: where the original is and which Quest file was made from it.</summary>
public sealed class SaberEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
    public QuestKind Kind { get; set; }
    public string? OutputPath { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.Now;
    public DateTime? ConvertedAt { get; set; }

    public bool SourceExists => File.Exists(SourcePath);

    // a converted file the user deleted from the folder shows up as missing, so it can be made again
    public SaberState State => OutputPath == null ? SaberState.NotConverted : File.Exists(OutputPath) ? SaberState.Converted : SaberState.OutputMissing;
}

/// <summary>The saber list survives restarts (sabers.json in the app data folder); originals are never moved or changed.</summary>
public sealed class SaberLibrary
{
    private static string FilePath => Path.Combine(AppPaths.Data, "sabers.json");

    public List<SaberEntry> Entries { get; private set; } = new();

    public static string DefaultOutputDir => AppPaths.QuestRoot;

    /// <summary>Sabers and notes go to their own folders under the Quest output folder.</summary>
    public static string FolderFor(string root, QuestKind kind) => Path.Combine(root, kind switch
    {
        QuestKind.Note => "Notes",
        QuestKind.Avatar => "Avatars",
        _ => "Sabers",
    });

    public static QuestKind KindOf(string file) => Path.GetExtension(file).ToLowerInvariant() switch
    {
        ".bloq" => QuestKind.Note,
        ".avatar" => QuestKind.Avatar,
        _ => QuestKind.Saber,
    };

    public static SaberLibrary Load()
    {
        SaberLibrary lib = new();
        try
        {
            if (File.Exists(FilePath)) lib.Entries = JsonSerializer.Deserialize<List<SaberEntry>>(File.ReadAllText(FilePath)) ?? new();
        }
        catch (Exception)
        {
            // a broken list starts empty; the sabers themselves are untouched
        }

        return lib;
    }

    public void Save()
    {
        Directory.CreateDirectory(AppPaths.Data);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(Entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    public SaberEntry Add(string sourcePath)
    {
        string full = Path.GetFullPath(sourcePath);
        SaberEntry? existing = Entries.FirstOrDefault(e => string.Equals(e.SourcePath, full, StringComparison.OrdinalIgnoreCase));
        if (existing != null) return existing;

        QuestKind kind = KindOf(full);
        SaberEntry entry = new() { Name = Path.GetFileNameWithoutExtension(full), SourcePath = full, Kind = kind };
        Entries.Add(entry);
        Save();
        return entry;
    }

    public void Remove(SaberEntry entry)
    {
        Entries.Remove(entry);
        Save();
    }
}
