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
    [Reactive] private readonly SubCueList cues = [];

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
    }

    public bool AddToGroup(CueViewModel cue)
    {
        if (cue == this || cue.Parent == this)
            return false;

        var ind = mainViewModel.FindCueIndex(cue);
        if (ind == -1)
            return false;

        cue.Parent = this;
        mainViewModel.DeleteCue(ind, false);
        // cues.Add(cue);
        if (ind > 0)
        {
            var prev = mainViewModel.Cues[ind - 1];
            // if (prev == this || prev.HasParent(this))
        }

        return true;
    }

    public override void DelayedGo(CueViewModel? waitForCue = null)
    {
        base.DelayedGo(waitForCue);

        // Ony strictly needed for shuffling
        // groupCount = EnumerateContents(true).Count();

        switch (groupTrigger)
        {
            case GroupTriggerMode.Next:
                mainViewModel.Go();
                break;
            case GroupTriggerMode.All:
                foreach (var child in Cues)
                    if (child.Trigger == Models.TriggerMode.Go)
                        mainViewModel.Go(child);
                break;
            case GroupTriggerMode.Shuffle:
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
