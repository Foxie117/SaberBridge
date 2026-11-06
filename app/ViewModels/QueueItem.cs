using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SaberBridge.Core;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SaberBridge.ViewModels;

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
