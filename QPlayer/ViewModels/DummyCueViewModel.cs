using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.Views;

namespace QPlayer.ViewModels;

[Model(typeof(DummyCue))]
[View(typeof(CueEditor))]
public class DummyCueViewModel : CueViewModel
{
    public DummyCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {

    }
}
