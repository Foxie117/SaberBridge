using System.Collections.ObjectModel;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SaberBridge.Core;

namespace SaberBridge.ViewModels;

public enum GuideState { Pending, Active, Done, Problem }

/// <summary>One step of the Unity setup as the guide window lists it.</summary>
public partial class GuideStep : ObservableObject
{
    public GuideStep(int number, string title, string body)
    {
        Number = number;
        Title = title;
        Body = body;
    }

    public int Number { get; }
    public string Title { get; }
    public string Body { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Mark), nameof(MarkBrush), nameof(IsActive), nameof(TitleOpacity))]
    private GuideState _state;

    public string Mark => State switch
    {
        GuideState.Done => "✓",
        GuideState.Problem => "!",
        _ => Number.ToString(),
    };

    public IBrush MarkBrush => State switch
    {
        GuideState.Done => Brush.Parse("#3DDC97"),
        GuideState.Active => Brush.Parse("#2F8CFF"),
        GuideState.Problem => Brush.Parse("#FF3B5C"),
        _ => Brush.Parse("#3A3F58"),
    };

    public bool IsActive => State is GuideState.Active or GuideState.Problem;
    public double TitleOpacity => State == GuideState.Pending ? 0.6 : 1;
}

/// <summary>A row of the disk table: drive, what goes there, needed and free space.</summary>
public sealed class DiskRowView
{
    public DiskRowView(UnitySetup.DiskRow r)
    {
        Drive = r.Drive;
        What = r.What;
        Need = $"{r.Need / 1073741824.0:0.0} " + L.GbShort;
        Free = $"{r.Free / 1073741824.0:0.0} " + L.GbShort;
        Ok = r.Ok;
    }

    public string Drive { get; }
    public string What { get; }
    public string Need { get; }
    public string Free { get; }
    public bool Ok { get; }
    public string Verdict => Ok ? "✓" : L.DiskShort;
    public IBrush VerdictBrush => Brush.Parse(Ok ? "#3DDC97" : "#FF3B5C");
}

/// <summary>The steps of the guide and how a setup status maps onto them.</summary>
public sealed class SetupGuide
{
    public ObservableCollection<GuideStep> Steps { get; } = new()
    {
        new(1, L.GuideSpaceTitle, L.GuideSpaceBody),
        new(2, L.GuideDownloadTitle, L.GuideDownloadBody),
        new(3, L.GuidePermissionTitle, L.GuidePermissionBody),
        new(4, L.GuideInstallTitle, L.GuideInstallBody),
        new(5, L.GuideSignInTitle, L.GuideSignInBody),
        new(6, L.GuidePrepareTitle, L.GuidePrepareBody),
    };

    public ObservableCollection<DiskRowView> Disks { get; } = new();

    private int _current;

    public void Apply(SetupStep step)
    {
        int index = step switch
        {
            SetupStep.Checking => 0,
            SetupStep.Downloading => 1,
            SetupStep.NeedsPermission => 2,
            SetupStep.Installing or SetupStep.Verifying => 3,
            SetupStep.NeedsSignIn => 4,
            SetupStep.Preparing => 5,
            SetupStep.Ready => Steps.Count,
            _ => _current,
        };
        if (step != SetupStep.Failed) _current = index;
        for (int i = 0; i < Steps.Count; i++)
        {
            Steps[i].State = i < _current ? GuideState.Done
                : i == _current ? (step == SetupStep.Failed ? GuideState.Problem : GuideState.Active)
                : GuideState.Pending;
        }
    }

    public void SetDisks(UnitySetup.DiskPlan? plan)
    {
        Disks.Clear();
        if (plan == null) return;
        foreach (UnitySetup.DiskRow r in plan.Rows) Disks.Add(new DiskRowView(r));
    }
}

public enum CheckState { Ok, Missing, Auto, Optional }

/// <summary>One line of the first-run checklist: what it is, why the app needs it, where it is, and whether it is there.</summary>
public sealed class ChecklistItem
{
    public ChecklistItem(string title, string why, string? where, CheckState state, string? actionText = null, System.Windows.Input.ICommand? action = null)
    {
        Title = title;
        Why = why;
        Where = L.CheckWhere(where ?? L.NotFound);
        State = state;
        ActionText = actionText ?? "";
        Action = action;
    }

    public string Title { get; }
    public string Why { get; }
    public string Where { get; }
    public CheckState State { get; }
    public string ActionText { get; }
    public System.Windows.Input.ICommand? Action { get; }
    public bool HasAction => Action != null;

    public string Mark => State switch
    {
        CheckState.Ok => "✓",
        CheckState.Missing => "!",
        CheckState.Auto => "↓",
        _ => "–",
    };

    public string StateText => State switch
    {
        CheckState.Ok => L.CheckOk,
        CheckState.Missing => L.CheckMissing,
        CheckState.Auto => L.CheckAuto,
        _ => L.CheckOptional,
    };

    public IBrush MarkBrush => State switch
    {
        CheckState.Ok => Brush.Parse("#3DDC97"),
        CheckState.Missing => Brush.Parse("#FF3B5C"),
        CheckState.Auto => Brush.Parse("#2F8CFF"),
        _ => Brush.Parse("#3A3F58"),
    };
}
