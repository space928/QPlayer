using System;

namespace QPlayer.Models
{
    [Serializable]
    public record ShowFile
    {
        public const int FILE_FORMAT_VERSION = 1;

        public int fileFormatVersion = FILE_FORMAT_VERSION;
        public ShowMetadata showMetadata = new();
    }

    [Serializable]
    public record ShowMetadata
    {
        public string title = "Untitled";
        public string description = "";
        public string author = "";
        public DateTime date = DateTime.Today;
    }
}
