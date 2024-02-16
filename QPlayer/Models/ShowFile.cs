using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.Json.Serialization;
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
        None,
        GroupCue,
        DummyCue,
        SoundCue,
        TimeCodeCue,
        StopCue,
    }

    public enum LoopMode
    {
        OneShot,
        Looped,
        LoopedInfinite,
    }

    public enum StopMode
    {
        Immediate,
        LoopEnd
    }

    [Serializable]
    [JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true, UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
    [JsonDerivedType(typeof(Cue), typeDiscriminator: nameof(Cue))]
    public record Cue
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
        public LoopMode loopMode;
        public int loopCount;
    }

    [Serializable]
    [JsonDerivedType(typeof(Cue), typeDiscriminator: nameof(GroupCue))]
    public record GroupCue : Cue
    {
        public GroupCue() : base()
        {
            type = CueType.GroupCue;
        }
    }

    [Serializable]
    [JsonDerivedType(typeof(Cue), typeDiscriminator: nameof(DummyCue))]
    public record DummyCue : Cue
    {
        public DummyCue() : base()
        {
            type = CueType.DummyCue;
        }
    }

    [Serializable]
    [JsonDerivedType(typeof(Cue), typeDiscriminator: nameof(SoundCue))]
    public record SoundCue : Cue
    {
        public string path = string.Empty;
        public DateTime startTime;
        public TimeSpan duration = TimeSpan.MaxValue;
        public float fadeIn;
        public float fadeOut;

        public SoundCue() : base()
        {
            type = CueType.SoundCue;
        }
    }

    [Serializable]
    [JsonDerivedType(typeof(Cue), typeDiscriminator: nameof(TimeCodeCue))]
    public record TimeCodeCue : Cue
    {
        public DateTime startTime;
        public TimeSpan duration;

        public TimeCodeCue() : base()
        {
            type = CueType.TimeCodeCue;
        }
    }

    [Serializable]
    [JsonDerivedType(typeof(Cue), typeDiscriminator: nameof(StopCue))]
    public record StopCue : Cue
    {
        public decimal stopQid;
        public StopMode stopMode;

        public StopCue() : base()
        {
            type = CueType.StopCue;
        }
    }
}
