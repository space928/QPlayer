using QPlayer.Models;
using QPlayer.ViewModels;

namespace QPlayer.OSCCuePlugin;

public class OSCCuePlugin : QPlayerPlugin
{
    public override void OnLoad(MainViewModel mainViewModel)
    {
        MainViewModel.Log("Loaded OSC Cue plugin!");
    }
}
