using QPlayer.Audio;
using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Collections.ObjectModel;
using System.Text;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace QPlayer.PyPlayPlugin;

/// <summary>
/// Model for the Video Cue
/// </summary>
public record PyVideoCue : Cue
{
    public string path = string.Empty;
    public string shader = string.Empty;
    public int zIndex;
    public string? alphaPath;
    public AlphaMode alphaMode = AlphaMode.Video;
    public TimeSpan startTime;
    public TimeSpan duration;
    public bool stompsOthers;
    public float dimmer = 1;
    public float volume = 1;
    public float fadeIn = 1;
    public float fadeOut = 1;
    public FadeType fadeType = FadeType.SCurve;
    public float brightness = 1;
    public float contrast = 1;
    public float gamma = 1;
    public float scale = 1;
    public float rotation = 0;
    public Vector2 offset = Vector2.Zero;
    public List<ShaderParameter> shaderParameters = [];
    // DMX address as a single integer (1..508). 0 means unset.
    public int dmxAddress = 0;

    public PyVideoCue() : base() { }
}

/// <summary>
/// Model for the Video Framing Cue
/// </summary>
[Serializable]
public record VideoFramingCue : Cue
{
    public List<Vector2> corners = [.. Enumerable.Repeat<Vector2>(default, 4)];
    public List<FramingShutter> framing = [.. Enumerable.Range(0, 4).Select(x => new FramingShutter())];
    public float fadeTime = 0;
    public FadeType fadeType = FadeType.SCurve;

    public VideoFramingCue() : base() { }
}

/// <summary>
/// Model for the Shader Parameters Cue
/// </summary>
[Serializable]
public record ShaderParamsCue : Cue
{
    /// <summary>
    /// Can be numeric or the string "post" for post-processing target.
    /// </summary>
    public string targetQid = string.Empty;
    public List<ShaderParameter> shaderParameters = [];
    public float fadeTime = 0;
    public FadeType fadeType = FadeType.SCurve;
    public bool postProcessing = false;

    public ShaderParamsCue() : base() { }
}

/// <summary>
/// Enum for alpha blending modes
/// </summary>
public enum AlphaMode
{
    Opaque,
    Video,
    Alpha,
    GradientWipe
}

/// <summary>
/// Struct for framing shutter configuration
/// </summary>
public record FramingShutter
{
    public float rotation;
    public float maskStart;
    public float softness;
}

/// <summary>
/// Struct for shader parameters
/// </summary>
[Serializable]
public record class ShaderParameter
{
    public string name = string.Empty;
    public float value;

    public ShaderParameter() { }

    public ShaderParameter(string name, float value)
    {
        this.name = name;
        this.value = value;
    }
}
