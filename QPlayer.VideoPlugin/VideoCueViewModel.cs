using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.VideoPlugin;

[Model(typeof(VideoCueModel))]
[View(typeof(VideoCueViewModelView))]
[GenerateView]
[DisplayName("Video Cue")]
// [Icon("IconOSCCue", typeof(Icons))]
public partial class VideoCueViewModel : CueViewModel
{
    [Reactive, FilePicker(nameof(PickVideoFileCommand))] private string videoFile = string.Empty;
    [Reactive] private TimeSpan startTime;
    [Reactive] private TimeSpan endTime;
    [Reactive, Range(0)] private float fadeIn;
    [Reactive, Range(0)] private float fadeOut;
    [Reactive] private FadeType fadeType = FadeType.SCurve;

    [Reactive] private BlendMode blendMode;
    [Reactive, Range(0, 1)] private float opacity;
    
    [Reactive] private ScalingMode scalingMode;
    [Reactive] private float layer;
    [Reactive] private ImageTransform? transform;
    
    [Reactive] private bool enableAudio;
    [Reactive, Range(-32, 32), Knob] private float volume;
    [Reactive, Range(-1, 1), Knob] private float pan;
    [Reactive] private EQSettings? eq;

    public VideoCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
    }

    public void PickVideoFileCommand()
    {

    }

    public override void Go()
    {
        base.Go();
    }

    public override void Pause()
    {
        base.Pause();
    }

    public override void Stop()
    {
        base.Stop();
    }

    public override void Preload(TimeSpan startTime)
    {
        base.Preload(startTime);
    }
}
