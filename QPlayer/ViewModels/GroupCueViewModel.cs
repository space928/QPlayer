using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.Utilities;
using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using DisplayNameAttribute = QPlayer.SourceGenerator.DisplayNameAttribute;

namespace QPlayer.ViewModels;

[Model(typeof(GroupCue))]
[View(typeof(CueEditor))]
[DisplayName("Group Cue")]
[Icon("IconGroupCue", typeof(Icons))]
public partial class GroupCueViewModel : CueViewModel
{
    private readonly HashSet<CueViewModel> activeChildren = [];
    private readonly Dictionary<CueViewModel, TimeSpan> childTimeOffsets = [];
    private readonly Throttle computeDurationThrottle;

    private TimeSpan playbackTime;
    private TimeSpan computedDuration;

    [Reactive, Readonly, NoUndo] private SubCueList cues;

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
    public override string NamePreview => string.IsNullOrEmpty(Name) ? $"Group ({Cues.TotalCount} cues)" : Name;
    public override TimeSpan Duration => computedDuration;

    public event GroupCollapseEventArgs? OnCollapse;
    public delegate void GroupCollapseEventArgs(GroupCueViewModel sender, bool isCollapsed);

    public GroupCueViewModel(MainViewModel mainViewModel) : this(mainViewModel, null) { }

    /// <summary>
    /// Used by the internal unit tests to decouple from the main view model.
    /// </summary>
    /// <param name="mainViewModel"></param>
    /// <param name="ownerCueList"></param>
    internal GroupCueViewModel(MainViewModel mainViewModel, CueList? ownerCueList) : base(mainViewModel)
    {
        cues = new(this, ownerCueList);

        PropertyChanged += GroupCueViewModel_PropertyChanged;
        cues.CueListChanged += Cues_CueListChanged;
        computeDurationThrottle = new Throttle(TimeSpan.FromMilliseconds(40), ComputeDuration);
    }

    private void GroupCueViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(GroupTrigger):
                computeDurationThrottle.Invoke();
                break;
        }
    }

    private void Cues_CueListChanged(bool wasInserted, IEnumerable<CueViewModel> changedCues, IEnumerable<AbstractCueList.CuePosition>? positions)
    {
        OnPropertyChanged(nameof(NamePreview));
        // TODO: For the duration to be correct we need to subscribe/unsubscribe from duration changes of the children here...
        ComputeDuration();

        if (wasInserted)
        {
            foreach (var cue in changedCues)
            {
                cue.PropertyChanged += ChildCueChanged;
                cue.OnCompleted += ChildCueCompleted;
            }
        }
        else
        {
            foreach (var cue in changedCues)
            {
                cue.PropertyChanged -= ChildCueChanged;
                cue.OnCompleted -= ChildCueCompleted;
            }
        }
    }

    private void ChildCueChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not CueViewModel child)
            return;
        switch (e.PropertyName)
        {
            case nameof(Trigger):
            case nameof(Enabled):
            case nameof(Duration):
                computeDurationThrottle.Invoke();
                break;
            case nameof(PlaybackTime):
                var childTime = child.PlaybackTime.Ticks;
                if (childTime != 0 && childTimeOffsets.TryGetValue(child, out var start))
                    playbackTime =  TimeSpan.FromTicks(start.Ticks + childTime);
                break;
            case nameof(State):
                if (child.State == CueState.Delay || child.State == CueState.Playing || child.State == CueState.PlayingLooped)
                    activeChildren.Add(child);
                else if (child.State == CueState.Delay)
                    activeChildren.Remove(child);
                break;
        }
    }

    private void ChildCueCompleted(CueViewModel sender)
    {
        activeChildren.Remove(sender);
        if (activeChildren.Count == 0)
            InternalStop();
    }

    protected internal override void UpdateUIStatus()
    {
        base.UpdateUIStatus();
        PlaybackTime = playbackTime;
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

    private void InternalStop()
    {
        base.Stop();
        PlaybackTime = playbackTime = TimeSpan.Zero;
    }

    public override void Stop()
    {
        InternalStop();
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

    protected internal override void UpdateFullQID()
    {
        base.UpdateFullQID();
        foreach (var child in Cues)
            child.UpdateFullQID();
    }

    private void ComputeDuration()
    {
        long maxDur = default;
        long lastDur = default;
        childTimeOffsets.Clear();
        if (groupTrigger == GroupTriggerMode.All)
        {
            foreach (var cue in Cues)
            {
                if (!cue.Enabled)
                    continue;
                var dur = cue.Duration.Ticks + cue.Delay.Ticks;
                maxDur = Math.Max(maxDur, dur);
                childTimeOffsets.Add(cue, cue.Delay);
            }
        }
        else
        {
            long lastStart = default;
            foreach (var cue in Cues)
            {
                if (!cue.Enabled)
                    continue;
                var delay = cue.Delay.Ticks;
                var dur = cue.Duration.Ticks + delay;
                switch (cue.Trigger)
                {
                    case TriggerMode.WithLast:
                        maxDur = Math.Max(maxDur, dur);
                        lastDur = dur;
                        childTimeOffsets.Add(cue, TimeSpan.FromTicks(lastStart + delay));
                        break;
                    case TriggerMode.Go:
                    /*    maxDur = Math.Max(maxDur, dur);
                        lastStart += lastDur; // not strictly true, as a 'go' cue can be triggered before the last cue has finished
                        lastDur = dur;
                        childTimeOffsets.Add(cue, TimeSpan.FromTicks(lastStart + delay));
                        break;*/
                    case TriggerMode.AfterLast:
                        lastStart += lastDur;
                        lastDur += dur;
                        maxDur = Math.Max(maxDur, lastDur);
                        childTimeOffsets.Add(cue, TimeSpan.FromTicks(lastStart + delay));
                        break;
                }
            }
        }

        computedDuration = TimeSpan.FromTicks(maxDur);
        OnPropertyChanged(nameof(Duration));
    }
}
