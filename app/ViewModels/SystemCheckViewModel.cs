using System.Collections.ObjectModel;
using Avalonia.Media;
using SaberBridge.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SaberBridge.ViewModels;

public enum CheckLevel { Ok, Info, Warn, Missing }

public sealed class CheckAction
{
    public CheckAction(string label, Func<CheckItem, Task> run, bool primary = false)
    {
        Label = label;
        Run = run;
        Primary = primary;
    }

    public string Label { get; }
    public Func<CheckItem, Task> Run { get; }
    public bool Primary { get; }
}

public partial class CheckItem : ObservableObject
{
    public CheckItem(string title, CheckLevel level, string detail, bool blocking, params CheckAction[] actions)
    {
        Title = title;
        Level = level;
        Detail = detail;
        Blocking = blocking && level == CheckLevel.Missing;
        Actions = actions;
    }

    public string Title { get; }
    public CheckLevel Level { get; }
    public string Detail { get; }
    public bool Blocking { get; }
    public IReadOnlyList<CheckAction> Actions { get; }
    public bool HasActions => Actions.Count > 0;

    public string Icon => Level switch { CheckLevel.Ok => "✓", CheckLevel.Info => "i", CheckLevel.Warn => "!", _ => "✕" };

    public IBrush LevelBrush => Level switch
    {
        CheckLevel.Ok => Brush.Parse("#3DDC97"),
        CheckLevel.Info => Brush.Parse("#2F8CFF"),
        CheckLevel.Warn => Brush.Parse("#FFB547"),
        _ => Brush.Parse("#FF3B5C"),
    };

    [ObservableProperty] private bool _isWorking;
    [ObservableProperty] private string? _progressText;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private bool _progressKnown;

    [RelayCommand]
    private async Task Act(CheckAction action)
    {
        if (IsWorking) return;
        IsWorking = true;
        ProgressText = null;
        try
        {
            await action.Run(this);
        }
        catch (Exception ex)
        {
            ProgressText = L.ActionFailed(ex.Message);
        }
        finally
        {
            IsWorking = false;
        }
    }

    public IProgress<(long done, long? total)> DownloadProgress() => new Progress<(long done, long? total)>(p =>
    {
        ProgressKnown = p.total is > 0;
        Progress = p.total is > 0 ? 100.0 * p.done / p.total.Value : 0;
        ProgressText = L.Downloading(p.done / 1048576.0, p.total / 1048576.0);
    });
}

/// <summary>Everything the converter needs, each with a way to fix it from inside the app.</summary>
public partial class SystemCheckViewModel : ObservableObject
{
    private readonly MainViewModel _main;

    public SystemCheckViewModel(MainViewModel main) => _main = main;

    public ObservableCollection<CheckItem> Items { get; } = new();
    public IReadOnlyList<Compatibility.Row> Table => Compatibility.Table;

    [ObservableProperty] private bool _allGood;
    [ObservableProperty] private bool _blocked;
    [ObservableProperty] private string _summary = "";

    // developer switch to see the "nothing installed" state: SABERBRIDGE_SIMULATE_MISSING=1
    private static readonly bool SimulateMissing = Environment.GetEnvironmentVariable("SABERBRIDGE_SIMULATE_MISSING") == "1";

    public void Refresh()
    {
        Items.Clear();
        GameInstall? game = SimulateMissing ? null : _main.SelectedGame;
        string? unity = SimulateMissing ? null : _main.UnityPath;
        // the Quest tab needs no Beat Saber, mod or BSManager on this PC: only Unity with the Android module
        bool quest = _main.IsQuestTab;
        if (!quest) AddGameItems(game);

        // ---- Unity Hub
        string? hub = SimulateMissing ? null : Tools.UnityHub();
        Items.Add(hub != null
            ? new CheckItem(L.ChkHub, CheckLevel.Ok, L.ChkHubOk, false)
            : new CheckItem(L.ChkHub, unity == null ? CheckLevel.Missing : CheckLevel.Info, L.ChkHubMissing, false, InstallHubAction()));

        // ---- Unity editor
        if (unity == null)
        {
            List<CheckAction> acts = new();
            if (OperatingSystem.IsWindows()) acts.Add(new CheckAction(L.ActInstallAll, InstallUnityDirect, true));
            if (hub != null) acts.Add(new CheckAction(L.ActInstallUnityHub, InstallUnityViaHub, !OperatingSystem.IsWindows()));
            else if (!OperatingSystem.IsWindows()) acts.Add(InstallHubAction());
            Items.Add(new CheckItem(L.ChkUnity, CheckLevel.Missing, L.ChkUnityMissing, true, acts.ToArray()));
        }
        else
        {
            string? ver = UnityLocator.VersionOf(unity);
            Items.Add(ver == Compatibility.UnityVersion
                ? new CheckItem(L.ChkUnity, CheckLevel.Ok, $"{L.ChkUnityOk} · {unity}", true)
                : new CheckItem(L.ChkUnity, CheckLevel.Warn, L.ChkUnityOther(ver ?? unity), false,
                    hub != null ? new CheckAction(L.ActInstallUnityHub, InstallUnityViaHub) : new CheckAction(L.ActInstallUnityDirect, InstallUnityDirect)));

            // a .whacker / .cyoob also carries a PC version, so on Linux the Quest tab needs the Windows module too
            if (!quest || !OperatingSystem.IsWindows())
                Items.Add(Tools.HasWindowsBuildSupport(unity)
                    ? new CheckItem(L.ChkWinSupport, CheckLevel.Ok, L.ChkWinSupportOk, true)
                    : new CheckItem(L.ChkWinSupport, CheckLevel.Missing, L.ChkWinSupportMissing, true,
                        new CheckAction(L.ActInstallUnityHub, InstallUnityViaHub, true)));

            if (quest)
            {
                List<CheckAction> acts = new();
                if (OperatingSystem.IsWindows()) acts.Add(new CheckAction(L.ActInstallAndroid, item => InstallAndroidDirect(item, unity), true));
                acts.Add(new CheckAction(L.ActInstallUnityHub, InstallUnityViaHub, !OperatingSystem.IsWindows()));
                Items.Add(UnityLocator.HasAndroid(unity)
                    ? new CheckItem(L.ChkAndroid, CheckLevel.Ok, L.ChkAndroidOk, true)
                    : new CheckItem(L.ChkAndroid, CheckLevel.Missing, L.ChkAndroidMissing, true, acts.ToArray()));
            }

            Items.Add(Tools.UnityLicenseFound()
                ? new CheckItem(L.ChkLicense, CheckLevel.Ok, L.ChkLicenseOk, false)
                : new CheckItem(L.ChkLicense, CheckLevel.Warn, L.ChkLicenseMissing, false,
                    hub != null ? new CheckAction(L.ActSignIn, WaitForLicense, true) : InstallHubAction()));
        }

        Blocked = Items.Any(i => i.Blocking);
        int problems = Items.Count(i => i.Level is CheckLevel.Missing or CheckLevel.Warn);
        AllGood = problems == 0;
        Summary = AllGood ? L.SystemOk : L.SystemProblems(problems);
    }

    private void AddGameItems(GameInstall? game)
    {
        // ---- game
        string? gameVer = game != null ? Compatibility.GameVersion(game.Path) : null;
        string? gameUnity = game != null ? Compatibility.GameUnityVersion(game.Path) : null;
        if (game == null)
        {
            Items.Add(new CheckItem(L.ChkGame, CheckLevel.Missing, L.ChkGameMissing, true,
                new CheckAction(L.ActPickGame, async _ => await _main.PickGameAsync(), true),
                InstallBsmAction()));
        }
        else if (gameUnity != null && gameUnity.StartsWith("2019"))
        {
            Items.Add(new CheckItem(L.ChkGame, CheckLevel.Missing, L.ChkGameTooOld(gameVer), true,
                new CheckAction(L.ActPickGame, async _ => await _main.PickGameAsync())));
        }
        else
        {
            Items.Add(new CheckItem(L.ChkGame, CheckLevel.Ok, L.ChkGameOk(game.Label, gameVer, gameUnity), true));
        }

        // ---- Custom Avatars in that game copy
        if (game != null)
        {
            Version? ca = Compatibility.CustomAvatarsVersion(game.Path);
            bool libs = new[] { "FinalIK.dll", "DynamicBone.dll" }.All(d => File.Exists(Path.Combine(game.Path, "Libs", d)) || File.Exists(Path.Combine(game.Path, "Plugins", d)));
            Version want = Compatibility.BundledCustomAvatars;
            CheckAction openBsm = Tools.BSManager() is { } bsm ? new CheckAction(L.ActOpenBsm, _ => { Tools.Open(bsm); return Task.CompletedTask; }, true) : InstallBsmAction(true);
            CheckAction page = new(L.ActModPage, _ => { Tools.Open(Downloads.CustomAvatarsPage); return Task.CompletedTask; });

            if (ca == null) Items.Add(new CheckItem(L.ChkCa, CheckLevel.Missing, L.ChkCaMissing, true, openBsm, page));
            else if (!libs) Items.Add(new CheckItem(L.ChkCa, CheckLevel.Missing, L.ChkCaLibs, true, openBsm));
            else if (ca.Major != want.Major || ca.Minor != want.Minor)
                Items.Add(new CheckItem(L.ChkCa, CheckLevel.Warn, L.ChkCaOther(Short(ca), Short(want)), false, page));
            else Items.Add(new CheckItem(L.ChkCa, CheckLevel.Ok, L.ChkCaOk(Short(ca)), true));
        }

        // ---- BSManager (optional)
        Items.Add(!SimulateMissing && Tools.BSManager() != null
            ? new CheckItem(L.ChkBsm, CheckLevel.Ok, L.ChkBsmOk, false)
            : new CheckItem(L.ChkBsm, CheckLevel.Info, L.ChkBsmMissing, false, InstallBsmAction(true)));

    }

    private static string Short(Version v) => v.Build >= 0 ? $"{v.Major}.{v.Minor}.{v.Build}" : $"{v.Major}.{v.Minor}";

    [RelayCommand]
    private void Recheck() => _main.RefreshEnvironment();

    // ---- actions

    private CheckAction InstallBsmAction(bool primary = false) => new(L.ActInstallBsm, async item =>
    {
        if (!OperatingSystem.IsWindows())
        {
            Tools.Open(Downloads.BSManagerReleases);
            return;
        }

        string url = await Downloads.BSManagerInstallerUrl(CancellationToken.None);
        string file = await Downloads.FetchAsync(url, item.DownloadProgress(), CancellationToken.None);
        item.ProgressText = L.Installing;
        await Downloads.RunInstallerAsync(file);
        _main.RefreshEnvironment();
    }, primary);

    private CheckAction InstallHubAction() => new(L.ActInstallHub, async item =>
    {
        if (!OperatingSystem.IsWindows())
        {
            Tools.Open(Downloads.UnityHubLinuxDocs);
            return;
        }

        string file = await Downloads.FetchAsync(Downloads.UnityHubWindows, item.DownloadProgress(), CancellationToken.None);
        item.ProgressText = L.Installing;
        await Downloads.RunInstallerAsync(file);
        _main.RefreshEnvironment();
    }, true);

    // Android Build Support has its own silent installer next to the editor's; it goes into the editor's folder
    private async Task InstallAndroidDirect(CheckItem item, string unityExe)
    {
        string file = await Downloads.FetchAsync(Downloads.UnityAndroidWindows, item.DownloadProgress(), CancellationToken.None, Downloads.UnityPublisher);
        item.ProgressText = L.Installing;
        item.ProgressKnown = false;
        string editorDir = Path.GetDirectoryName(Path.GetDirectoryName(unityExe)!)!;
        await Downloads.RunInstallersElevatedAsync(new[] { (file, $"/S /D={editorDir}") });
        _main.RefreshEnvironment();
    }

    private static Task InstallUnityViaHub(CheckItem item)
    {
        Tools.Open(Downloads.UnityHubInstallLink);
        item.ProgressText = L.WaitingForHub;
        return Task.CompletedTask;
    }

    // Everything Unity needs, downloaded (resumable, signature checked) and installed silently behind one admin
    // prompt: Unity Hub when missing (needed once to sign in for the free license), the editor into the folder Hub
    // scans, and on the Quest tab the Android module. Afterwards Hub is opened if no license is found yet.
    private async Task InstallUnityDirect(CheckItem item)
    {
        bool quest = _main.IsQuestTab;
        string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor", Compatibility.UnityVersion);
        List<(string file, string args)> run = new();
        if (Tools.UnityHub() == null)
            run.Add((await Downloads.FetchAsync(Downloads.UnityHubWindows, item.DownloadProgress(), CancellationToken.None, Downloads.UnityPublisher), "/S"));
        run.Add((await Downloads.FetchAsync(Downloads.UnityEditorWindows, item.DownloadProgress(), CancellationToken.None, Downloads.UnityPublisher), $"/S /D={target}"));
        if (quest)
            run.Add((await Downloads.FetchAsync(Downloads.UnityAndroidWindows, item.DownloadProgress(), CancellationToken.None, Downloads.UnityPublisher), $"/S /D={target}"));

        item.ProgressText = L.Installing;
        item.ProgressKnown = false;
        if (!await Downloads.RunInstallersElevatedAsync(run))
        {
            item.ProgressText = L.AdminDeclined;
            return;
        }

        if (!Tools.UnityLicenseFound() && Tools.UnityHub() != null) await WaitForLicense(item);
        _main.RefreshEnvironment();
    }

    // Unity's free Personal license is tied to a Unity account, so signing in is the one step left to the user: Unity
    // Hub opens, and the app notices the license file by itself and carries on.
    private async Task WaitForLicense(CheckItem item)
    {
        if (Tools.UnityHub() is { } hub) Tools.Open(hub);
        item.ProgressText = L.WaitingForLicense;
        item.ProgressKnown = false;
        DateTime until = DateTime.Now.AddMinutes(30);
        while (!Tools.UnityLicenseFound())
        {
            if (DateTime.Now > until)
            {
                item.ProgressText = L.LicenseTimeout;
                return;
            }

            await Task.Delay(3000);
        }

        _main.RefreshEnvironment();
    }
}
