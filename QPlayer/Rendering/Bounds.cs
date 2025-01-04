using System;
using System.Numerics;

namespace QPlayer.Rendering;

public struct Bounds
{
    public Vector3 center;
    public Vector3 size;

    public Bounds() { }

    public Bounds(Vector3 center, Vector3 size)
    {
        this.center = center;
        this.size = size;
    }

    public static Bounds FromMinMax(Vector3 min, Vector3 max)
    {
        var c = (min + max) / 2;
        var s = max - min;
        Bounds ret = new()
        {
            center = c,
            size = s
        };
        return ret;
    }

    public static (Vector3 min, Vector3 max) ToMinMax(Bounds bounds)
    {
        return bounds.ToMinMax();
    }

    public readonly (Vector3 min, Vector3 max) ToMinMax()
    {
        var hsize = size / 2;
        return (center - hsize,
                center + hsize);
    }

    public readonly Bounds Union(Bounds other)
    {
        var a = ToMinMax();
        var b = other.ToMinMax();
        var min = Vector3.Min(a.min, b.min);
        var max = Vector3.Max(a.max, b.max);

        return FromMinMax(min, max);
    }

    public readonly Bounds Intersection(Bounds other)
    {
        var a = ToMinMax();
        var b = other.ToMinMax();
        var min = Vector3.Max(a.min, b.min);
        var max = Vector3.Min(a.max, b.max);

        return FromMinMax(min, max);
    }

    public readonly bool Intersects(in Ray ray)
    {
        float tmin = 0, tmax = float.MaxValue;
        var (min, max) = ToMinMax();

        for (int d = 0; d < 3; d++)
        {
            float t1 = (min[d] - ray.start[d]) * ray.dirInv[d];
            float t2 = (max[d] - ray.start[d]) * ray.dirInv[d];

            tmin = Math.Max(tmin, Math.Min(t1, t2));
            tmax = Math.Min(tmax, Math.Max(t1, t2));
        }

        return tmin <= tmax;
    }
}
