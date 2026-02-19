using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

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

[StructLayout(LayoutKind.Sequential)]
public struct EQBand : IEquatable<EQBand>
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

    public readonly override bool Equals(object? obj)
    {
        return obj is EQBand band && Equals(band);
    }

    public readonly bool Equals(EQBand other)
    {
        return freq == other.freq &&
               gain == other.gain &&
               q == other.q &&
               shape == other.shape;
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(freq, gain, q, shape);
    }

    public static bool operator ==(EQBand left, EQBand right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EQBand left, EQBand right)
    {
        return !(left == right);
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
