using System;
using System.Numerics;

namespace QPlayer.VideoPlugin;

public struct VectorInt2 : IEquatable<VectorInt2>, IFormattable
{
    public int x, y;

    public VectorInt2() { }

    public VectorInt2(int x, int y)
    {
        this.x = x;
        this.y = y;
    }

    public VectorInt2(Vector2 vec)
    {
        this.x = (int)vec.X;
        this.y = (int)vec.Y;
    }

    public static readonly VectorInt2 Zero = new();
    public static readonly VectorInt2 One = new(1, 1);

    public readonly bool Equals(VectorInt2 other) => other.x == x && other.y == y;

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return $"[{x}, {y}]";
    }

    public override readonly bool Equals(object? obj) => obj is VectorInt2 vec && Equals(vec);
    public override readonly int GetHashCode() => HashCode.Combine(x, y);
    public override readonly string ToString() => ToString(null, null);

    public static bool operator ==(VectorInt2 left, VectorInt2 right) => left.Equals(right);
    public static bool operator !=(VectorInt2 left, VectorInt2 right) => !(left == right);
    public static VectorInt2 operator +(VectorInt2 left, VectorInt2 right) => new(left.x + right.x, left.y + right.y);
    public static VectorInt2 operator -(VectorInt2 left, VectorInt2 right) => new(left.x - right.x, left.y - right.y);
    public static VectorInt2 operator *(VectorInt2 left, VectorInt2 right) => new(left.x * right.x, left.y * right.y);
    public static VectorInt2 operator /(VectorInt2 left, VectorInt2 right) => new(left.x / right.x, left.y / right.y);
    public static VectorInt2 operator *(VectorInt2 left, int right) => new(left.x * right, left.y * right);
    public static VectorInt2 operator /(VectorInt2 left, int right) => new(left.x / right, left.y / right);

    public static VectorInt2 Min(VectorInt2 a, VectorInt2 b) => new((a.x > b.x) ? a.x : b.x, (a.y > b.y) ? a.y : b.y);
    public static VectorInt2 Max(VectorInt2 a, VectorInt2 b) => new((a.x < b.x) ? a.x : b.x, (a.y < b.y) ? a.y : b.y);
    public static VectorInt2 Clamp(VectorInt2 x, VectorInt2 min, VectorInt2 max) => Min(Max(x, min), max);
}

public struct VectorInt3 : IEquatable<VectorInt3>, IFormattable
{
    public int x, y, z;

    public VectorInt3() { }

    public VectorInt3(int x, int y, int z)
    {
        this.x = x;
        this.y = y;
        this.z = z;
    }

    public static readonly VectorInt3 Zero = new();

    public readonly bool Equals(VectorInt3 other) => other.x == x && other.y == y && other.z == z;

    public readonly string ToString(string? format, IFormatProvider? formatProvider)
    {
        return $"[{x}, {y}, {z}]";
    }

    public override readonly bool Equals(object? obj) => obj is VectorInt3 vec && Equals(vec);
    public override readonly int GetHashCode() => HashCode.Combine(x, y);
    public override readonly string ToString() => ToString(null, null);

    public static bool operator ==(VectorInt3 left, VectorInt3 right) => left.Equals(right);
    public static bool operator !=(VectorInt3 left, VectorInt3 right) => !(left == right);
    public static VectorInt3 operator +(VectorInt3 left, VectorInt3 right) => new(left.x + right.x, left.y + right.y, left.z + right.z);
    public static VectorInt3 operator -(VectorInt3 left, VectorInt3 right) => new(left.x - right.x, left.y - right.y, left.z - right.z);
    public static VectorInt3 operator *(VectorInt3 left, VectorInt3 right) => new(left.x * right.x, left.y * right.y, left.z * right.z);
    public static VectorInt3 operator /(VectorInt3 left, VectorInt3 right) => new(left.x / right.x, left.y / right.y, left.z / right.z);
    public static VectorInt3 operator *(VectorInt3 left, int right) => new(left.x * right, left.y * right, left.z * right);
    public static VectorInt3 operator /(VectorInt3 left, int right) => new(left.x / right, left.y / right, left.z / right);

    public static VectorInt3 Min(VectorInt3 a, VectorInt3 b) => new((a.x > b.x) ? a.x : b.x, (a.y > b.y) ? a.y : b.y, (a.z > b.z) ? a.z : b.z);
    public static VectorInt3 Max(VectorInt3 a, VectorInt3 b) => new((a.x < b.x) ? a.x : b.x, (a.y < b.y) ? a.y : b.y, (a.z < b.z) ? a.z : b.z);
    public static VectorInt3 Clamp(VectorInt3 x, VectorInt3 min, VectorInt3 max) => Min(Max(x, min), max);
}
