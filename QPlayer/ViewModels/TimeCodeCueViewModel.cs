using QPlayer.Models;
using QPlayer.Views;
using ReactiveUI.Fody.Helpers;
using System;

namespace QPlayer.ViewModels;

[Model(typeof(TimeCodeCue))]
[View(typeof(CueEditor))]
public class TimeCodeCueViewModel : CueViewModel
{
    [Reactive] public TimeSpan StartTime { get; set; }
    [Reactive] public TimeSpan TCDuration { get; set; }
    
    [Reactive, ReactiveDependency(nameof(TCDuration)), ModelSkip]
    public override TimeSpan Duration => TCDuration;

    public TimeCodeCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
    }
}
