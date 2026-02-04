using QPlayer.Models;
using QPlayer.ViewModels;

namespace QPlayer.OSCCuePlugin;

[PluginName("OSC Cue Plugin")]
[PluginAuthor("Thomas Mathieson")]
[PluginDescription("This plugin adds the OSC cue, which allows you to send OSC messages to external devices from the cue stack.")]
public class OSCCuePlugin : QPlayerPlugin
{
    public override void OnLoad(MainViewModel mainViewModel)
    {
        MainViewModel.Log("Loaded OSC Cue plugin!");
    }
}
