using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Audio;

internal static class VectorExtensions
{
    /// <summary>
    /// Multiplies the contents of span a by the scalar b, mutating a with the result.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="a"></param>
    /// <param name="b"></param>
    public static void Multiply<T>(Span<T> a, T b)
        where T : INumber<T>
    {
        int len = a.Length;
        int i = 0;
        if (len < Vector<T>.Count || !Vector.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector<T>.Count;

        var vecB = new Vector<T>(b);
        ref T dst = ref MemoryMarshal.GetReference(a);
        /*
         Inner loop compiles to (x86_64 with AVX2, .NET 8, PGO Tier 1):
               vmulps   ymm2, ymm0, ymmword ptr [r8]
               vmovups  ymmword ptr [r8], ymm2
               add      r8, 32
               add      r9d, 8
               cmp      r11d, r9d
         */
        for (; i < len - remaining; i += Vector<T>.Count)
        {
            //var vecA = new Vector<T>(ref dst);
            var vecA = Vector.LoadUnsafe(ref dst);
            var res = Vector.Multiply(vecA, vecB);
            ref var dstByte = ref Unsafe.As<T, byte>(ref dst);
            Unsafe.WriteUnaligned(ref dstByte, res);
            dst = ref Unsafe.Add(ref dst, Vector<T>.Count);
        }

    CopyRemaining:
        for (; i < len; i++)
            a[i] *= b;
    }

    /// <summary>
    /// Multiplies the pairs of values in a by the values in b, mutating a with the result.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="a"></param>
    /// <param name="b"></param>
    public static void Multiply<T>(Span<T> a, Span<T> b)
        where T : INumber<T>
    {
        int len = a.Length;
        ArgumentOutOfRangeException.ThrowIfNotEqual(len, b.Length);
        int i = 0;
        if (len < Vector<T>.Count || !Vector.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector<T>.Count;

        ref T dst = ref MemoryMarshal.GetReference(a);
        ref T bRef = ref MemoryMarshal.GetReference(b);
        for (; i < len - remaining; i += Vector<T>.Count)
        {
            var vecA = Vector.LoadUnsafe(ref dst);
            var vecB = Vector.LoadUnsafe(ref bRef);

            var res = Vector.Multiply(vecA, vecB);
            ref var dstByte = ref Unsafe.As<T, byte>(ref dst);
            Unsafe.WriteUnaligned(ref dstByte, res);

            dst = ref Unsafe.Add(ref dst, Vector<T>.Count);
            bRef = ref Unsafe.Add(ref bRef, Vector<T>.Count);
        }

    CopyRemaining:
        for (; i < len; i++)
            a[i] *= b[i];
    }

    public static void ApplyPan(Span<float> buffer, float pan)
    {
        int len = buffer.Length;
        int i = 0;
        Vector2 lr = new(1 - Math.Max(0, pan), 1 + Math.Min(0, pan));
        if (len < Vector<float>.Count || !Vector.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector<float>.Count;

        // Create a vector of LRLRLRLR values by interpretting lr as a double and broadcasting it to the vector.
        var vecB = new Vector<double>(Unsafe.As<Vector2, double>(ref lr)).As<double, float>();
        ref float dst = ref MemoryMarshal.GetReference(buffer);
        for (; i < len - remaining; i += Vector<float>.Count)
        {
            var vecA = Vector.LoadUnsafe(ref dst);
            var res = Vector.Multiply(vecA, vecB);
            ref var dstByte = ref Unsafe.As<float, byte>(ref dst);
            Unsafe.WriteUnaligned(ref dstByte, res);
            dst = ref Unsafe.Add(ref dst, Vector<float>.Count);
        }

    CopyRemaining:
        for (; i < len; i += 2)
        {
            buffer[i] *= lr.X;
            buffer[i + 1] *= lr.Y;
        }
    }

    public static Span<float> Max(Span<float> x, float y)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            var vt = Vector256.Create(y);
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var v = Vector256.LoadUnsafe(in xRef, i);
                v = Avx.Max(v, vt);
                v.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var p = ref Unsafe.Add(ref xRef, i);
            if (p < y)
                p = y;
        }

        return x;
    }

    public static Span<float> Max(Span<float> x, Span<float> y)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        ref var yRef = ref MemoryMarshal.GetReference(y);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var vx = Vector256.LoadUnsafe(in xRef, i);
                var vy = Vector256.LoadUnsafe(in yRef, i);
                vx = Avx.Max(vx, vy);
                vx.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var px = ref Unsafe.Add(ref xRef, i);
            var py = Unsafe.Add(ref yRef, i);
            if (px < py)
                px = py;
        }

        return x;
    }

    /// <summary>
    /// Computes the element-wise minimum of x and y, storing the result in x.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
    public static Span<float> Min(Span<float> x, ReadOnlySpan<float> y)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        ref var yRef = ref MemoryMarshal.GetReference(y);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var vx = Vector256.LoadUnsafe(in xRef, i);
                var vy = Vector256.LoadUnsafe(in yRef, i);
                vx = Avx.Min(vx, vy);
                vx.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var px = ref Unsafe.Add(ref xRef, i);
            var py = Unsafe.Add(ref yRef, i);
            if (px > py)
                px = py;
        }

        return x;
    }

    /// <summary>
    /// Computes the minimum value in x.
    /// </summary>
    /// <param name="x"></param>
    /// <returns></returns>
    public static float Min(ReadOnlySpan<float> x)
    {
        float res = float.MaxValue;
        int i = 0;
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            ref readonly var xRef = ref x[0];
            // Parallel min
            var minVec = Vector256.Create(res);
            for (; i < x.Length - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                var va = Vector256.LoadUnsafe(in xRef, (nuint)i);
                minVec = Avx.Min(minVec, va);
            }

            // Reduce
            var p = Sse.Min(minVec.GetLower(), minVec.GetUpper());
            p = Sse.Min(p, Sse2.Shuffle(p.AsInt32(), 2 + (3 << 2)).AsSingle());
            res = MathF.Min(p[0], p[1]);
        }
        for (; i < x.Length; i++)
        {
            float y = x[i];
            if (y < res)
                res = y;
        }
        return res;
    }

    /// <summary>
    /// Computes the pair-wise minimum of x, storing the result in x.
    /// </summary>
    /// <param name="x"></param>
    /// <returns></returns>
    public static Span<float> StereoMin(Span<float> x)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var vx = Vector256.LoadUnsafe(in xRef, i);
                // Swap each LR pair
                var vy = Avx2.Shuffle(vx.AsInt32(), 1 | (0 << 2) | (3 << 4) | (2 << 6)).AsSingle();
                vx = Avx.Min(vx, vy);
                vx.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i += 2)
        {
            ref var px = ref Unsafe.Add(ref xRef, i);
            ref var py = ref Unsafe.Add(ref xRef, i | 1);
            px = py = MathF.Min(px, py);
        }

        return x;
    }

    /// <summary>
    /// Computes: ∑(x[i] * kernel[i])
    /// </summary>
    /// <param name="x"></param>
    /// <param name="kernel"></param>
    /// <returns></returns>
    public static float Convolve(ReadOnlySpan<float> x, ReadOnlySpan<float> kernel)
    {
        float res = 0;
        int i = 0;
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            ref readonly var xRef = ref x[0];
            ref readonly var wRef = ref kernel[0];
            // Parallel sum
            var sumVec = Vector256.Create(res);
            for (; i < x.Length - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                var va = Vector256.LoadUnsafe(in xRef, (nuint)i);
                var vw = Vector256.LoadUnsafe(in wRef, (nuint)i);
                sumVec += va * vw;
            }

            // Reduce
            var p = sumVec.GetLower() + sumVec.GetUpper();
            p += Sse2.Shuffle(p.AsInt32(), 2 + (3 << 2)).AsSingle();
            res = p[0] + p[1];
        }
        for (; i < x.Length; i++)
        {
            float y = x[i];
            float w = kernel[i];
            res += y * w;
        }
        return res;
    }

    /// <summary>
    /// Computes the gain reduction required to clip a signal x by threshold.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="gain"></param>
    /// <param name="threshold"></param>
    /// <returns></returns>
    public static Span<float> ClipGR(Span<float> x, float gain, float threshold)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            var gainVec = Vector256.Create(gain);
            var threshVec = Vector256.Create(threshold);
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var vx = Vector256.LoadUnsafe(in xRef, i);
                vx = Vector256.Abs(vx) * gainVec;
                var v1 = Vector256.GreaterThan(vx, threshVec);
                vx = Avx.BlendVariable(Vector256<float>.One, threshVec / vx, v1);
                vx.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var px = ref Unsafe.Add(ref xRef, i);
            var v = MathF.Abs(px) * gain;
            px = v > threshold ? threshold / v : 1;
        }

        return x;
    }
}
