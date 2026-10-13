using QPlayer.Models;

namespace QPlayer.HttpCuePlugin
{
    public record HttpCueModel : Cue
    {
        public HttpCueModel() : base() { }

        public string url = "";

        public CueHttpMethod method = CueHttpMethod.Get;


        public enum CueHttpMethod
        {
            Get,
            Post
        }

    }
}
