using QPlayer.Models;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels
{
    public class GroupCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
    {
        public GroupCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
        }

        public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            GroupCueViewModel vm = new(mainViewModel);
            if (cue is GroupCue gcue)
            {
                //
            }
            return vm;
        }
    }
}
