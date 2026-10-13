using QPlayer.Models;
using QPlayer.ViewModels;

namespace QPlayer.TcpCuePlugin
{
    [PluginName("TCP Cue Plugin")]
    [PluginAuthor("Ummerland")]
    [PluginDescription("This plugin adds the TCP cue, which allows you to send TCP messages from the cue stack.")]
    public class TcpCuePlugin : QPlayerPlugin
    {

        public override void OnLoad(MainViewModel mainViewModel)
        {
            MainViewModel.Log("Loaded TCP Cue Plugin");
        }
    }
}
