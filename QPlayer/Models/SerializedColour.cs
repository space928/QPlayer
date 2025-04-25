using ColorPicker.Models;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace QPlayer.Models;

/// <summary>
/// A simple RGBA colour represented as 4 singles. Provides explicit 
/// conversion operators to a number of formats.
/// </summary>
[Serializable]
public struct SerializedColour : IEquatable<SerializedColour>
{
    public float r, g, b, a;

    public static readonly SerializedColour Transparent = new();
    public static readonly SerializedColour Black = new(0,0,0,1);
    public static readonly SerializedColour White = new(1,1,1,1);
    public static readonly SerializedColour Red = new(1,0,0,1);
    public static readonly SerializedColour Green = new(0,1,0,1);
    public static readonly SerializedColour Blue = new(0,0,1,1);

    public SerializedColour() { }

    public SerializedColour(float r, float g, float b, float a)
    {
        this.r = r;
        this.g = g;
        this.b = b;
        this.a = a;
    }

    public readonly override int GetHashCode()
    {
        return HashCode.Combine(r, g, b, a);
    }

    public readonly override string ToString() => $"[R: {r:F3}; G: {g:F3}; B: {b:F3}; A: {a:F3}]";

    public readonly override bool Equals([NotNullWhen(true)] object? obj)
    {
        if (obj is SerializedColour col)
            return Equals(col);
        return false;
    }

    public readonly bool Equals(SerializedColour other) => r == other.r && g == other.g && b == other.b && a == other.a;
    public static bool operator ==(SerializedColour left, SerializedColour right) => left.Equals(right);
    public static bool operator !=(SerializedColour left, SerializedColour right) => !(left == right);
    public static SerializedColour operator *(SerializedColour left, SerializedColour right)
    {
        ref var a = ref Unsafe.As<SerializedColour, Vector4>(ref left);
        ref var b = ref Unsafe.As<SerializedColour, Vector4>(ref right);
        var c = a * b;
        return Unsafe.As<Vector4, SerializedColour>(ref c);
    }

    public static explicit operator Color(SerializedColour x) => Color.FromArgb((byte)(x.a * 255), (byte)(x.r * 255), (byte)(x.g * 255), (byte)(x.b * 255));
    public static explicit operator ColorState(SerializedColour x)
    {
        var colState = new ColorState();
        colState.SetARGB(x.a, x.r, x.g, x.b);
        return colState;
    }
    public static explicit operator Vector4(SerializedColour x) => Unsafe.As<SerializedColour, Vector4>(ref x);

    public static explicit operator SerializedColour(Color x) => new(x.R / 255f, x.G / 255f, x.B / 255f, x.A / 255f);
    public static explicit operator SerializedColour(ColorState x) => new((float)x.RGB_R, (float)x.RGB_G, (float)x.RGB_B, (float)x.A);
    public static explicit operator SerializedColour(Vector4 x) => Unsafe.As<Vector4, SerializedColour>(ref x);
}
