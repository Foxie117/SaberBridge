using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaberBridge.Core;

namespace SaberBridge.ViewModels;

/// <summary>
/// The banner that gets Unity ready by itself. It starts with the app whenever something is missing and only asks
/// the user for what cannot be automated: the admin prompt and the Unity sign-in.
/// </summary>
public partial class SetupViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private readonly Settings _settings;
    private UnitySetup? _setup;
    private CancellationTokenSource? _cts;

    /// <summary>True while the setup owns Unity (installing it or importing the project); conversions wait for it.</summary>
    public static bool Busy { get; private set; }

    public SetupViewModel(MainViewModel main, Settings settings)
    {
        _main = main;
        _settings = settings;
    }

    [ObservableProperty] private bool _isVisible;

    /// <summary>The steps and disk table of the guide window.</summary>
    public SetupGuide Guide { get; } = new();

    /// <summary>Opens the guide window (set by the main window).</summary>
    public Action? OpenGuideWindow { get; set; }

    /// <summary>Closes the guide window once Unity is ready (set by the main window).</summary>
    public Action? CloseGuideWindow { get; set; }

    private bool _guideShown;

    [ObservableProperty] private string _editorDirText = "";

    [RelayCommand]
    private void OpenGuide() => OpenGuideWindow?.Invoke();
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Title), nameof(ShowAllow), nameof(ShowHub), nameof(ShowRetry), nameof(IsError), nameof(ShowLater))]
    private SetupStep _step;
    [ObservableProperty] private string _text = "";
    [ObservableProperty] private double _percent;
    [ObservableProperty] private bool _percentKnown;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasError))] private string? _error;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasDetail))] private string? _detail;
    public bool HasDetail => !string.IsNullOrEmpty(Detail);
    [ObservableProperty] private bool _canSkip;
    [ObservableProperty] private bool _canPickHub;

    /// <summary>Set by the main window: a file picker for Unity Hub.exe.</summary>
    public Func<Task<string?>>? PickHubFile { get; set; }

    [RelayCommand]
    private async Task PickHub()
    {
        if (PickHubFile == null || await PickHubFile() is not { } exe || !File.Exists(exe)) return;
        _settings.UnityHubPath = exe;
        _settings.Save();
        Tools.UnityHubOverride = exe;
        StartIfNeeded();
    }

    [RelayCommand]
    private void Skip() => _setup?.SkipNetFx();

    public string Title => Step switch
    {
        SetupStep.NeedsPermission => L.SetupTitlePermission,
        SetupStep.NeedsSignIn => L.SetupTitleSignIn,
        SetupStep.Failed => L.SetupTitleFailed,
        SetupStep.Ready => L.SetupReady,
        _ => L.SetupTitle,
    };

    public bool ShowAllow => Step == SetupStep.NeedsPermission && Error != null;
    public bool ShowHub => Step == SetupStep.NeedsSignIn;
    public bool ShowRetry => Step == SetupStep.Failed;
    public bool ShowLater => Step is not (SetupStep.Installing or SetupStep.Preparing or SetupStep.Ready);
    public bool IsError => Step == SetupStep.Failed;
    public bool HasError => !string.IsNullOrEmpty(Error);

    /// <summary>Starts the setup when anything is missing; does nothing when Unity is ready or the setup already runs.</summary>
    public void StartIfNeeded()
    {
        if (_cts != null) return;
        if (Environment.GetEnvironmentVariable("SABERBRIDGE_SIMULATE_MISSING") == "1") return;
        if (!OperatingSystem.IsWindows()) return;

        _setup = new UnitySetup(_settings, s => Dispatcher.UIThread.Post(() => Show(s)));
        bool nothingToDo = !_setup.Detect().Any && Tools.UnityLicenseFound() && ProjectSetup.IsQuestImported;
        if (nothingToDo) return;

        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        ShowPlan(_setup.PlanDisks(_setup.Detect()));
        IsVisible = true;
        // the first time the setup starts in this run, the guide opens by itself so nothing comes as a surprise
        if (!_guideShown)
        {
            _guideShown = true;
            Dispatcher.UIThread.Post(() => OpenGuideWindow?.Invoke(), DispatcherPriority.Background);
        }

        Busy = true;
        _main.Status = _main.Sabers.Status = L.SetupWaitStatus;
        Changed();
        _ = Task.Run(async () =>
        {
            bool ok = false;
            try
            {
                ok = await _setup.RunAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // "Later"
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => Show(new SetupStatus(SetupStep.Failed, L.SetupTitleFailed, null, ex.Message)));
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Busy = false;
                _cts?.Dispose();
                _cts = null;
                _main.RefreshEnvironment();
                _main.Sabers.RefreshUnity();
                _main.Status = ok ? L.Ready : L.SetupWaitStatus;
                Changed();
                if (ok) _ = HideSoon();
            });
        });
    }

    private void ShowPlan(UnitySetup.DiskPlan plan)
    {
        Guide.SetDisks(plan);
        EditorDirText = plan.Rows.Count == 0 ? L.GuideNothingToInstall : L.GuideEditorDir(plan.EditorDir);
    }

    private void Show(SetupStatus s)
    {
        if (_setup?.Plan is { } plan) ShowPlan(plan);
        Guide.Apply(s.Step);
        Step = s.Step;
        Text = s.Text;
        Error = s.Error;
        Detail = s.Detail;
        CanSkip = s.CanSkip;
        CanPickHub = s.CanPickHub;
        PercentKnown = s.Percent != null;
        Percent = s.Percent ?? 0;
        OnPropertyChanged(nameof(ShowAllow));
    }

    private async Task HideSoon()
    {
        await Task.Delay(4000);
        if (_cts == null && Step == SetupStep.Ready)
        {
            IsVisible = false;
            CloseGuideWindow?.Invoke();
        }
    }

    private void Changed()
    {
        _main.ConvertCommand.NotifyCanExecuteChanged();
        _main.Sabers.ConvertCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void Allow() => _setup?.RetryPermission();

    [RelayCommand]
    private void OpenHub() => UnitySetup.OpenHub();

    [RelayCommand]
    private void CreateAccount() => Tools.Open("https://id.unity.com/");

    [RelayCommand]
    private void Retry() => StartIfNeeded();

    [RelayCommand]
    private void OpenLog() => Tools.Open(Path.Combine(AppPaths.Logs, "setup.log"));

    [RelayCommand]
    private void Later()
    {
        _cts?.Cancel();
        IsVisible = false;
    }
}
