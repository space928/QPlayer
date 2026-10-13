using QPlayer.Models;

namespace QPlayer.LauncherPlugin
{
    public record LauncherModel : Cue
    {
        public LauncherModel() : base() { }
        
        public string fileName = "";
        public string arguments = "";

    }
}
