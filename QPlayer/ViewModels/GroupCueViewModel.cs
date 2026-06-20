using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.Views;
using System;

namespace QPlayer.ViewModels;

[Model(typeof(GroupCue))]
[View(typeof(CueEditor))]
[DisplayName("Group Cue")]
[Icon("IconGroupCue", typeof(Icons))]
public partial class GroupCueViewModel : CueViewModel
{
    [Reactive] private readonly SubCueList cues;

    [Reactive] private GroupTriggerMode groupTrigger;
    private bool isCollapsed;
    [Reactive("IsCollapsed"), ModelBindsTo("isCollapsed")]
    private bool IsCollapsed_Template
    {
        get => isCollapsed;
        set
        {
            isCollapsed = value;
            OnCollapse?.Invoke(this, value);
        }
    }

    public event GroupCollapseEventArgs? OnCollapse;
    public delegate void GroupCollapseEventArgs(GroupCueViewModel sender, bool isCollapsed);

    public GroupCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        cues = new(this);
    }

    public override void DelayedGo(CueViewModel? waitForCue = null)
    {
        base.DelayedGo(waitForCue);
    }

    public override void Go()
    {
        base.Go();

        if (IsCollapsed && groupTrigger != GroupTriggerMode.All)
            IsCollapsed = false;

        // Ony strictly needed for shuffling
        // groupCount = EnumerateContents(true).Count();

        // This action should happen after any group delay (or wait cue)
        switch (groupTrigger)
        {
            case GroupTriggerMode.Next:
                if (!Cues.IsEmpty)
                    mainViewModel.Go(Cues[0]);
                break;
            case GroupTriggerMode.All:
                foreach (var child in Cues)
                    if (child.Trigger == Models.TriggerMode.Go)
                        mainViewModel.Go(child);
                break;
            case GroupTriggerMode.Shuffle:
                if (!Cues.IsEmpty)
                {
                    Cues.Shuffle();
                    mainViewModel.Go(Cues[0]);
                }
                break;
        }
    }

    public override void Stop()
    {
        base.Stop();
        foreach (var child in Cues)
            child.Stop();
    }

    public override void DeVamp(Action? onDevampStart, float fadeDuration = -1, FadeType? fadeType = null)
    {
        base.DeVamp(onDevampStart, fadeDuration, fadeType);
        foreach (var child in Cues)
            child.DeVamp(null, fadeDuration, fadeType);
    }

    public override void FadeOutAndStop(float duration, FadeType? fadeType = null)
    {
        base.FadeOutAndStop(duration, fadeType);
        foreach (var child in Cues)
            child.FadeOutAndStop(duration, fadeType);
    }

    public override void Pause()
    {
        base.Pause();
        foreach (var child in Cues)
            child.Pause();
    }
}
