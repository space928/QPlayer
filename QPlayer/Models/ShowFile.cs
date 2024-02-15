using System;
using System.Collections.Generic;
using System.Drawing;
using LibVLCSharp;

namespace QPlayer.Models
{
    [Serializable]
    public record ShowFile
    {
        public const int FILE_FORMAT_VERSION = 1;

        public int fileFormatVersion = FILE_FORMAT_VERSION;
        public ShowMetadata showMetadata = new();
        public List<Cue> cues = new() { new SoundCue() };
    }

    [Serializable]
    public record ShowMetadata
    {
        public string title = "Untitled";
        public string description = "";
        public string author = "";
        public DateTime date = DateTime.Today;
    }

    public enum CueType
    {
        GroupCue,
        DummyCue,
        SoundCue,
        TimeCodeCue
    }

    [Serializable]
    public abstract record Cue
    {
        public CueType type;
        public decimal qid;
        public decimal? parent;
        public Color colour;
        public string name = string.Empty;
        public string description = string.Empty;
        public bool halt;
        public bool enabled;
        public TimeSpan delay;
    }

    [Serializable]
    public record GroupCue : Cue
    {

    }

    [Serializable]
    public record DummyCue : Cue
    {

    }

    [Serializable]
    public record SoundCue : Cue
    {
        public string path = string.Empty;
        public DateTime startTime;
        public TimeSpan duration = TimeSpan.MaxValue;
        public float fadeIn;
        public float fadeOut;
    }

    [Serializable]
    public record TimeCodeCue : Cue
    {
        public DateTime startTime;
        public TimeSpan duration;
    }
}
