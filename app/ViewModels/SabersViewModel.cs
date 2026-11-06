using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Threading;
using SaberBridge.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SaberBridge.ViewModels;

public partial class SaberCard : ObservableObject
{
    public SaberCard(SaberEntry entry)
    {
        Entry = entry;
        Refresh();
    }

    public SaberEntry Entry { get; }
    public string Name => Entry.Name;
    public string SourcePath => Entry.SourcePath;

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private IBrush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _sourceMissing;

    public void Refresh()
    {
        SourceMissing = !Entry.SourceExists;
        (StatusText, StatusBrush) = Entry.State switch
        {
            SaberState.Converted => (L.SaberConverted, Brush.Parse("#3DDC97")),
            SaberState.OutputMissing => (L.SaberOutputMissing, Brush.Parse("#FFB547")),
            _ => (L.SaberNotConverted, Brush.Parse("#8A90A8")),
        };
        Detail = SourceMissing ? L.SaberSourceMissing(Entry.SourcePath) : Entry.SourcePath;
    }
}

/// <summary>The Quest tab: PC .saber files in, Quest .whacker files out, with their state kept between runs.</summary>
public partial class SabersViewModel : ObservableObject, IDisposable
{
    private readonly Settings _settings;
    private readonly SaberLibrary _library = SaberLibrary.Load();
    private FileSystemWatcher? _watcher;

    public SabersViewModel(Settings settings)
    {
        _settings = settings;
        foreach (SaberEntry e in _library.Entries.OrderByDescending(e => e.AddedAt)) Cards.Add(new SaberCard(e));
        WatchOutput();
        UpdateCounters();
    }

    public ObservableCollection<SaberCard> Cards { get; } = new();

    public string OutputDir => string.IsNullOrWhiteSpace(_settings.QuestOutputDir) ? SaberLibrary.DefaultOutputDir : _settings.QuestOutputDir;
    public bool IsEmpty => Cards.Count == 0;
    public string Header => Cards.Count == 0 ? L.Sabers : $"{L.Sabers} · {Cards.Count}";

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = L.SaberEngineNote;

    public void AddFiles(IEnumerable<InputJob> jobs)
    {
        foreach (InputJob j in jobs.Where(j => j.Kind == JobKind.Saber))
        {
            string source = j.File;

            // a saber unpacked from a .zip lives in the temp folder; keep our own copy so it can be converted later
            if (source.StartsWith(AppPaths.Temp, StringComparison.OrdinalIgnoreCase))
            {
                string keep = Path.Combine(AppPaths.Data, "saber-sources", Path.GetFileName(source));
                Directory.CreateDirectory(Path.GetDirectoryName(keep)!);
                File.Copy(source, keep, true);
                source = keep;
            }

            SaberEntry entry = _library.Add(source);
            if (Cards.All(c => c.Entry != entry)) Cards.Insert(0, new SaberCard(entry));
        }

        UpdateCounters();
    }

    [RelayCommand]
    private void Remove(SaberCard card)
    {
        // only forgets the entry; the original .saber and any .whacker stay on disk
        _library.Remove(card.Entry);
        Cards.Remove(card);
        UpdateCounters();
    }

    [RelayCommand]
    private void OpenOutput() => Tools.Open(Directory.CreateDirectory(OutputDir).FullName);

    [RelayCommand]
    private void ShowOriginal(SaberCard card)
    {
        string? dir = Path.GetDirectoryName(card.SourcePath);
        if (dir != null && Directory.Exists(dir)) Tools.Open(dir);
    }

    public void SetOutputDir(string dir)
    {
        _settings.QuestOutputDir = dir;
        _settings.Save();
        OnPropertyChanged(nameof(OutputDir));
        WatchOutput();
        RefreshAll();
    }

    [RelayCommand]
    private void RefreshAll()
    {
        foreach (SaberCard c in Cards) c.Refresh();
        UpdateCounters();
    }

    private bool CanConvert() => false;

    // the Quest build engine (Unity 2019.3 + Android) is the next step; the button is wired already
    [RelayCommand(CanExecute = nameof(CanConvert))]
    private Task Convert() => Task.CompletedTask;

    private void UpdateCounters()
    {
        int done = Cards.Count(c => c.Entry.State == SaberState.Converted);
        int missing = Cards.Count(c => c.Entry.State == SaberState.OutputMissing);
        Summary = Cards.Count == 0 ? "" : L.SaberSummary(Cards.Count, done, missing);
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(Header));
    }

    // deleting a .whacker in Explorer flips its card to "no converted file" without a restart
    private void WatchOutput()
    {
        _watcher?.Dispose();
        _watcher = null;
        try
        {
            Directory.CreateDirectory(OutputDir);
            _watcher = new FileSystemWatcher(OutputDir, "*.whacker") { EnableRaisingEvents = true };
            _watcher.Deleted += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
            _watcher.Created += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
            _watcher.Renamed += (_, _) => Dispatcher.UIThread.Post(RefreshAll);
        }
        catch (Exception)
        {
            // no watcher: the state still refreshes when the tab is opened
        }
    }

    public void Dispose() => _watcher?.Dispose();
}
