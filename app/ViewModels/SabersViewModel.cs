using System.Collections.ObjectModel;
using System.IO.Compression;
using Avalonia.Media;
using Avalonia.Media.Imaging;
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
    public string Badge => Entry.Kind == QuestKind.Note ? "NOTES" : "SABER";
    public IBrush BadgeBrush => Brush.Parse(Entry.Kind == QuestKind.Note ? "#18A999" : "#B068FF");

    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private IBrush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string _detail = "";
    [ObservableProperty] private bool _sourceMissing;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasCover))] private Bitmap? _cover;
    [ObservableProperty] private bool _isWorking;

    public bool HasCover => Cover != null;
    private string? _coverKey;

    // the last conversion error stays visible until the saber converts fine
    public string? Error { get; set; }

    public void Refresh()
    {
        SourceMissing = !Entry.SourceExists;
        LoadCover();
        if (IsWorking)
        {
            (StatusText, StatusBrush) = (L.Working, Brush.Parse("#4D9BFF"));
            Detail = Entry.SourcePath;
            return;
        }

        (StatusText, StatusBrush) = Entry.State switch
        {
            SaberState.Converted => (L.SaberConverted, Brush.Parse("#3DDC97")),
            SaberState.OutputMissing => (L.SaberOutputMissing, Brush.Parse("#FFB547")),
            _ => (L.SaberNotConverted, Brush.Parse("#8A90A8")),
        };
        if (Entry.State == SaberState.Converted) Error = null;
        Detail = SourceMissing ? L.SaberSourceMissing(Entry.SourcePath)
            : Error ?? (Entry.State == SaberState.Converted ? $"{Path.GetFileName(Entry.SourcePath)}  →  {Path.GetFileName(Entry.OutputPath)}" : Entry.SourcePath);
    }

    // the cover comes from the converted .whacker (thumbnail.png), so it shows what the Quest will show
    private void LoadCover()
    {
        string? path = Entry.State == SaberState.Converted ? Entry.OutputPath : null;
        string? key = path == null ? null : path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        if (key == _coverKey) return;
        _coverKey = key;
        Cover = null;
        if (path == null) return;
        try
        {
            using ZipArchive zip = ZipFile.OpenRead(path);
            ZipArchiveEntry? thumb = zip.GetEntry("thumbnail.png");
            if (thumb == null) return;
            using MemoryStream ms = new();
            using (Stream s = thumb.Open()) s.CopyTo(ms);
            ms.Position = 0;
            Cover = Bitmap.DecodeToWidth(ms, 96);
        }
        catch (Exception)
        {
            // a broken or locked file just shows no cover
        }
    }
}

/// <summary>
/// The Quest tab: PC sabers (.saber -> .whacker) and notes (.bloq -> .cyoob), with their state kept between runs.
/// The tab shows one kind at a time; Convert converts the pending entries of that kind.
/// </summary>
public partial class SabersViewModel : ObservableObject, IDisposable
{
    private readonly Settings _settings;
    private readonly SaberLibrary _library = SaberLibrary.Load();
    private FileSystemWatcher? _watcher;

    public SabersViewModel(Settings settings)
    {
        _settings = settings;
        _kind = settings.LastQuestKind == 1 ? QuestKind.Note : QuestKind.Saber;
        foreach (SaberEntry e in _library.Entries.OrderByDescending(e => e.AddedAt)) _all.Add(new SaberCard(e));
        Filter();
        WatchOutput();
        UpdateCounters();
        RefreshUnity();
    }

    private readonly List<SaberCard> _all = new();

    /// <summary>The entries of the kind the tab shows.</summary>
    public ObservableCollection<SaberCard> Cards { get; } = new();

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsSabers), nameof(IsNotes), nameof(DropTitle), nameof(DropHint), nameof(EmptyText))]
    private QuestKind _kind;

    public bool IsSabers => Kind == QuestKind.Saber;
    public bool IsNotes => Kind == QuestKind.Note;
    public string DropTitle => IsNotes ? L.NoteDropTitle : L.SaberDropTitle;
    public string DropHint => IsNotes ? L.NoteDropHint : L.SaberDropHint;
    public string EmptyText => IsNotes ? L.NotesEmpty : L.SabersEmpty;
    public string FilePattern => IsNotes ? "*.bloq" : "*.saber";

    [RelayCommand]
    private void SelectKind(string kind)
    {
        Kind = kind == "1" ? QuestKind.Note : QuestKind.Saber;
        _settings.LastQuestKind = IsNotes ? 1 : 0;
        _settings.Save();
    }

    partial void OnKindChanged(QuestKind value)
    {
        Filter();
        if (!IsBusy) Status = QuestUnity == null ? L.SaberNeedsUnity : L.SaberReady;
    }

    private void Filter()
    {
        Cards.Clear();
        foreach (SaberCard c in _all.Where(c => c.Entry.Kind == Kind)) Cards.Add(c);
        UpdateCounters();
        ConvertCommand.NotifyCanExecuteChanged();
    }

    public string OutputDir => string.IsNullOrWhiteSpace(_settings.QuestOutputDir) ? SaberLibrary.DefaultOutputDir : _settings.QuestOutputDir;
    public bool IsEmpty => Cards.Count == 0;
    public string Header => Cards.Count == 0 ? (IsNotes ? L.Notes : L.Sabers) : $"{(IsNotes ? L.Notes : L.Sabers)} · {Cards.Count}";

    [ObservableProperty] private string _summary = "";
    [ObservableProperty] private string _status = "";
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ConvertCommand))] private bool _isBusy;
    [ObservableProperty] private string? _questUnity;

    /// <summary>Looks for Unity 2021.3 with the Android module; without it the tab explains what to install.</summary>
    public void RefreshUnity()
    {
        string? exe = UnityLocator.FindQuest(_settings.UnityPath);
        QuestUnity = exe != null && UnityLocator.HasAndroid(exe) ? exe : null;
        if (!IsBusy) Status = QuestUnity == null ? L.SaberNeedsUnity : L.SaberReady;
        ConvertCommand.NotifyCanExecuteChanged();
    }

    public void AddFiles(IEnumerable<InputJob> jobs)
    {
        QuestKind? last = null;
        foreach (InputJob j in jobs.Where(j => j.Kind is JobKind.Saber or JobKind.Note))
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
            if (_all.All(c => c.Entry != entry)) _all.Insert(0, new SaberCard(entry));
            last = entry.Kind;
        }

        // show the kind that was just added
        if (last != null && last != Kind) Kind = last.Value;
        else Filter();
    }

    [RelayCommand]
    private void Remove(SaberCard card)
    {
        // only forgets the entry; the original .saber and any .whacker stay on disk
        _library.Remove(card.Entry);
        _all.Remove(card);
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
        foreach (SaberCard c in _all) c.Refresh();
        UpdateCounters();
        ConvertCommand.NotifyCanExecuteChanged();
    }

    private List<SaberCard> Todo() => Cards.Where(c => c.Entry.SourceExists && c.Entry.State != SaberState.Converted).ToList();

    private bool CanConvert() => !IsBusy && QuestUnity != null && Todo().Count > 0;

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task Convert()
    {
        List<SaberCard> todo = Todo();
        if (QuestUnity == null || todo.Count == 0) return;
        IsBusy = true;
        try
        {
            Status = ProjectSetup.IsQuestImported ? L.Phase("unity-start") : L.SaberFirstRun;
            await Task.Run(ProjectSetup.EnsureQuest);
            Directory.CreateDirectory(AppPaths.Temp);
            string method = IsNotes ? "SaberBridge.BloqToCyoob.Run" : "SaberBridge.SaberToWhacker.Run";
            string jobs = Path.Combine(AppPaths.Temp, $"quest-{Guid.NewGuid():N}.txt");
            await File.WriteAllLinesAsync(jobs, todo.Select(c => $"{c.Entry.SourcePath}|{OutputDir}"));

            int ok = 0, failed = 0;
            UnityResult r = await UnityRunner.RunAsync(QuestUnity, method, new[] { "-jobsFile", jobs }, line => Dispatcher.UIThread.Post(() =>
            {
                // "BEGIN 2/3 name.saber", "OK   name.saber -> C:\...\name.whacker", "FAIL name.saber: reason"
                if (line.StartsWith("BEGIN ")) Status = L.SaberConverting(line[6..]);
                SaberCard? card = todo.FirstOrDefault(c => line.Contains(Path.GetFileName(c.Entry.SourcePath)));
                if (card == null) return;
                card.IsWorking = line.StartsWith("BEGIN ") || line.StartsWith("STAGE ");
                if (line.StartsWith("OK ") && line.Split(" -> ") is [_, var path])
                {
                    card.Entry.OutputPath = path.Trim();
                    card.Entry.ConvertedAt = DateTime.Now;
                    ok++;
                }
                else if (line.StartsWith("FAIL "))
                {
                    card.Error = line[5..];
                    failed++;
                }

                card.Refresh();
                UpdateCounters();
            }), _ => { }, CancellationToken.None, AppPaths.QuestProject);

            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            ProjectSetup.MarkQuestImported();
            _library.Save();
            RefreshAll();
            Status = r.Problem ?? L.SaberFinished(ok, failed);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            _watcher = new FileSystemWatcher(OutputDir) { EnableRaisingEvents = true };
            _watcher.Filters.Add("*.whacker");
            _watcher.Filters.Add("*.cyoob");
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
