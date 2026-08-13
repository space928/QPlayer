using QPlayer.Audio;
using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;

namespace QPlayer.PyPlayPlugin;

[Model(typeof(VideoFramingCue))]
//[GenerateView]
[View(typeof(FramingCueView))]
[DisplayName("Video Framing Cue")]
[Icon("IconPyFramingCue", typeof(Icons))]
public partial class FramingCueViewModel(MainViewModel mainViewModel) : CueViewModel(mainViewModel)
{
    [Reactive] private UndoableObservableCollection<Vector2> corners = [..Enumerable.Repeat<Vector2>(default, 4)];
    [Reactive] private UndoableObservableCollection<FramingShutterViewModel, FramingShutter> framing = [.. Enumerable.Range(0, 4).Select(x => new FramingShutterViewModel())];
    [Reactive] private float fadeTime = 0;
    [Reactive] private FadeType fadeType = FadeType.SCurve;

    public override string NamePreview => string.IsNullOrEmpty(Name) ? "Framing Cue" : Name;
}

public partial class FramingShutterViewModel : BindableViewModel<FramingShutter>
{
    [Reactive] private float rotation;
    [Reactive] private float maskStart;
    [Reactive] private float softness;
}
