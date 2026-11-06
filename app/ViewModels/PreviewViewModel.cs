using System.Collections.ObjectModel;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SaberBridge.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SaberBridge.ViewModels;

public sealed record AnimOption(string Key, string Label);

public sealed class CheckRow
{
    public CheckRow(CheckResult c)
    {
        Text = L.CheckText(c.Code, c.Value);
        (Icon, Brush) = c.Level switch
        {
            "ok" => ("✓", (IBrush)Avalonia.Media.Brush.Parse("#3DDC97")),
            "warn" => ("!", Avalonia.Media.Brush.Parse("#FFB547")),
            "fail" => ("✕", Avalonia.Media.Brush.Parse("#FF3B5C")),
            _ => ("i", Avalonia.Media.Brush.Parse("#2F8CFF")),
        };
    }

    public string Text { get; }
    public string Icon { get; }
    public IBrush Brush { get; }
}

/// <summary>Plays the frames Unity rendered for one avatar, with the skeleton drawn on top.</summary>
public partial class PreviewViewModel : ObservableObject, IDisposable
{
    private readonly MainViewModel _main;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(1000 / 12.0) };
    private readonly Dictionary<string, Bitmap[]> _frames = new();
    private PreviewData? _data;

    public PreviewViewModel(MainViewModel main, AvatarCard card)
    {
        _main = main;
        Card = card;
        _timer.Tick += (_, _) =>
        {
            if (Playing && FrameCount > 0) FrameIndex = (FrameIndex + 1) % FrameCount;
        };
        _ = InitAsync();
    }

    // the library may still be hashing this file when the window opens
    private async Task InitAsync()
    {
        if (Card.Hash == null) await Card.LoadMetaAsync();
        Load();
    }

    public AvatarCard Card { get; }
    public MainViewModel Main => _main;

    public ObservableCollection<CheckRow> Checks { get; } = new();

    public IReadOnlyList<AnimOption> AnimOptions { get; } = new[]
    {
        new AnimOption("turn", L.AnimTurn), new AnimOption("idle", L.AnimIdle), new AnimOption("wave", L.AnimWave), new AnimOption("walk", L.AnimWalk),
    };

    [ObservableProperty] private bool _hasData;
    [ObservableProperty] private bool _isRendering;
    [ObservableProperty] private string _title = "";
    [ObservableProperty] private string _subtitle = "";
    [ObservableProperty] private AnimOption? _anim;
    [ObservableProperty] private int _frameIndex;
    [ObservableProperty] private Bitmap? _frame;
    [ObservableProperty] private float[][]? _segments;
    [ObservableProperty] private bool _showSkeleton = true;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(ModelOpacity))] private bool _skeletonOnly;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(PlayLabel))] private bool _playing = true;
    [ObservableProperty] private double _frameWidth = 360;
    [ObservableProperty] private double _frameHeight = 480;

    public double ModelOpacity => SkeletonOnly ? 0.15 : 1;
    public string PlayLabel => Playing ? "❚❚" : "▶";
    public int FrameCount => _data?.Frames ?? 0;
    public bool IsTurntable => Anim?.Key == "turn";

    private void Load()
    {
        _data = Card.Hash != null ? PreviewData.Load(PreviewCache.DirFor(Card.Hash)) : null;
        Title = _data?.Name is { Length: > 0 } n ? n : Card.Name;
        Subtitle = string.Join("  ·  ", new[]
        {
            string.IsNullOrWhiteSpace(_data?.Author) ? null : L.By(_data!.Author!),
            Card.Size,
            Card.StateText,
        }.Where(s => !string.IsNullOrEmpty(s)));

        Checks.Clear();
        foreach (CheckResult c in _data?.Checks ?? new()) Checks.Add(new CheckRow(c));
        HasData = _data != null;
        if (_data == null) return;

        FrameWidth = _data.Width;
        FrameHeight = _data.HeightPx;
        _frames.Clear();
        Anim = AnimOptions.FirstOrDefault(a => a.Key == "idle" && _data.Anims.ContainsKey("idle")) ?? AnimOptions[0];
        _timer.Start();
    }

    partial void OnAnimChanged(AnimOption? value)
    {
        FrameIndex = 0;
        OnPropertyChanged(nameof(IsTurntable));
        ShowFrame();
    }

    partial void OnFrameIndexChanged(int value) => ShowFrame();

    private void ShowFrame()
    {
        if (_data == null || Anim == null || !_data.Anims.TryGetValue(Anim.Key, out float[][][]? skel)) return;
        if (!_frames.TryGetValue(Anim.Key, out Bitmap[]? frames))
        {
            frames = Enumerable.Range(0, _data.Frames).Select(i => File.Exists(_data.Frame(Anim.Key, i)) ? new Bitmap(_data.Frame(Anim.Key, i)) : null)
                .OfType<Bitmap>().ToArray();
            _frames[Anim.Key] = frames;
        }

        if (frames.Length == 0) return;
        int i = Math.Clamp(FrameIndex, 0, frames.Length - 1);
        Frame = frames[i];
        Segments = ShowSkeleton && i < skel.Length ? skel[i] : null;
    }

    partial void OnShowSkeletonChanged(bool value) => ShowFrame();

    /// <summary>Turntable scrubbing: a horizontal drag of one frame width turns the model once.</summary>
    public void Scrub(double dx)
    {
        if (!IsTurntable || FrameCount == 0) return;
        Playing = false;
        int step = (int)Math.Round(dx / (FrameWidth / FrameCount));
        if (step != 0) FrameIndex = ((FrameIndex - step) % FrameCount + FrameCount) % FrameCount;
    }

    [RelayCommand]
    private void TogglePlay() => Playing = !Playing;

    [RelayCommand]
    private async Task MakePreview()
    {
        if (_main.IsBusy) return;
        IsRendering = true;
        try
        {
            await _main.MakePreviewsAsync(new List<AvatarCard> { Card });
            Load();
        }
        finally
        {
            IsRendering = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        foreach (Bitmap b in _frames.Values.SelectMany(f => f)) b.Dispose();
        _frames.Clear();
    }
}
