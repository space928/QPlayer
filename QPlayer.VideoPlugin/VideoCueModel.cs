using QPlayer.Audio;
using QPlayer.Models;
using System;
using System.Numerics;

namespace QPlayer.VideoPlugin;

public record VideoCueModel : Cue
{
    public string videoFile = string.Empty;
    public TimeSpan startTime;
    public TimeSpan endTime;
    public float fadeIn;
    public float fadeOut;
    public FadeType fadeType = FadeType.SCurve;

    public BlendMode blendMode;
    public float opacity;

    public ScalingMode scalingMode;
    public float layer;
    public ImageTransform? transform;

    public bool enableAudio;
    public float volume;
    public float pan;
    public EQSettings? eq;
}

public record ImageTransform
{
    public Vector2 position;
    public Vector2 scale;
    public float rotation;
    public Vector2 anchor;
    public Vector4 crop;

    public GridMeshTransform? mesh;
}

public record GridMeshTransform
{

}

public enum BlendMode
{
    Normal,

    Screen,
    Lighten,
    Add,

    Darken,
    Multiply,
    Subtract,

    Overlay,
    SoftLight,
    HardLight,

    Luminosity,
    Hue,
    Saturation,
    Colour
}

public enum ScalingMode
{
    Fill,
    Fit,
    Stretch
}
