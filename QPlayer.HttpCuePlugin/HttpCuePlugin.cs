using QPlayer.Models;
using QPlayer.ViewModels;

namespace QPlayer.HttpCuePlugin
{
    [PluginName("HTTP Cue Plugin")]
    [PluginAuthor("Ummerland")]
    [PluginDescription("This plugin adds the HTTP cue, which allows you to send HTTP requests from the cue stack.")]
    public class HttpCuePlugin : QPlayerPlugin
    {

        public override void OnLoad(MainViewModel mainViewModel)
        {
            MainViewModel.Log("Loaded HTTP Cue Plugin");
        }


    }
}
