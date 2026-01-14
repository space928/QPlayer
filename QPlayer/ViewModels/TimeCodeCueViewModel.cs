using QPlayer.Models;
using QPlayer.Views;
using QPlayer.SourceGenerator;
using System;

namespace QPlayer.ViewModels;

[Model(typeof(TimeCodeCue))]
[View(typeof(CueEditor))]
public partial class TimeCodeCueViewModel : CueViewModel
{
    [Reactive] private TimeSpan startTime;
    [Reactive("TCDuration"), ChangesProp(nameof(Duration))] private TimeSpan duration;
    
    public override TimeSpan Duration => TCDuration;

    public TimeCodeCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
    }
}
