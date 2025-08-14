using QPlayer.Audio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json.Serialization;

namespace QPlayer.Models;

[Serializable]
public record ShowFile
{
    public const int FILE_FORMAT_VERSION = 4;

    public int fileFormatVersion = FILE_FORMAT_VERSION;
    public ShowSettings showSettings = new();
    public List<float> columnWidths = [];
    public List<Cue> cues = [new SoundCue()];
}

[Serializable]
public record ShowSettings
{
    public string title = "Untitled";
    public string description = "";
    public string author = "";
    public DateTime date = DateTime.Today;

    public int audioLatency = 100;
    public AudioOutputDriver audioOutputDriver;
    public string audioOutputDevice = "";

    public string oscNIC = "";
    public int oscRXPort = 9000;
    public int oscTXPort = 8000;

    public bool enableRemoteControl = false;
    public bool isRemoteHost = true;
    public bool syncShowFileOnSave = true;
    public string nodeName = "QPlayer";
    public List<RemoteNode> remoteNodes = [];

    public int mscRXPort = 6004;
    public int mscTXPort = 6004;
    public int mscRXDevice = 0x70;
    public int mscTXDevice = 0x71;
    public int mscExecutor = -1;
    public int mscPage = -1;
}

[Serializable]
public record struct RemoteNode(string name, string address)
{
    public string name = name;
    public string address = address;
}

[Serializable]
public record struct ShaderParameter(string name, float value);

public enum CueType
{
    None,
    GroupCue,
    DummyCue,
    SoundCue,
    TimeCodeCue,
    StopCue,
    VolumeCue
}

public enum LoopMode
{
    OneShot,
    Looped,
    LoopedInfinite,
    HoldLast
}

public enum StopMode
{
    Immediate,
    LoopEnd
}

public enum TriggerMode
{
    Go,
    WithLast,
    AfterLast
}

[Serializable]
[JsonPolymorphic(IgnoreUnrecognizedTypeDiscriminators = true, UnknownDerivedTypeHandling = JsonUnknownDerivedTypeHandling.FallBackToNearestAncestor)]
[JsonDerivedType(typeof(Cue), typeDiscriminator: nameof(Cue))]
[JsonDerivedType(typeof(GroupCue), typeDiscriminator: nameof(GroupCue))]
[JsonDerivedType(typeof(DummyCue), typeDiscriminator: nameof(DummyCue))]
[JsonDerivedType(typeof(SoundCue), typeDiscriminator: nameof(SoundCue))]
[JsonDerivedType(typeof(TimeCodeCue), typeDiscriminator: nameof(TimeCodeCue))]
[JsonDerivedType(typeof(StopCue), typeDiscriminator: nameof(StopCue))]
[JsonDerivedType(typeof(VolumeCue), typeDiscriminator: nameof(VolumeCue))]
public record Cue
{
    public CueType type;
    public decimal qid;
    public decimal? parent;
    public SerializedColour colour = SerializedColour.Black;
    public string name = string.Empty;
    public string description = string.Empty;
    //public bool halt = true;
    public TriggerMode trigger = TriggerMode.Go;
    public bool enabled = true;
    public TimeSpan delay;
    public LoopMode loopMode;
    public int loopCount;
    public string remoteNode = string.Empty;

    public static Cue CreateCue(CueType type) => type switch
    {
        CueType.GroupCue => new GroupCue(),
        CueType.DummyCue => new DummyCue(),
        CueType.SoundCue => new SoundCue(),
        CueType.TimeCodeCue => new TimeCodeCue(),
        CueType.StopCue => new StopCue(),
        CueType.VolumeCue => new VolumeCue(),
        CueType.None => new Cue(),
        _ => throw new NotImplementedException(),
    };
}

[Serializable]
[JsonDerivedType(typeof(GroupCue), typeDiscriminator: nameof(GroupCue))]
public record GroupCue : Cue
{
    public GroupCue() : base()
    {
        type = CueType.GroupCue;
    }
}

[Serializable]
[JsonDerivedType(typeof(DummyCue), typeDiscriminator: nameof(DummyCue))]
public record DummyCue : Cue
{
    public DummyCue() : base()
    {
        type = CueType.DummyCue;
    }
}

[Serializable]
[JsonDerivedType(typeof(SoundCue), typeDiscriminator: nameof(SoundCue))]
public record SoundCue : Cue
{
    public string path = string.Empty;
    public TimeSpan startTime;
    public TimeSpan duration;
    public float volume = 1;
    public float fadeIn;
    public float fadeOut;
    public FadeType fadeType = FadeType.SCurve;

    public SoundCue() : base()
    {
        type = CueType.SoundCue;
    }
}

[Serializable]
[JsonDerivedType(typeof(TimeCodeCue), typeDiscriminator: nameof(TimeCodeCue))]
public record TimeCodeCue : Cue
{
    public TimeSpan startTime;
    public TimeSpan duration;

    public TimeCodeCue() : base()
    {
        type = CueType.TimeCodeCue;
    }
}

[Serializable]
[JsonDerivedType(typeof(StopCue), typeDiscriminator: nameof(StopCue))]
public record StopCue : Cue
{
    public decimal stopQid;
    public StopMode stopMode;
    public float fadeOutTime;
    public FadeType fadeType = FadeType.SCurve;

    public StopCue() : base()
    {
        type = CueType.StopCue;
    }
}

[Serializable]
[JsonDerivedType(typeof(VolumeCue), typeDiscriminator: nameof(VolumeCue))]
public record VolumeCue : Cue
{
    public decimal soundQid;
    public float fadeTime;
    public float volume;
    public FadeType fadeType = FadeType.SCurve;

    public VolumeCue() : base()
    {
        type = CueType.VolumeCue;
    }
}
