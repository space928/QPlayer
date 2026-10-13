using QPlayer.Models;
using QPlayer.ViewModels;

namespace QPlayer.LauncherPlugin
{
    [PluginName("Launcher Cue Plugin")]
    [PluginAuthor("Ummerland")]
    [PluginDescription("This plugin adds a Launcher, which allows you to launch programs (bat, exe,...) from the cue stack.")]
    public class LauncherPlugin : QPlayerPlugin
    {

        public override void OnLoad(MainViewModel mainViewModel)
        {
            MainViewModel.Log("Loaded Launcher Cue Plugin");
        }

    }
}
