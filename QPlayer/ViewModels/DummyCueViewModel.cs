using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.Views;

namespace QPlayer.ViewModels;

[Model(typeof(DummyCue))]
[View(typeof(CueEditor))]
[DisplayName("Dummy Cue")]
[Icon("IconDummyCue", typeof(Icons))]
public class DummyCueViewModel : CueViewModel
{
    public DummyCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {

    }
}
