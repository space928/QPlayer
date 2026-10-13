using QPlayer.Models;

namespace QPlayer.TcpCuePlugin
{
    public record TcpCueModel : Cue
    {
        public TcpCueModel() : base() { }

        public string host = "";
        public string port = "";

        public string path = "";

    }
}
