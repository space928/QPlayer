using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels
{
    public class TimeCodeCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
    {
        [Reactive] public TimeSpan StartTime { get; set; }
        [Reactive, ReactiveDependency(nameof(TCDuration))] 
        public override TimeSpan Duration => TCDuration;
        [Reactive] public TimeSpan TCDuration { get; set; }

        public TimeCodeCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(string propertyName)
        {
            base.ToModel(propertyName);
            if (cueModel is TimeCodeCue tccue)
            {
                switch (propertyName)
                {
                    case nameof(StartTime): tccue.startTime = StartTime; break;
                    case nameof(TCDuration): tccue.duration = TCDuration; break;
                }
            }
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
            if (cue is TimeCodeCue tccue)
            {
                tccue.startTime = StartTime;
                tccue.duration = TCDuration;
            }
        }

        public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            TimeCodeCueViewModel vm = new(mainViewModel);
            if (cue is TimeCodeCue tccue)
            {
                vm.StartTime = tccue.startTime;
                vm.TCDuration = tccue.duration;
            }
            return vm;
        }
    }
}
