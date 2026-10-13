using QPlayer.Models;

namespace QPlayer.OSCCuePlugin;

public record OSCCueModel : Cue
{
    public OSCCueModel() : base() { }

    public string command = string.Empty;

    public string ip = string.Empty;

    public string port = string.Empty;
}
