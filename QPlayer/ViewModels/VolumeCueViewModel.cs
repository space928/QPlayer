using QPlayer.SourceGenerator;
using System;
using System.Linq;
using System.Timers;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Views;

namespace QPlayer.ViewModels;

[Model(typeof(VolumeCue))]
[View(typeof(CueEditor))]
public partial class VolumeCueViewModel : CueViewModel
{
    public override TimeSpan Duration => TimeSpan.FromSeconds(FadeTime);
    [Reactive, ModelBindsTo(nameof(VolumeCue.soundQid))] private decimal target;
    [Reactive] private float volume;
    [Reactive, ChangesProp(nameof(Duration))] private float fadeTime;
    [Reactive] private FadeType fadeType;

    private DateTime startTime;

    public VolumeCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(FadeTime):
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
        // Volume cues don't support preloading
        PlaybackTime = TimeSpan.Zero;
        startTime = DateTime.Now;
        var cue = mainViewModel?.Cues.FirstOrDefault(x => x.QID == Target);
        if(cue != null)
        {
            if (cue is SoundCueViewModel soundCue)
                soundCue.Fade(Volume, FadeTime, FadeType);
            else
                Stop();
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
