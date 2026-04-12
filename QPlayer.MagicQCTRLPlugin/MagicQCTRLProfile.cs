using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Windows.Media;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static QPlayer.MagicQCTRLPlugin.USBDriver;

namespace QPlayer.MagicQCTRLPlugin;

[Serializable]
public struct MagicQCTRLProfile
{
    [MinLength(MAX_PAGES), MaxLength(MAX_PAGES)]
    public MagicQCTRLPage[] pages;
    public float baseBrightness;
    public float pressedBrightness;

    public MagicQCTRLProfile()
    {
        pages = Enumerable.Range(0, MAX_PAGES).Select(x => new MagicQCTRLPage()).ToArray();
        baseBrightness = 0.5f;
        pressedBrightness = 3.0f;
    }
}

[Serializable]
public struct MagicQCTRLPage
{
    [MinLength(BUTTON_COUNT), MaxLength(BUTTON_COUNT)]
    public MagicQCTRLKey[] keys;

    public MagicQCTRLPage()
    {
        keys = new MagicQCTRLKey[BUTTON_COUNT];
    }
}

[Serializable]
public struct MagicQCTRLEncoder
{
    public float scaleValue;
}

[Serializable]
public struct MagicQCTRLKey
{
    public string name = string.Empty;
    public int customKeyCode;
    public MagicQCTRLColour keyColourOn;
    public MagicQCTRLColour keyColourOff;
    public Action? onPress;
    public Action<sbyte>? onRotate;

    public MagicQCTRLKey() { }
}

[Serializable]
public struct MagicQCTRLColour
{
    public byte r, g, b;

    public MagicQCTRLColour(byte r, byte g, byte b)
    {
        this.r = r;
        this.g = g;
        this.b = b;
    }

    public static implicit operator Color(MagicQCTRLColour other) => Color.FromArgb(255, other.r, other.g, other.b);
    public static implicit operator MagicQCTRLColour(Color other) => new() { r = other.R, g = other.G, b = other.B };
    public static MagicQCTRLColour operator *(MagicQCTRLColour a, float x) => new()
    {
        r = (byte)Math.Clamp(a.r * x, 0, 255),
        g = (byte)Math.Clamp(a.g * x, 0, 255),
        b = (byte)Math.Clamp(a.b * x, 0, 255)
    };

    public MagicQCTRLColour Pow(float x) => new()
    {
        r = (byte)Math.Clamp(Math.Pow(r / 255f, x) * 255, 0, 255),
        g = (byte)Math.Clamp(Math.Pow(g / 255f, x) * 255, 0, 255),
        b = (byte)Math.Clamp(Math.Pow(b / 255f, x) * 255, 0, 255)
    };
}
