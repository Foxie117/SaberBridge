using System.Text.Json;

namespace SaberBridge.Core;

public enum SaberState { NotConverted, Converted, OutputMissing }

/// <summary>One PC saber the user added: where the original is and which Quest file was made from it.</summary>
public sealed class SaberEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string SourcePath { get; set; } = "";
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

    public static string DefaultOutputDir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SaberBridge", "Quest Sabers");

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

        SaberEntry entry = new() { Name = Path.GetFileNameWithoutExtension(full), SourcePath = full };
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
