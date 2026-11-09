using System.Globalization;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SaberBridge.Core;

namespace SaberBridge.ViewModels;

/// <summary>The "new version" strip under the header and the "Check for updates" button in the settings.</summary>
public partial class UpdateViewModel : ObservableObject
{
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

    private readonly Settings _settings;
    private readonly Func<bool> _busy;
    private UpdateInfo? _info;

    public UpdateViewModel(Settings settings, Func<bool> busy)
    {
        _settings = settings;
        _busy = busy;
    }

    public string CurrentVersion => L.AppVersion(Updates.Current.ToString(3));

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(Title), nameof(Text), nameof(ActionLabel))] private bool _isVisible;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(UpdateCommand), nameof(CheckNowCommand))] private bool _isWorking;
    [ObservableProperty] private double _percent;
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _checkResult = "";

    private Version? Available => _info?.Version ?? (Version.TryParse(_settings.UpdateLatest, out Version? v) ? v : null);
    private bool OneClick => _info != null && Updates.CanSelfUpdate(_info);

    public string Title => Available is { } v ? L.UpdateTitle(v.ToString(3)) : "";
    public string Text => L.UpdateText(Updates.Current.ToString(3), _info == null || OneClick);
    public string ActionLabel => _info != null && !OneClick ? L.UpdateDownload : L.UpdateNow;
    public bool HasError => Error.Length > 0;
    public bool HasCheckResult => CheckResult.Length > 0;

    partial void OnErrorChanged(string value) => OnPropertyChanged(nameof(HasError));
    partial void OnCheckResultChanged(string value) => OnPropertyChanged(nameof(HasCheckResult));

    /// <summary>Called on start: cleans up after an update, then asks GitHub at most once a day (a few seconds after the window opens).</summary>
    public void Start()
    {
        Updates.CleanUpOld();
        if (Environment.GetEnvironmentVariable("SABERBRIDGE_NO_UPDATE_CHECK") == "1") return;

        // a newer version seen earlier today is shown right away, without asking GitHub again
        if (Available is { } known && known > Updates.Current && _settings.UpdateSkipped != _settings.UpdateLatest) IsVisible = true;
        if (_settings.UpdateCheckedAt is { } at && DateTime.UtcNow - at < Interval) return;

        _ = Task.Run(async () =>
        {
            await Task.Delay(TimeSpan.FromSeconds(4));
            await CheckAsync(manual: false);
        });
    }

    private async Task CheckAsync(bool manual)
    {
        (bool reached, UpdateInfo? info) = await Updates.LatestAsync(CancellationToken.None);
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            if (reached)
            {
                _settings.UpdateCheckedAt = DateTime.UtcNow;
                _settings.UpdateLatest = info?.Version.ToString(3);
                _settings.Save();
            }

            _info = info;
            bool skipped = info != null && !manual && _settings.UpdateSkipped == info.Version.ToString(3);
            IsVisible = info != null && !skipped;
            OnPropertyChanged(nameof(Title));
            OnPropertyChanged(nameof(Text));
            OnPropertyChanged(nameof(ActionLabel));
            if (manual) CheckResult = !reached ? L.UpdateOffline : info == null ? L.UpdateNone : L.UpdateTitle(info.Version.ToString(3));
        });
    }

    [RelayCommand(CanExecute = nameof(CanCheck))]
    private async Task CheckNow()
    {
        IsWorking = true;
        CheckResult = L.UpdateChecking;
        try
        {
            await CheckAsync(manual: true);
        }
        finally
        {
            IsWorking = false;
        }
    }

    private bool CanCheck() => !IsWorking;

    [RelayCommand(CanExecute = nameof(CanUpdate))]
    private async Task Update()
    {
        Error = "";
        if (_busy())
        {
            Error = L.UpdateBusy;
            return;
        }

        IsWorking = true;
        try
        {
            // the strip may come from yesterday's check: ask GitHub for the file now
            if (_info == null) (_, _info) = await Updates.LatestAsync(CancellationToken.None);
            if (_info == null)
            {
                Error = L.UpdateOffline;
                return;
            }

            if (!Updates.CanSelfUpdate(_info))
            {
                Tools.Open(_info.PageUrl);
                return;
            }

            Progress<(long done, long? total)> progress = new(p =>
            {
                long total = p.total ?? _info.FileSize;
                Percent = total > 0 ? 100.0 * p.done / total : 0;
                ProgressText = L.UpdateDownloading(Mb(p.done), Mb(total));
            });
            // GitHub sometimes answers 502/504 or drops the connection: try again a few times, the download resumes where it stopped
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await Updates.InstallAsync(_info, progress, CancellationToken.None);
                    break;
                }
                catch (HttpRequestException) when (attempt < 4)
                {
                    ProgressText = L.UpdateRetrying(attempt + 1, 4);
                    await Task.Delay(TimeSpan.FromSeconds(5 * attempt));
                }
            }

            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime d) d.Shutdown();
        }
        catch (HttpRequestException)
        {
            Error = L.UpdateServerBusy;
        }
        catch (Exception e)
        {
            Error = L.UpdateFailed(e.Message);
        }
        finally
        {
            IsWorking = false;
            ProgressText = "";
        }
    }

    private bool CanUpdate() => !IsWorking;

    private static string Mb(long b) => (b / 1048576.0).ToString("0", CultureInfo.InvariantCulture) + (L.Ru ? " МБ" : " MB");

    [RelayCommand]
    private void WhatsNew() => Tools.Open(_info?.PageUrl ?? Updates.ReleasesPage);

    [RelayCommand]
    private void Skip()
    {
        _settings.UpdateSkipped = Available?.ToString(3);
        _settings.Save();
        IsVisible = false;
    }
}
