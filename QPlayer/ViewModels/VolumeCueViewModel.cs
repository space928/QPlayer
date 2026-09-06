using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.Views;
using System;
using System.Linq;

namespace QPlayer.ViewModels;

[Model(typeof(VolumeCue))]
[View(typeof(CueEditor))]
[DisplayName("Volume Cue")]
[Icon("IconVolumeCue", typeof(Icons))]
public partial class VolumeCueViewModel : CueViewModel
{
    public override TimeSpan Duration => TimeSpan.FromSeconds(FadeTime);
    [Reactive, ModelBindsTo(nameof(VolumeCue.soundQid))] private string target = string.Empty;
    [Reactive] private float volume;
    [Reactive, ChangesProp(nameof(Duration))] private float fadeTime;
    [Reactive] private FadeType fadeType;
    public override string NamePreview => string.IsNullOrEmpty(Name) ? $"Change Volume of Q{target}" : Name;

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
                case nameof(Target):
                    OnPropertyChanged(nameof(NamePreview));
                    break;
            }
        };
    }

    protected internal override void UpdateUIStatus()
    {
        PlaybackTime = DateTime.UtcNow.Subtract(startTime);
        if (PlaybackTime >= Duration)
            Stop();
    }

    public override void Go()
    {
        base.Go();
        // Volume cues don't support preloading
        PlaybackTime = TimeSpan.Zero;
        startTime = DateTime.UtcNow;
        if (mainViewModel.FindCue(Target, out var cue))
        {
            if (cue is SoundCueViewModel soundCue)
                soundCue.Fade(MathF.Pow(10, Volume / 20f), FadeTime, FadeType);
            else
                Stop();
        }
        else
        {
            MainViewModel.Log($"Volume cue (Q{FullQID}) couldn't find a cue with QID: {target} to adjust!", MainViewModel.LogLevel.Warning);
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
