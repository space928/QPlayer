using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.ThemesV2;
using QPlayer.Views;

namespace QPlayer.ViewModels;

[Model(typeof(GroupCue))]
[View(typeof(CueEditor))]
[DisplayName("Group Cue")]
[Icon("IconGroupCue", typeof(Icons))]
public class GroupCueViewModel : CueViewModel
{
    public GroupCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
    }
}
