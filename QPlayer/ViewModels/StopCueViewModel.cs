using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels
{
    public class StopCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
    {
        [Reactive] public decimal StopTarget { get; set; }
        [Reactive] public StopMode StopMode { get; set; }

        public StopCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(string propertyName)
        {
            base.ToModel(propertyName);
            if (cueModel is StopCue scue)
            {
                switch (propertyName)
                {
                    case nameof(StopTarget): scue.stopQid = StopTarget; break;
                    case nameof(StopMode): scue.stopMode = StopMode; break;
                }
            }
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
            if (cue is StopCue scue)
            {
                scue.stopQid = StopTarget;
                scue.stopMode = StopMode;
            }
        }

        public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            StopCueViewModel vm = new(mainViewModel);
            if (cue is StopCue scue)
            {
                vm.StopTarget = scue.stopQid;
                vm.StopMode = scue.stopMode;
            }
            return vm;
        }
    }
}
