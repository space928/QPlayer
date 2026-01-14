using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Views;
using QPlayer.SourceGenerator;
using System;
using System.Linq;
using System.Timers;

namespace QPlayer.ViewModels;

[Model(typeof(StopCue))]
[View(typeof(CueEditor))]
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

    internal override void UpdateUIStatus()
    {
        PlaybackTime = DateTime.Now.Subtract(startTime);
        if (PlaybackTime >= Duration)
            Stop();
    }

    public override void Go()
    {
        base.Go();
        // Stop cues don't support preloading
        PlaybackTime = TimeSpan.Zero;
        startTime = DateTime.Now;
        var cue = mainViewModel?.Cues.FirstOrDefault(x => x.QID == StopTarget);
        if(cue != null)
        {
            if (cue is SoundCueViewModel soundCue)
                soundCue.FadeOutAndStop(FadeOutTime, FadeType);
            else
            {
                cue.Stop();
                Stop();
            }
        } else
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
