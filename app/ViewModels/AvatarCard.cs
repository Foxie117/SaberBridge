using Avalonia.Media;
using Avalonia.Media.Imaging;
using BSAvatarConverter.Core;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BSAvatarConverter.ViewModels;

/// <summary>One avatar in the library: installed in the game, freshly converted, or both.</summary>
public partial class AvatarCard : ObservableObject
{
    public AvatarCard(string fileName)
    {
        FileName = fileName;
        Name = Path.GetFileNameWithoutExtension(fileName);
    }

    public string FileName { get; }
    public string Name { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(CanInstall), nameof(InstallLabel), nameof(StateText), nameof(PreviewFile))]
    private string? _installedFile;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(CanInstall), nameof(InstallLabel), nameof(StateText), nameof(PreviewFile))]
    private string? _outputFile;

    // false while the game still has an older file with the same name
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsInstalled), nameof(CanInstall), nameof(InstallLabel), nameof(StateText))]
    private bool _installedIsCurrent = true;

    [ObservableProperty] private bool _isNew;
    [ObservableProperty] private DateTime _addedAt;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasNoCover))] private Bitmap? _cover;
    [ObservableProperty] private string? _hash;
    [ObservableProperty] private bool _hasPreview;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CheckBrush), nameof(HasCheck))] private string _checkLevel = "none";
    [ObservableProperty] private string _size = "";

    public bool IsInstalled => InstalledFile != null && InstalledIsCurrent;
    public bool CanInstall => OutputFile != null && !IsInstalled;
    public string InstallLabel => InstalledFile != null && !InstalledIsCurrent ? L.ReplaceInGame : L.Install;
    public bool HasNoCover => Cover == null;
    public bool HasCheck => CheckLevel != "none";

    // the freshly converted file is what the user wants to look at; otherwise the one in the game
    public string? PreviewFile => OutputFile ?? InstalledFile;

    public string StateText => IsInstalled ? L.Installed : InstalledFile != null ? L.OldInGame : L.NotInstalled;

    public IBrush CheckBrush => CheckLevel switch
    {
        "ok" => Brush.Parse("#3DDC97"),
        "warn" => Brush.Parse("#FFB547"),
        "fail" => Brush.Parse("#FF3B5C"),
        _ => Brushes.Transparent,
    };

    /// <summary>Hash, cover and check result; runs off the UI thread for the hashing.</summary>
    public async Task LoadMetaAsync(string? coverFallback = null)
    {
        string? file = PreviewFile;
        if (file == null || !File.Exists(file)) return;
        Size = $"{new FileInfo(file).Length / 1048576.0:0.0} MB";
        string hash = await Task.Run(() => PreviewCache.Hash(file));
        Hash = hash;
        PreviewData? data = await Task.Run(() => PreviewData.Load(PreviewCache.DirFor(hash)));
        HasPreview = data != null;
        CheckLevel = data?.WorstLevel ?? "none";

        string? cover = PreviewCache.Cover(hash) ?? coverFallback;
        if (cover != null && Cover == null)
        {
            try
            {
                Cover = await Task.Run(() => new Bitmap(cover));
            }
            catch (Exception)
            {
                // a broken image just means no cover
            }
        }
    }
}
