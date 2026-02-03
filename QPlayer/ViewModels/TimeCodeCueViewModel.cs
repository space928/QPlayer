using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.Views;
using System;

namespace QPlayer.ViewModels;

[Model(typeof(TimeCodeCue))]
[View(typeof(CueEditor))]
[DisplayName("Timecode Cue")]
[Icon("IconTimeCodeCue", typeof(Icons))]
public partial class TimeCodeCueViewModel : CueViewModel
{
    [Reactive] private TimeSpan startTime;
    [Reactive("TCDuration"), ChangesProp(nameof(Duration))] private TimeSpan duration;
    
    public override TimeSpan Duration => TCDuration;

    public TimeCodeCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
    }
}
