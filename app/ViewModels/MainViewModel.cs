using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using BSAvatarConverter.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace BSAvatarConverter.ViewModels;

public partial class QueueItem : ObservableObject
{
    public QueueItem(InputJob job)
    {
        Job = job;
        if (job.Kind is JobKind.Unsupported or JobKind.Skipped) SetState(JobState.Skipped, job.Note);
        else SetState(JobState.Waiting, null);
    }

    public InputJob Job { get; }
    public string Name => Job.Name;
    public string Origin => Job.Origin == Path.GetFileName(Job.File) ? "" : Job.Origin;

    public string Badge => Job.Kind switch { JobKind.Vrm => "VRM", JobKind.Pmx => "MMD", JobKind.VrcPackage => "VRC", _ => "?" };

    public IBrush BadgeBrush => Job.Kind switch
    {
        JobKind.Vrm => Brush.Parse("#2F8CFF"),
        JobKind.Pmx => Brush.Parse("#B068FF"),
        JobKind.VrcPackage => Brush.Parse("#FF3B5C"),
        _ => Brush.Parse("#4A4F66"),
    };

    [ObservableProperty] private JobState _state;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private IBrush _statusBrush = Brushes.Gray;
    [ObservableProperty] private string? _detail;
    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _stageText = "";

    public void SetState(JobState state, string? detail)
    {
        State = state;
        Detail = detail;
        IsWorking = state == JobState.Working;
        if (!IsWorking) StageText = "";
        Percent = state == JobState.Done ? 100 : state == JobState.Working ? Percent : 0;
        (StatusText, StatusBrush) = state switch
        {
            JobState.Working => (L.Working, Brush.Parse("#2F8CFF")),
            JobState.Done => (L.Done, Brush.Parse("#3DDC97")),
            JobState.Failed => (L.Failed, Brush.Parse("#FF3B5C")),
            JobState.Skipped => (Job.Kind == JobKind.Unsupported ? L.Unsupported : L.Skipped, Brush.Parse("#FFB547")),
            _ => (L.Waiting, Brush.Parse("#8A90A8")),
        };
    }
}

public partial class ResultItem : ObservableObject
{
    public ResultItem(BuiltAvatar avatar)
    {
        File = avatar.File;
        Name = Path.GetFileNameWithoutExtension(avatar.File);
        Size = $"{new FileInfo(avatar.File).Length / 1024.0 / 1024.0:0.0} MB";
        try
        {
            if (avatar.Cover != null) Cover = new Bitmap(avatar.Cover);
        }
        catch (Exception)
        {
            // no cover is fine
        }
    }

    public string File { get; }
    public string Name { get; }
    public string Size { get; }
    public Bitmap? Cover { get; }
    public bool HasNoCover => Cover == null;

    [ObservableProperty] private bool _installed;
}

public partial class MainViewModel : ObservableObject, IConversionListener
{
    private readonly Settings _settings = Settings.Load();
    private readonly StringBuilder _log = new();
    private CancellationTokenSource? _cts;
    private string? _lastOutDir;

    public MainViewModel()
    {
        _autoInstall = _settings.AutoInstall;
        RefreshEnvironment();
    }

    public ObservableCollection<QueueItem> Queue { get; } = new();
    public ObservableCollection<ResultItem> Results { get; } = new();
    public ObservableCollection<GameInstall> Games { get; } = new();

    [ObservableProperty] private GameInstall? _selectedGame;
    [ObservableProperty] private string? _unityPath;
    [ObservableProperty] private string _unityText = "";
    [ObservableProperty] private bool _unityOk;
    [ObservableProperty] private string _gameText = "";
    [ObservableProperty] private bool _gameOk;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(ConvertCommand))] private bool _isBusy;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressIndeterminate;
    [ObservableProperty] private string _status = L.Ready;
    [ObservableProperty] private string _subStatus = "";
    [ObservableProperty] private string _percentText = "";
    [ObservableProperty] private string _elapsed = "";
    [ObservableProperty] private string _logText = "";
    [ObservableProperty] private bool _showLog;
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _autoInstall;

    public bool QueueEmpty => Queue.Count == 0;
    public bool ResultsEmpty => Results.Count == 0;
    public string ResultsHeader => Results.Count == 0 ? L.Results : $"{L.Results} · {Results.Count}";
    public string DataFolder => AppPaths.Data;

    partial void OnAutoInstallChanged(bool value)
    {
        _settings.AutoInstall = value;
        _settings.Save();
    }

    partial void OnSelectedGameChanged(GameInstall? value)
    {
        GameOk = value is { HasCustomAvatars: true };
        GameText = value == null ? L.GameMissing : value.HasCustomAvatars ? value.Label : $"{value.Label} · {L.NoCustomAvatars}";
        if (value != null && value.Path != _settings.GamePath)
        {
            _settings.GamePath = value.Path;
            _settings.Save();
        }

        ConvertCommand.NotifyCanExecuteChanged();
    }

    public void RefreshEnvironment()
    {
        UnityPath = UnityLocator.Find(_settings.UnityPath);
        UnityOk = UnityPath != null;
        UnityText = UnityPath == null ? L.UnityMissing : $"Unity {UnityLocator.VersionOf(UnityPath) ?? ""}";

        Games.Clear();
        foreach (GameInstall g in GameLocator.FindAll(_settings.GamePath)) Games.Add(g);
        SelectedGame = Games.FirstOrDefault(g => g.Path == _settings.GamePath) ?? Games.FirstOrDefault();
        OnSelectedGameChanged(SelectedGame);
        ConvertCommand.NotifyCanExecuteChanged();
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

    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        string before = Status;
        Status = L.Scanning;
        try
        {
            List<InputJob> jobs = await Task.Run(() => InputScanner.Scan(paths.ToList()));
            foreach (InputJob job in jobs)
            {
                if (Queue.Any(q => q.Job.File == job.File)) continue;
                Queue.Add(new QueueItem(job));
            }
        }
        catch (Exception ex)
        {
            AppendLog("⚠ " + ex.Message);
        }

        Status = before;
        OnPropertyChanged(nameof(QueueEmpty));
        ConvertCommand.NotifyCanExecuteChanged();
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

    private bool CanConvert() => !IsBusy && UnityOk && GameOk && Queue.Any(q => q.State is JobState.Waiting or JobState.Failed);

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
        IsBusy = true;
        _cts = new CancellationTokenSource();
        Stopwatch clock = Stopwatch.StartNew();
        DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => Elapsed = clock.Elapsed.ToString(clock.Elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss");
        timer.Start();
        int builtBefore = Results.Count;
        string finalStatus = L.Ready;
        Converter converter = new(UnityPath, SelectedGame, this);
        _lastOutDir = converter.OutDir;

        try
        {
            await converter.RunAsync(todo.Select(q => q.Job).ToList(), _cts.Token);

            // let the queued result/state updates from the worker land first
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            int built = Results.Count - builtBefore;
            finalStatus = L.Finished(built, Queue.Count(q => q.State == JobState.Failed));
            if (AutoInstall && built > 0) finalStatus += "  ·  " + InstallAll(Results.Take(built).ToList());
            if (Queue.Any(q => q.State == JobState.Failed)) ShowLog = true;
        }
        catch (OperationCanceledException)
        {
            finalStatus = L.Cancelled;
            foreach (QueueItem q in todo.Where(q => q.State == JobState.Working)) q.SetState(JobState.Waiting, null);
        }
        catch (Exception ex)
        {
            finalStatus = ex.Message;
            AppendLog("⚠ " + ex);
            ShowLog = true;
            foreach (QueueItem q in todo.Where(q => q.State == JobState.Working)) q.SetState(JobState.Failed, ex.Message);
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
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void InstallAllResults() => Status = InstallAll(Results.Where(r => !r.Installed).ToList());

    [RelayCommand]
    private void InstallOne(ResultItem item) => Status = InstallAll(new List<ResultItem> { item });

    private string InstallAll(List<ResultItem> items)
    {
        if (SelectedGame == null || items.Count == 0) return Status;
        try
        {
            Directory.CreateDirectory(SelectedGame.AvatarsDir);
            foreach (ResultItem r in items)
            {
                File.Copy(r.File, Path.Combine(SelectedGame.AvatarsDir, Path.GetFileName(r.File)), true);
                r.Installed = true;
            }

            return L.InstalledTo(items.Count, SelectedGame.AvatarsDir);
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    [RelayCommand]
    private void OpenOutput() => OpenPath(_lastOutDir ?? AppPaths.Output);

    [RelayCommand]
    private void OpenGameAvatars()
    {
        if (SelectedGame != null) OpenPath(SelectedGame.AvatarsDir);
    }

    [RelayCommand]
    private void OpenDataFolder() => OpenPath(AppPaths.Data);

    [RelayCommand]
    private void GetUnity() => OpenPath("https://unity.com/releases/editor/archive");

    [RelayCommand]
    private void SetLanguage(string code)
    {
        if (IsBusy || (code == "ru") == L.Ru) return;
        _settings.Language = code;
        _settings.Save();

        // the UI strings are fixed at startup, so restart in the new language
        if (Environment.ProcessPath is { } exe) Process.Start(exe);
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
    }

    [RelayCommand]
    private void ToggleLog() => ShowLog = !ShowLog;

    [RelayCommand]
    private void ToggleSettings() => ShowSettings = !ShowSettings;

    private static void OpenPath(string path)
    {
        try
        {
            if (!path.StartsWith("http")) Directory.CreateDirectory(path);
            if (OperatingSystem.IsWindows()) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else Process.Start("xdg-open", path);
        }
        catch (Exception)
        {
            // nothing useful to do if the desktop cannot open it
        }
    }

    private string? _lastLogLine;

    // progress for the first import arrives many times; keep the log readable
    private void AppendLogOnce(string line)
    {
        if (IsRepeat(line)) return;
        AppendLog(line);
    }

    private bool IsRepeat(string line)
    {
        string key = line.Split(" · ")[0] + line.Split(" · ").ElementAtOrDefault(1);
        bool same = key == _lastLogLine;
        _lastLogLine = key;
        return same;
    }

    private void AppendLog(string line)
    {
        _log.AppendLine(line);
        if (_log.Length > 200_000) _log.Remove(0, _log.Length - 150_000);
        LogText = _log.ToString();
    }

    // ---- IConversionListener (called from worker threads)

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

        if (p.Phase.StartsWith("stage:") || p.Phase is "first-import" or "unity-start" or "unpack") AppendLogOnce($"── {Status} · {L.Phase(p.Phase)}");
    });

    void IConversionListener.JobState(InputJob job, JobState state, string? detail) =>
        Dispatcher.UIThread.Post(() => Queue.FirstOrDefault(q => q.Job == job)?.SetState(state, detail));

    void IConversionListener.Finish(InputJob job, JobState state, string? detail) => Dispatcher.UIThread.Post(() =>
    {
        QueueItem? q = Queue.FirstOrDefault(x => x.Job == job);
        if (q is { State: JobState.Working }) q.SetState(state, detail);
    });

    void IConversionListener.Built(BuiltAvatar avatar) => Dispatcher.UIThread.Post(() =>
    {
        Results.Insert(0, new ResultItem(avatar));
        OnPropertyChanged(nameof(ResultsEmpty));
        OnPropertyChanged(nameof(ResultsHeader));
    });
}
