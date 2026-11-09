using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Threading;
using SaberBridge.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SaberBridge.ViewModels;

public partial class MainViewModel : ObservableObject, IConversionListener
{
    private readonly Settings _settings = Settings.Load();
    private readonly StringBuilder _log = new();
    private readonly List<AvatarCard> _builtThisRun = new();
    private CancellationTokenSource? _cts;
    private string? _lastOutDir;

    public MainViewModel()
    {
        _autoInstall = _settings.AutoInstall;
        SystemCheck = new SystemCheckViewModel(this);
        Sabers = new SabersViewModel(_settings) { Game = () => SelectedGame };
        Sabers.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SabersViewModel.Pending)) OnPropertyChanged(nameof(QuestTabTitle));
            if (e.PropertyName == nameof(SabersViewModel.HasAvatars)) OnPropertyChanged(nameof(ShowGamePicker));
        };
        Queue.CollectionChanged += (_, e) =>
        {
            foreach (QueueItem q in e.NewItems?.OfType<QueueItem>() ?? Enumerable.Empty<QueueItem>())
                q.PropertyChanged += (_, pe) => { if (pe.PropertyName == nameof(QueueItem.State)) OnPropertyChanged(nameof(PcTabTitle)); };
            OnPropertyChanged(nameof(PcTabTitle));
        };
        Games.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasGames));
        // without Beat Saber on this PC the PC tab has nothing to do, so the app opens on Quest
        bool hasGame = GameLocator.FindAll(_settings.GamePath).Any();
        _tab = _settings.LastTab == 1 || !hasGame ? 1 : 0;
        Tools.UnityHubOverride = _settings.UnityHubPath;
        Setup = new SetupViewModel(this, _settings);
        UnityRunner.HiddenWindowMode = _settings.UnityHiddenWindow;
        UnityRunner.HiddenWindowModeChanged += on =>
        {
            _settings.UnityHiddenWindow = on;
            _settings.Save();
        };
        RefreshEnvironment();
        // Unity gets set up by itself in the background; the system check opens only for what that cannot fix
        Setup.StartIfNeeded();
        if (Setup.IsVisible) Status = Sabers.Status = L.SetupWaitStatus;
        ShowSystemCheck = NeedsSystemCheck;
        Updater = new UpdateViewModel(_settings, () => IsBusy || Sabers.IsBusy || SetupViewModel.Busy);
        Updater.Start();
    }

    public UpdateViewModel Updater { get; }

    // The system check opens by itself only when the open tab has something to convert and lacks what it needs;
    // the header chip ("Needs attention") opens it any time.
    private bool NeedsSystemCheck => SystemCheck.Blocked && !Setup.IsVisible && (IsQuestTab ? Sabers.Cards.Count > 0 : Queue.Count > 0);

    public SetupViewModel Setup { get; }

    public SystemCheckViewModel SystemCheck { get; }
    public SabersViewModel Sabers { get; }

    // 0 = PC (avatars), 1 = Quest (sabers and notes); each tab has its own requirements in the system check
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsAvatarsTab), nameof(IsQuestTab), nameof(UnityReady), nameof(SystemIntro), nameof(ShowGamePicker))] private int _tab;

    /// <summary>The game picker: always on the PC tab, on the Quest tab when .avatar files wait there.</summary>
    public bool ShowGamePicker => IsAvatarsTab || Sabers.HasAvatars;
    public bool IsAvatarsTab => Tab == 0;
    public bool IsQuestTab => Tab == 1;
    public string SystemIntro => IsQuestTab ? L.SystemIntroQuest : L.SystemIntro;

    /// <summary>The header's Unity dot: the PC tab needs the Windows module, the Quest tab the Android module.</summary>
    public bool UnityReady => IsQuestTab ? UnityPath != null && UnityLocator.HasAndroid(UnityPath) : UnityOk;

    partial void OnTabChanged(int value)
    {
        _settings.LastTab = value;
        _settings.Save();
        if (IsQuestTab)
        {
            Sabers.RefreshAllCommand.Execute(null);
            Sabers.RefreshUnity();
        }

        SystemCheck.Refresh();
    }

    [RelayCommand]
    private void SelectTab(string index)
    {
        Tab = int.Parse(index);
    }
    public ObservableCollection<QueueItem> Queue { get; } = new();
    public ObservableCollection<GameInstall> Games { get; } = new();
    public bool HasGames => Games.Count > 0;

    // all library cards, and the sorted / filtered list the view shows
    public List<AvatarCard> Avatars { get; } = new();
    public ObservableCollection<AvatarCard> VisibleAvatars { get; } = new();

    /// <summary>Set by the view: asks a yes/no question (title, text) and returns the answer.</summary>
    public Func<string, string, Task<bool>>? Confirm { get; set; }

    /// <summary>Set by the view: opens the preview window for a card.</summary>
    public Action<AvatarCard>? OpenPreviewWindow { get; set; }

    /// <summary>Set by the view: brings the top of the library (where new avatars land) into view.</summary>
    public Action? ScrollLibraryToTop { get; set; }

    /// <summary>Set by the view: folder picker for the game.</summary>
    public Func<Task<string?>>? PickFolder { get; set; }

    [ObservableProperty] private GameInstall? _selectedGame;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UnityReady))] private string? _unityPath;
    [ObservableProperty] private string _unityText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(UnityReady))] private bool _unityOk;
    [ObservableProperty] private bool _gameOk;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ConvertCommand), nameof(PreviewAllCommand))] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressIndeterminate;
    [ObservableProperty] private string _status = L.Ready;
    [ObservableProperty] private string _subStatus = "";
    [ObservableProperty] private string _percentText = "";
    [ObservableProperty] private string _elapsed = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _showLog;
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _showSystemCheck;
    [ObservableProperty] private bool _autoInstall;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PreviewAllLabel), nameof(HasMissingPreviews))] private int _missingPreviews;

    public bool QueueEmpty => Queue.Count == 0;

    // each mode keeps its own queue; the tab buttons show how much waits there
    private int PcPending => Queue.Count(q => q.State is JobState.Waiting or JobState.Working or JobState.Failed);
    public string PcTabTitle => PcPending > 0 ? $"{L.TabPc}  ·  {PcPending}" : L.TabPc;
    public string QuestTabTitle => Sabers.Pending > 0 ? $"{L.TabQuest}  ·  {Sabers.Pending}" : L.TabQuest;
    public bool LibraryEmpty => Avatars.Count == 0;
    public string LibraryHeader => Avatars.Count == 0 ? L.Library : $"{L.Library} · {Avatars.Count}";
    public bool HasNewToInstall => Avatars.Any(a => a.IsNew && a.CanInstall);
    public string PreviewAllLabel => L.PreviewAll(MissingPreviews);
    public bool HasMissingPreviews => MissingPreviews > 0;
    public string LanguageCode => L.Ru ? "RU" : "EN";

    partial void OnAutoInstallChanged(bool value)
    {
        _settings.AutoInstall = value;
        _settings.Save();
    }

    partial void OnSearchTextChanged(string value) => RefreshVisible();

    partial void OnSelectedGameChanged(GameInstall? value)
    {
        GameOk = value is { HasCustomAvatars: true };
        if (value != null && value.Path != _settings.GamePath)
        {
            _settings.GamePath = value.Path;
            _settings.Save();
        }

        SystemCheck?.Refresh();
        ConvertCommand.NotifyCanExecuteChanged();
        _ = LoadLibraryAsync();
    }

    public void RefreshEnvironment()
    {
        UnityPath = UnityLocator.Find(_settings.UnityPath);
        UnityOk = UnityPath != null && Tools.HasWindowsBuildSupport(UnityPath);
        UnityText = UnityPath == null ? L.UnityMissing : $"Unity {UnityLocator.VersionOf(UnityPath) ?? ""}";
        // a module installed into the same folder (Android) leaves the path as it was: recount the dot anyway
        OnPropertyChanged(nameof(UnityReady));
        Sabers.RefreshUnity();

        GameInstall? before = SelectedGame;
        Games.Clear();
        foreach (GameInstall g in GameLocator.FindAll(_settings.GamePath)) Games.Add(g);
        SelectedGame = Games.FirstOrDefault(g => g.Path == _settings.GamePath) ?? Games.FirstOrDefault();
        if (SelectedGame == before) OnSelectedGameChanged(SelectedGame);
        SystemCheck.Refresh();
        ConvertCommand.NotifyCanExecuteChanged();
        PreviewAllCommand.NotifyCanExecuteChanged();
    }

    public void SetUnity(string exe)
    {
        _settings.UnityPath = exe;
        _settings.Save();
        RefreshEnvironment();
    }

    public void SetGame(string dir)
    {
        _settings.GamePath = dir;
        _settings.Save();
        RefreshEnvironment();
    }

    public async Task PickGameAsync()
    {
        if (PickFolder != null && await PickFolder() is { } dir) SetGame(dir);
    }

    // ---------------------------------------------------------------- queue

    /// <param name="openTheirTab">files the app was started with: open the tab they belong to when they are all one kind</param>
    public async Task AddPathsAsync(IEnumerable<string> paths, bool openTheirTab = false)
    {
        string before = Status;
        Status = L.Scanning;
        try
        {
            List<InputJob> jobs = await Task.Run(() => InputScanner.Scan(paths.ToList()));

            // every file goes to the queue of its mode (sabers and notes: Quest, models: PC), wherever it was
            // dropped; the open tab stays, and a note says what went to the other one
            List<InputJob> quest = jobs.Where(j => j.Kind is JobKind.Saber or JobKind.Note or JobKind.Avatar).ToList();
            if (quest.Count > 0) Sabers.AddFiles(quest);

            int pc = 0;
            foreach (InputJob job in jobs.Where(j => j.Kind is not (JobKind.Saber or JobKind.Note or JobKind.Avatar)))
            {
                if (Queue.Any(q => q.Job.File == job.File)) continue;
                Queue.Add(new QueueItem(job));
                pc++;
            }

            if (openTheirTab && (quest.Count > 0) != (pc > 0)) Tab = quest.Count > 0 ? 1 : 0;

            int other = IsQuestTab ? pc : quest.Count;
            if (other > 0)
            {
                Status = L.AddedToOtherQueue(other, IsQuestTab ? L.TabPc : L.TabQuest);
                if (IsQuestTab) Sabers.Status = Status;
                before = Status;
            }
        }
        catch (Exception ex)
        {
            AppendLog("⚠ " + ex.Message);
        }

        Status = before;
        OnPropertyChanged(nameof(QueueEmpty));
        ConvertCommand.NotifyCanExecuteChanged();
        if (NeedsSystemCheck) ShowSystemCheck = true;
    }

    [RelayCommand]
    private void Remove(QueueItem item)
    {
        if (IsBusy) return;
        Queue.Remove(item);
        OnPropertyChanged(nameof(QueueEmpty));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ClearQueue()
    {
        if (IsBusy) return;
        Queue.Clear();
        OnPropertyChanged(nameof(QueueEmpty));
        ConvertCommand.NotifyCanExecuteChanged();
    }

    // ---------------------------------------------------------------- conversion

    private bool CanConvert() => !IsBusy && !SetupViewModel.Busy && !SystemCheck.Blocked && UnityOk && GameOk && Queue.Any(q => q.State is JobState.Waiting or JobState.Failed);

    [RelayCommand(CanExecute = nameof(CanConvert))]
    private async Task ConvertAsync()
    {
        List<QueueItem> todo = Queue.Where(q => q.State is JobState.Waiting or JobState.Failed).ToList();
        if (todo.Count == 0 || UnityPath == null || SelectedGame == null)
        {
            Status = L.NothingToDo;
            return;
        }

        foreach (QueueItem q in todo) q.SetState(JobState.Waiting, null);
        _builtThisRun.Clear();
        Converter converter = new(UnityPath, SelectedGame, this);
        _lastOutDir = converter.OutDir;

        await RunBusyAsync(async ct =>
        {
            await converter.RunAsync(todo.Select(q => q.Job).ToList(), ct);

            // let the queued result/state updates from the worker land first
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            string summary = L.Finished(_builtThisRun.Count, Queue.Count(q => q.State == JobState.Failed));
            if (AutoInstall && _builtThisRun.Count > 0) summary += "  ·  " + Install(_builtThisRun.ToList());
            if (Queue.Any(q => q.State == JobState.Failed)) ShowLog = true;
            return summary;
        }, () =>
        {
            foreach (QueueItem q in todo.Where(q => q.State == JobState.Working)) q.SetState(JobState.Waiting, null);
        });
    }

    /// <summary>Shared busy/timer/cancel handling for anything that runs Unity.</summary>
    private async Task RunBusyAsync(Func<CancellationToken, Task<string>> work, Action? onCancel = null)
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Elapsed = clock.Elapsed.ToString(clock.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        timer.Start();
        string finalStatus = L.Ready;

        try
        {
            finalStatus = await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            finalStatus = L.Cancelled;
            onCancel?.Invoke();
        }
        catch (Exception ex)
        {
            finalStatus = ex.Message;
            AppendLog("⚠ " + ex);
            ShowLog = true;
            foreach (QueueItem q in Queue.Where(q => q.State == JobState.Working)) q.SetState(JobState.Failed, ex.Message);
        }
        finally
        {
            timer.Stop();
            IsBusy = false;
            ProgressIndeterminate = false;
            SubStatus = "";
            PercentText = "";
            _cts = null;
            Status = finalStatus;
            UpdateLibraryCounters();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    // ---------------------------------------------------------------- library

    public async Task LoadLibraryAsync()
    {
        string? dir = SelectedGame?.AvatarsDir;
        Dictionary<string, string> installed = new(StringComparer.OrdinalIgnoreCase);
        if (dir != null && Directory.Exists(dir))
        {
            foreach (string f in Directory.GetFiles(dir, "*.avatar")) installed[Path.GetFileName(f)] = f;
        }

        // drop cards of avatars that left the game folder (unless converted in this session)
        foreach (AvatarCard c in Avatars.ToList())
        {
            if (installed.TryGetValue(c.FileName, out string? path)) c.InstalledFile = path;
            else if (c.OutputFile != null) c.InstalledFile = null;
            else Avatars.Remove(c);
        }

        List<AvatarCard> added = new();
        foreach (var (name, path) in installed)
        {
            if (Avatars.Any(c => c.FileName.Equals(name, StringComparison.OrdinalIgnoreCase))) continue;
            AvatarCard card = new(name) { InstalledFile = path, AddedAt = File.GetLastWriteTime(path) };
            Avatars.Add(card);
            added.Add(card);
        }

        RefreshVisible();
        foreach (AvatarCard c in added) await c.LoadMetaAsync();
        UpdateLibraryCounters();
    }

    private void RefreshVisible()
    {
        IEnumerable<AvatarCard> q = Avatars;
        if (!string.IsNullOrWhiteSpace(SearchText)) q = q.Where(a => a.Name.Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));

        // fresh conversions first, newest on top; then the game's avatars by name
        List<AvatarCard> sorted = q.OrderByDescending(a => a.IsNew).ThenByDescending(a => a.IsNew ? a.AddedAt : DateTime.MinValue)
            .ThenBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        VisibleAvatars.Clear();
        foreach (AvatarCard a in sorted) VisibleAvatars.Add(a);
        OnPropertyChanged(nameof(LibraryEmpty));
        OnPropertyChanged(nameof(LibraryHeader));
    }

    private void UpdateLibraryCounters()
    {
        MissingPreviews = Avatars.Count(a => !a.HasPreview && a.PreviewFile != null);
        OnPropertyChanged(nameof(HasNewToInstall));
        PreviewAllCommand.NotifyCanExecuteChanged();
    }

    private bool CanPreviewAll() => !IsBusy && UnityPath != null && MissingPreviews > 0;

    [RelayCommand(CanExecute = nameof(CanPreviewAll))]
    private async Task PreviewAllAsync() => await MakePreviewsAsync(Avatars.Where(a => !a.HasPreview && a.PreviewFile != null).ToList());

    public async Task MakePreviewsAsync(List<AvatarCard> cards)
    {
        if (UnityPath == null || cards.Count == 0 || IsBusy) return;
        string unity = UnityPath;
        GameInstall? game = SelectedGame;

        await RunBusyAsync(async ct =>
        {
            Status = L.MakingPreviews(1, cards.Count);
            SubStatus = ProjectSetup.IsFirstImport ? L.FirstRunHint : L.Phase("unity-start");
            ProgressIndeterminate = true;
            PercentText = "";
            if (game != null) await Task.Run(() => ProjectSetup.Ensure(game), ct);

            await PreviewCache.RenderAsync(unity, cards.Select(c => c.PreviewFile!).ToList(), line => Dispatcher.UIThread.Post(() =>
            {
                AppendLog(line);
                // "PREVIEW 3/12 name.avatar"
                string[] parts = line.Split(' ', 3);
                if (parts.Length >= 2 && parts[0] == "PREVIEW" && parts[1].Split('/') is [var a, var b] && int.TryParse(a, out int i) && int.TryParse(b, out int n))
                {
                    ProgressIndeterminate = false;
                    Progress = 100.0 * (i - 1) / n;
                    PercentText = $"{Progress:0}%";
                    Status = L.MakingPreviews(i, n);
                    SubStatus = parts.ElementAtOrDefault(2) ?? "";
                }
            }), ct);

            foreach (AvatarCard c in cards) await c.LoadMetaAsync();
            return L.PreviewsDone;
        });
    }

    [RelayCommand]
    private void OpenPreview(AvatarCard card) => OpenPreviewWindow?.Invoke(card);

    public async Task OpenAvatarFileAsync(string file)
    {
        if (!File.Exists(file)) return;
        string full = Path.GetFullPath(file);
        AvatarCard? card = Avatars.FirstOrDefault(a => string.Equals(a.PreviewFile, full, StringComparison.OrdinalIgnoreCase));
        if (card == null)
        {
            card = new AvatarCard(Path.GetFileName(full)) { OutputFile = full };
            await card.LoadMetaAsync();
        }

        OpenPreviewWindow?.Invoke(card);
    }

    [RelayCommand]
    private void InstallCard(AvatarCard card) => Status = Install(new List<AvatarCard> { card });

    [RelayCommand]
    private void InstallAllNew() => Status = Install(Avatars.Where(a => a.IsNew && a.CanInstall).ToList());

    public string Install(List<AvatarCard> cards)
    {
        if (SelectedGame == null || cards.Count == 0) return Status;
        try
        {
            Directory.CreateDirectory(SelectedGame.AvatarsDir);
            foreach (AvatarCard c in cards.Where(c => c.OutputFile != null))
            {
                string dst = Path.Combine(SelectedGame.AvatarsDir, c.FileName);
                File.Copy(c.OutputFile!, dst, true);
                c.InstalledFile = dst;
                c.InstalledIsCurrent = true;
            }

            OnPropertyChanged(nameof(HasNewToInstall));
            return L.InstalledTo(cards.Count, SelectedGame.AvatarsDir);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    [RelayCommand]
    public async Task DeleteCard(AvatarCard card)
    {
        string? target = card.InstalledFile ?? card.OutputFile;
        if (target == null || IsBusy) return;
        string text = card.InstalledFile != null ? L.DeleteFromGame(card.Name) : L.DeleteConverted(card.Name);
        if (Confirm != null && !await Confirm(L.DeleteTitle, text)) return;

        try
        {
            Trash.Move(target);
            if (target == card.InstalledFile)
            {
                card.InstalledFile = null;
                card.InstalledIsCurrent = true;
            }
            else
            {
                card.OutputFile = null;
            }

            if (card.InstalledFile == null && card.OutputFile == null) Avatars.Remove(card);
            RefreshVisible();
            UpdateLibraryCounters();
            Status = L.Deleted(card.Name);
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    // ---------------------------------------------------------------- misc commands

    [RelayCommand]
    private void OpenOutput() => Tools.Open(Directory.CreateDirectory(_lastOutDir ?? AppPaths.PcAvatars).FullName);

    [RelayCommand]
    private void OpenGameAvatars()
    {
        if (SelectedGame != null) Tools.Open(Directory.CreateDirectory(SelectedGame.AvatarsDir).FullName);
    }

    [RelayCommand]
    private void OpenDataFolder() => Tools.Open(Directory.CreateDirectory(AppPaths.Data).FullName);

    /// <summary>Set by the app: replaces the main window with a fresh one (its fixed texts are read when it is built).</summary>
    public Action? RebuildWindow { get; set; }

    [RelayCommand]
    private void SetLanguage(string code)
    {
        if ((code == "ru") == L.Ru) return;
        _settings.Language = code;
        _settings.Save();

        // no restart: the queue, the library and a running conversion stay; the window is rebuilt in the new language
        L.Use(code);
        RebuildWindow?.Invoke();
        if (!IsBusy) Status = Setup.IsVisible ? L.SetupWaitStatus : L.Ready;
        Sabers.RefreshUnity();
        SystemCheck.Refresh();
        RefreshEnvironment();
        OnPropertyChanged(string.Empty);
    }

    [RelayCommand]
    private void ToggleLanguage() => SetLanguage(L.Ru ? "en" : "ru");

    [RelayCommand]
    private void ToggleLog() => ShowLog = !ShowLog;

    [RelayCommand]
    private void ToggleSettings() => ShowSettings = !ShowSettings;

    [RelayCommand]
    private void ToggleSystemCheck()
    {
        if (!ShowSystemCheck) RefreshEnvironment();
        ShowSystemCheck = !ShowSystemCheck;
    }

    private string? _lastLogLine;

    // progress for the first import arrives many times; keep the log readable
    private void AppendLogOnce(string line)
    {
        string key = line.Split(" · ")[0] + line.Split(" · ").ElementAtOrDefault(1);
        if (key == _lastLogLine) return;
        _lastLogLine = key;
        AppendLog(line);
    }

    public void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (_log.Length > 200_000) _log.Remove(0, _log.Length - 150_000);
        LogText = _log.ToString();
    }

    // ---------------------------------------------------------------- IConversionListener (called from worker threads)

    void IConversionListener.Log(string line) => Dispatcher.UIThread.Post(() => AppendLog(line));

    void IConversionListener.Progress(ProgressInfo p) => Dispatcher.UIThread.Post(() =>
    {
        if (!IsBusy) return;
        Progress = p.Overall;
        PercentText = $"{p.Overall:0}%";
        ProgressIndeterminate = false;
        string phase = L.Phase(p.Phase);
        if (p.Phase == "first-import") phase = $"{phase} · {p.Overall:0}%";

        if (p.Phase == "done") return;
        if (p.Model != null && p.ModelCount > 0)
        {
            Status = L.ModelOf(p.Model, p.ModelIndex, p.ModelCount);
            SubStatus = phase;
        }
        else
        {
            Status = phase;
            SubStatus = p.Phase == "first-import" ? L.FirstRunHint : "";
        }

        if (p.Job != null && Queue.FirstOrDefault(q => q.Job == p.Job) is { } item)
        {
            item.Percent = p.JobPercent;
            item.StageText = L.Phase(p.Phase);
        }

        if (p.Phase.StartsWith("stage:") || p.Phase is "first-import" or "unity-start" or "unpack" or "preview") AppendLogOnce($"── {Status} · {L.Phase(p.Phase)}");
    });

    void IConversionListener.JobState(InputJob job, JobState state, string? detail) =>
        Dispatcher.UIThread.Post(() => Queue.FirstOrDefault(q => q.Job == job)?.SetState(state, detail));

    void IConversionListener.Finish(InputJob job, JobState state, string? detail) => Dispatcher.UIThread.Post(() =>
    {
        QueueItem? q = Queue.FirstOrDefault(x => x.Job == job);
        if (q is { State: JobState.Working }) q.SetState(state, detail);
    });

    void IConversionListener.Built(BuiltAvatar avatar) => Dispatcher.UIThread.Post(async () =>
    {
        string name = Path.GetFileName(avatar.File);
        AvatarCard? card = Avatars.FirstOrDefault(a => a.FileName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (card == null)
        {
            card = new AvatarCard(name);
            Avatars.Add(card);
        }

        card.OutputFile = avatar.File;
        card.IsNew = true;
        card.AddedAt = DateTime.Now;
        card.InstalledIsCurrent = card.InstalledFile == null;
        card.Cover = null;
        _builtThisRun.Add(card);
        RefreshVisible();
        ScrollLibraryToTop?.Invoke();
        await card.LoadMetaAsync(avatar.Cover);
        UpdateLibraryCounters();
    });
}
