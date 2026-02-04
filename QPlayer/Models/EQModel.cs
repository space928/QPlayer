using System;

namespace QPlayer.Models;

public record EQSettings
{
    public bool enabled;
    public EQBand band1;
    public EQBand band2;
    public EQBand band3;
    public EQBand band4;
    public EQFilter hpf;
    public EQFilter lpf;
}

public struct EQBand
{
    public float freq;
    public float gain;
    public float q;
    public EQBandShape shape;

    public EQBand() { }

    public EQBand(float freq, float gain, float q, EQBandShape shape)
    {
        this.freq = freq;
        this.gain = gain;
        this.q = q;
        this.shape = shape;
    }
}

public enum EQBandShape
{
    Bell,
    HighShelf,
    LowShelf,
    Notch
}

public struct EQFilter
{
    public float frequency;
    public EQFilterOrder order;
}

public enum EQFilterOrder
{
    Disabled,
    _12dBOct,
    _24dBOct,
}
