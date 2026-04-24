using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.Views;
using System;
using System.Linq;
using System.Timers;

namespace QPlayer.ViewModels;

[Model(typeof(StopCue))]
[View(typeof(CueEditor))]
[DisplayName("Stop Cue")]
[Icon("IconStopCue", typeof(Icons))]
public partial class StopCueViewModel : CueViewModel
{
    public override TimeSpan Duration => TimeSpan.FromSeconds(FadeOutTime);
    [Reactive, ModelBindsTo(nameof(StopCue.stopQid))] private decimal stopTarget;
    [Reactive] private StopMode stopMode;
    [Reactive, ChangesProp(nameof(Duration))] private float fadeOutTime;
    [Reactive] private FadeType fadeType;

    private DateTime startTime;

    public StopCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(FadeOutTime):
                    OnPropertyChanged(nameof(Duration));
                    break;
            }
        };
    }

    protected internal override void UpdateUIStatus()
    {
        PlaybackTime = startTime.Ticks == 0 ? TimeSpan.Zero : DateTime.UtcNow.Subtract(startTime);
        if (PlaybackTime >= Duration)
            Stop();
    }

    public override void Go()
    {
        base.Go();
        // Stop cues don't support preloading
        PlaybackTime = TimeSpan.Zero;
        startTime = DateTime.UtcNow;
        if (mainViewModel != null && mainViewModel.FindCue(StopTarget, out var cue))
        {
            if (stopMode == StopMode.LoopEnd)
            {
                State = CueState.Delay;
                startTime = new(0);
                cue.DeVamp(() =>
                {
                    State = CueState.Playing;
                    startTime = DateTime.UtcNow;
                }, fadeOutTime, fadeType);
            }
            else
            {
                if (FadeOutTime == 0)
                {
                    cue.Stop();
                    Stop();
                }
                else
                {
                    cue.FadeOutAndStop(FadeOutTime, FadeType);
                }
            }
        }
        else
        {
            Stop();
        }
    }

    public override void Stop()
    {
        base.Stop();
        PlaybackTime = TimeSpan.Zero;
    }

    public override void Pause()
    {
        // Pausing isn't supported on stop cues
        //base.Pause();
        Stop();
    }
}
