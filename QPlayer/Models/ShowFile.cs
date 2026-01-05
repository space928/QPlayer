using QPlayer.Audio;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Numerics;
using System.Text.Json.Serialization;

namespace QPlayer.Models;

public record ShowFile
{
    public const int FILE_FORMAT_VERSION = 6;

    public int fileFormatVersion = FILE_FORMAT_VERSION;
    public ShowSettings showSettings = new();
    public List<float> columnWidths = [];
    public List<Cue> cues = [];
}

public record ShowSettings
{
    public string title = "Untitled";
    public string description = "";
    public string author = "";
    public DateTime date = DateTime.Today;

    public int audioLatency = 10;
    public AudioOutputDriver audioOutputDriver = AudioOutputDriver.WASAPI;
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

public record struct RemoteNode(string name, string address)
{
    public string name = name;
    public string address = address;
}

public record struct ShaderParameter(string name, float value);

/*public enum CueType
{
    None,
    GroupCue,
    DummyCue,
    SoundCue,
    TimeCodeCue,
    StopCue,
    VolumeCue
}*/

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

public record Cue
{
    //public CueType type;
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
    public int loopCount = 1;
    public string remoteNode = string.Empty;
}

public record GroupCue : Cue
{
    public GroupCue() : base() { }
}

public record DummyCue : Cue
{
    public DummyCue() : base() { }
}

public record SoundCue : Cue
{
    public string path = string.Empty;
    public TimeSpan startTime;
    public TimeSpan duration;
    public float volume = 1;
    public float fadeIn;
    public float fadeOut;
    public FadeType fadeType = FadeType.SCurve;
    public EQSettings? eq;

    public SoundCue() : base() { }
}

public record TimeCodeCue : Cue
{
    public TimeSpan startTime;
    public TimeSpan duration;

    public TimeCodeCue() : base() { }
}

public record StopCue : Cue
{
    public decimal stopQid;
    public StopMode stopMode;
    public float fadeOutTime;
    public FadeType fadeType = FadeType.SCurve;

    public StopCue() : base() { }
}

public record VolumeCue : Cue
{
    public decimal soundQid;
    public float fadeTime;
    public float volume;
    public FadeType fadeType = FadeType.SCurve;

    public VolumeCue() : base() { }
}
