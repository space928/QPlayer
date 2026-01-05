using QPlayer.Models;
using QPlayer.Views;

namespace QPlayer.ViewModels;

[Model(typeof(GroupCue))]
[View(typeof(CueEditor))]
public class GroupCueViewModel : CueViewModel
{
    public GroupCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
    }
}
