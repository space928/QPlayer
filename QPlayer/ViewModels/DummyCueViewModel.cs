using QPlayer.Models;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels
{
    public class DummyCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
    {
        public DummyCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
        }

        public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            DummyCueViewModel vm = new(mainViewModel);
            if (cue is DummyCue dcue)
            {
                //
            }
            return vm;
        }
    }
}
