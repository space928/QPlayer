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

/// <summary>
/// This class provides a number of vectorised maths routines, which are optimised for X86 SIMD instructions.
/// </summary>
internal static class VectorExtensions
{
    /// <summary>
    /// Pretty prints an audio signal as a waveform. Based on https://github.com/sudara/melatonin_audio_sparklines/blob/main/melatonin_audio_sparklines.h
    /// <para/>
    /// The signal is represented as a series of dashes, with a few special characters indicated specific values:
    /// <code>
    /// 0     -- a single 0 value
    /// 0(23) -- 23 consecutive zeros
    /// x     -- a zero-crossing
    /// N     -- NaN
    /// I     -- +-Inf
    /// S     -- subnormal values
    /// E     -- outside of the -1 to 1 range
    /// </code>
    /// For interleaved multi-channel signals, each chanel is displayed on it's own line.
    /// If a signal is prepended with a + then every sample is >= 0.
    /// </summary>
    /// <param name="sig">The signal to display.</param>
    /// <param name="channels">The number of interleaved channels.</param>
    /// <param name="collapse">Whether the signal should be collapsed to skip repeated values.</param>
    /// <param name="normalize">Whether the signal should be normalized.</param>
    /// <param name="maxLen">The maximum number of characters to return.</param>
    /// <returns></returns>
    public static string PrintSignal(ReadOnlySpan<float> sig, int channels = 1, bool collapse = true, bool normalize = true, int maxLen = 120)
    {
        string waveform = "_⎽⎼—⎻⎺‾";//"_\xe2\x8e\xbd\xe2\x8e\xbc\xe2\x80\x94\xe2\x8e\xbb\xe2\x8e\xba\xe2\x80\xbe";
        StringBuilder sb = new();
        for (int c = 0; c < channels; c++)
        {
            float max = float.MinValue;
            float min = float.MaxValue;
            for (int i = c; i < sig.Length; i += channels)
            {
                float x = sig[i];
                max = Math.Max(max, x);
                min = Math.Min(min, x);
            }
            bool pos = min >= 0;
            float trueMin = min;
            min = MathF.Abs(min);
            float trueMax = MathF.Max(min, max);

            if (pos)
                sb.Append('+');
            sb.Append('[');
            int numZeros = 0;
            for (int i = 0; i < sig.Length; i += channels)
            {
                float xRaw = sig[i];
                float x = normalize && trueMax > 1e-9 ? xRaw / trueMax : xRaw;

                char ch;
                if (MathF.Abs(x) <= float.Epsilon)
                {
                    ch = '0';
                    numZeros++;
                }
                else if ((i > 0) && ((x < 0) != (sig[i - channels] < 0)))
                    ch = 'x';
                else if (float.IsNaN(x))
                    ch = 'N';
                else if (float.IsInfinity(x))
                    ch = 'I';
                else if (float.IsSubnormal(x))
                    ch = 'S';
                else if (float.Abs(xRaw) - float.Epsilon > 1.0f)
                    ch = 'E';
                else
                {
                    float xNrm = pos ? x : ((x + 1) * 0.5f);
                    ch = waveform[(int)(xNrm * 6.999f)];
                }

                if (collapse && (((ch != '0') && numZeros > 1) || ((ch == '0') && i == sig.Length - 1)))
                {
                    sb.Append('(').Append(numZeros).Append(')');
                    numZeros = 0;
                }
                else if ((ch != '0') && numZeros == 1)
                {
                    sb.Append(ch);
                    numZeros = 0;
                }
                else if (i == 0 || !collapse || (ch != sb[^1]))
                {
                    sb.Append(ch);
                }

                if (sb.Length >= maxLen)
                {
                    sb.Append('…');
                    break;
                }
            }
            sb.AppendLine("]");
            sb.AppendLine($"  range: {trueMin:F3} // {max:F3}");
        }

        return sb.ToString();
    }

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

    /// <summary>
    /// Pans a span of interleaved stereo samples left or right.
    /// <code>
    /// Lout = Lin * 1 - max(0,pan)
    /// Rout = Rin * 1 + min(0,pan)
    /// </code>
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="pan"></param>
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

    /// <summary>
    /// Computes the element-wise maximum of a signal and a constant.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
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

    /// <summary>
    /// Computes the element-wise maximum of two signals.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <returns></returns>
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
    /// Computes the smooth-minimum of two signals using quadratic interpolation.
    /// See: https://iquilezles.org/articles/smin/
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="k">The blending factor [0-1]. Must be > 0.</param>
    /// <returns></returns>
    public static Span<float> SmoothMin(Span<float> x, ReadOnlySpan<float> y, float k)
    {
        /*
            // Derived from: https://iquilezles.org/articles/smin/
            float smin( float a, float b, float k )
            {
                k *= 4.0;
                float h = max( k-abs(a-b), 0.0 );
                return min(a,b) - h*h*k*(1.0/4.0);
            }
        */
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        ref var yRef = ref MemoryMarshal.GetReference(y);
        float ik = .25f / k;
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            var vk = Vector256.Create(k);
            var vik = Vector256.Create(ik);
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var vx = Vector256.LoadUnsafe(in xRef, i);
                var vy = Vector256.LoadUnsafe(in yRef, i);
                var vh = Avx.Max(vk - Vector256.Abs(vx - vy), Vector256<float>.Zero);
                vx = Avx.Min(vx, vy) - vh * vh * vik;
                vx.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var px = ref Unsafe.Add(ref xRef, i);
            var py = Unsafe.Add(ref yRef, i);
            float h = MathF.Max(k - MathF.Abs(px - py), 0);
            px = MathF.Min(px, py) - h * h * ik;
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
    /// Computes the gain reduction (fraction) required to clip a signal x by threshold.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="threshold"></param>
    /// <returns></returns>
    public static Span<float> ClipGR(Span<float> x, float threshold)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            var threshVec = Vector256.Create(threshold);
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var vx = Vector256.LoadUnsafe(in xRef, i);
                vx = Vector256.Abs(vx);
                vx = Avx.Min(Vector256<float>.One, threshVec / vx);
                vx.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var px = ref Unsafe.Add(ref xRef, i);
            var v = MathF.Abs(px);
            px = MathF.Min(threshold / v, 1);
        }

        return x;
    }

    /// <summary>
    /// Computes the gain reduction (fraction) required to clip a signal x by threshold.
    /// </summary>
    /// <param name="x"></param>
    /// <param name="threshold">The threshold level at which to start reducing gain.</param>
    /// <param name="ratio">Value between 0-1, where 1 is brickwall limiting.</param>
    /// <param name="knee">Value between 0-1, where 0 is a hard knee. Value must be > 0.</param>
    /// <param name="gain">Gain multiplier applied to the input signal before clipping.</param>
    /// <returns></returns>
    public static Span<float> SoftGR(Span<float> x, float threshold, float ratio, float knee, float gain)
    {
        /*
         h(a, b, k) = max(k - abs(a - b), 0)
         smin(a, b, k) = min(a, b) - h(a, b, k)**2 * 1/4k
         gr = smin(1, threshold/x, knee) * ratio + 1 - ratio
         */
        // Precompute a few constants we'll need
        float rm = 1 - ratio;
        float ik = 0.25f / knee;

        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        if (Avx2.IsSupported && Fma.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            var vt = Vector256.Create(threshold);
            var vk = Vector256.Create(knee);
            var vr = Vector256.Create(ratio);
            var vrm = Vector256.Create(rm);
            var vik = Vector256.Create(ik);
            var vg = Vector256.Create(gain);
            var vo = Vector256<float>.One;
            var vz = Vector256<float>.Zero;
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var vx = Vector256.LoadUnsafe(in xRef, i);
                vx = Vector256.Abs(vx) * vg;
                var a = vt / vx;
                var h = Avx.Max(vk - Vector256.Abs(vo - a), vz);
                var s = Avx.Min(vo, a) - h * h * vik;
                vx = Fma.MultiplyAdd(s, vr, vrm);
                vx.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var px = ref Unsafe.Add(ref xRef, i);
            var x1 = MathF.Abs(px) * gain;
            var a = threshold / x1;
            var h = MathF.Max(knee - MathF.Abs(1 - a), 0);
            var s = MathF.Min(1, a) - h * h * ik;
            px = s * ratio + rm;
        }

        return x;
    }

    private static Span<float> SoftClip(Span<float> x, float y)
    {
        // https://www.desmos.com/calculator/dq0mjejv3t

        throw new NotImplementedException();
    }

    /// <summary>
    /// Multiplies two signals x and y element-wise and then clamps the resulting signal between [-c, c].
    /// </summary>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="c"></param>
    /// <returns></returns>
    public static Span<float> MulClip(Span<float> x, ReadOnlySpan<float> y, float c)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ArgumentOutOfRangeException.ThrowIfNotEqual(l, (nuint)y.Length);
        ref var xRef = ref MemoryMarshal.GetReference(x);
        ref var yRef = ref MemoryMarshal.GetReference(y);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            var vmax = Vector256.Create(-c);
            var vmin = Vector256.Create(c);
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var v = Vector256.LoadUnsafe(in xRef, i);
                v *= Vector256.LoadUnsafe(in yRef, i);
                v = Avx.Max(v, vmax);
                v = Avx.Min(v, vmin);
                v.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var p = ref Unsafe.Add(ref xRef, i);
            float p1 = p * Unsafe.Add(ref yRef, i);
            if (p1 > c)
                p = c;
            else if (p1 < -c)
                p = -c;
            else
                p = p1;
        }

        return x;
    }

    public static Span<float> HardClip(Span<float> x, float y)
    {
        nuint i = 0;
        nuint l = (nuint)x.Length;
        ref var xRef = ref MemoryMarshal.GetReference(x);
        if (Avx2.IsSupported && x.Length > Vector256<float>.Count * 2)
        {
            var vmax = Vector256.Create(-y);
            var vmin = Vector256.Create(y);
            for (; i < l - (nuint)Vector256<float>.Count; i += (nuint)Vector256<float>.Count)
            {
                var v = Vector256.LoadUnsafe(in xRef, i);
                v = Avx.Max(v, vmax);
                v = Avx.Min(v, vmin);
                v.StoreUnsafe(ref xRef, i);
            }
        }
        for (; i < l; i++)
        {
            ref var p = ref Unsafe.Add(ref xRef, i);
            if (p > y)
                p = y;
            else if (p < -y)
                p = -y;
        }

        return x;
    }

    internal static unsafe int LerpSamplesMono(Span<float> dst, ReadOnlySpan<float> src, ref double srcPos, double drsPos)
    {
        int len = Math.Min(dst.Length, (int)(src.Length / drsPos)) - (Vector256<float>.Count * 2);
        if (!Fma.IsSupported || !Avx2.IsSupported || len <= 0)
            return 0;

        ref var dstRef = ref MemoryMarshal.GetReference(dst);
        nuint i = 0;

        fixed (float* srcPtr = &src[0])
        {
            // Here we attempt to vectorise a linear resampling operation
            // In essence, for each destination sample, we sample 2 source samples and lerp between them.
            // The source samples being samples are not necessarily consecutive, hence we need to gather
            // samples before lerping in parallel.
            // There may be some specific cases (friendly values of drsPos) which would allow us to sample
            // src consecutively which would improve performance, this is not done here for simplicity.
            var srcPosVec = Vector256.Create(srcPos);
            var drsPosInc = Vector256.Create(drsPos * 8);
            var incVecA = Vector256.Create(0d, 1d, 2d, 3d) * drsPos;
            var incVecB = Vector256.Create(4d, 5d, 6d, 7d) * drsPos;
            for (; i < (nuint)len; i += (nuint)Vector256<float>.Count)
            {
                // We split this into two vectors so this can be done in parallel on Avx2 targets, as
                // this is computed as doubles, once converted back to integers then we truncate back
                // to 32 bit.
                var srcPosA = incVecA + srcPosVec;
                var srcPosB = incVecB + srcPosVec;
                var srcPosIntA = Avx.ConvertToVector128Int32WithTruncation(srcPosA);
                var srcPosIntB = Avx.ConvertToVector128Int32WithTruncation(srcPosB);
                var srcPosIntLo = Vector256.Create(srcPosIntA, srcPosIntB);
                var srcPosIntHi = srcPosIntLo + Vector256<int>.One;
                // Gather source samples
                var srcLo = Avx2.GatherVector256(srcPtr, srcPosIntLo, 4);
                var srcHi = Avx2.GatherVector256(srcPtr, srcPosIntHi, 4);

                // Compute the lerp fraction while in double and then truncate down to floats
                var ta = Avx.ConvertToVector128Single(srcPosA - Avx.Floor(srcPosA));
                var tb = Avx.ConvertToVector128Single(srcPosB - Avx.Floor(srcPosB));
                var t = Vector256.Create(ta, tb);
                // Lerp
                // v0 + t * (v1 - v0)
                var res = Fma.MultiplyAdd(t, srcHi - srcLo, srcLo);
                res.StoreUnsafe(ref dstRef, i);

                srcPosVec += drsPosInc;
            }
            srcPos = srcPosVec[0];
        }

        return (int)i;
    }

    /// <summary>
    /// Exactly the same as <see cref="LerpSamplesMono(Span{float}, ReadOnlySpan{float}, ref double, double)"/>
    /// but processes interleaved stereo samples instead.
    /// Only processes samples tht can be vectorised, 
    /// </summary>
    /// <param name="dst">Resampling destination.</param>
    /// <param name="src">Resampling source.</param>
    /// <param name="srcPos">The fractional position in the source buffer.</param>
    /// <param name="drsPos">The increment size when sampling the source buffer.</param>
    /// <returns>The number of interpolated samples written.</returns>
    internal static unsafe int LerpSamplesStereo(Span<float> dst, ReadOnlySpan<float> src, ref double srcPos, double drsPos)
    {
        int len = Math.Min(dst.Length, (int)(src.Length / drsPos)) - (Vector256<float>.Count * 4);
        if (!Fma.IsSupported || !Avx2.IsSupported || len <= 0)
            return 0;

        ref var dstRef = ref MemoryMarshal.GetReference(dst);
        nuint i = 0;
        fixed (float* srcPtr = &src[0])
        {
            // Here we attempt to vectorise a linear resampling operation
            // In essence, for each destination sample, we sample 2 source samples and lerp between them.
            // The source samples being samples are not necessarily consecutive, hence we need to gather
            // samples before lerping in parallel.
            // There may be some specific cases (friendly values of drsPos) which would allow us to sample
            // src consecutively which would improve performance, this is not done here for simplicity.
            var srcPosVec = Vector256.Create(srcPos);
            var drsPosInc = Vector256.Create(drsPos * 8);
            var incVecA = Vector256.Create(0d, 1d, 2d, 3d) * drsPos;
            var incVecB = Vector256.Create(4d, 5d, 6d, 7d) * drsPos;
            var permA = Vector256.Create(0, 0, 1, 1, 2, 2, 3, 3);
            for (; i < (nuint)len; i += (nuint)Vector256<float>.Count * 2)
            {
                // We split this into two vectors so this can be done in parallel on Avx2 targets, as
                // this is computed as doubles, once converted back to integers then we truncate back
                // to 32 bit.
                var srcPosA = incVecA + srcPosVec;
                var srcPosB = incVecB + srcPosVec;
                var srcPosIntA = Avx.ConvertToVector128Int32WithTruncation(srcPosA);
                var srcPosIntB = Avx.ConvertToVector128Int32WithTruncation(srcPosB);
                var srcPosIntAHi = srcPosIntA + Vector128<int>.One;
                var srcPosIntBHi = srcPosIntB + Vector128<int>.One;
                // Gather source samples
                // As we're loading pairs of stereo samples, we need to do this in four gathers to
                // end up with two Vec256 of floats. We gather as doubles instead of as singles as
                // the throughput of the former is better.
                var srcLoA = Avx2.GatherVector256((double*)srcPtr, srcPosIntA, 8).AsSingle();
                var srcLoB = Avx2.GatherVector256((double*)srcPtr, srcPosIntB, 8).AsSingle();
                var srcHiA = Avx2.GatherVector256((double*)srcPtr, srcPosIntAHi, 8).AsSingle();
                var srcHiB = Avx2.GatherVector256((double*)srcPtr, srcPosIntBHi, 8).AsSingle();

                // Compute the lerp fraction while in double and then truncate down to floats
                var ta = Avx.ConvertToVector128Single(srcPosA - Avx.Floor(srcPosA));
                var tb = Avx.ConvertToVector128Single(srcPosB - Avx.Floor(srcPosB));
                // Duplicate each element
                var taStereo = Avx2.PermuteVar8x32(ta.ToVector256Unsafe(), permA);
                var tbStereo = Avx2.PermuteVar8x32(tb.ToVector256Unsafe(), permA);
                // Lerp
                // v0 + t * (v1 - v0)
                var res = Fma.MultiplyAdd(taStereo, srcHiA - srcLoA, srcLoA);
                res.StoreUnsafe(ref dstRef, i);
                res = Fma.MultiplyAdd(tbStereo, srcHiB - srcLoB, srcLoB);
                res.StoreUnsafe(ref dstRef, i + (nuint)Vector256<float>.Count);

                srcPosVec += drsPosInc;
            }
            srcPos = srcPosVec[0];
        }

        return (int)i;
    }

    public static void FadeSamplesLinMono(Span<float> src, float startGain, float endGain)
    {
        int len = src.Length;
        int i = 0;
        float rlen = 1f / len;
        if (len < Vector256<float>.Count || !Vector256.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector256<float>.Count;

        var vecB = Vector256.Create(0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f);
        var startGainVec = Vector256.Create(startGain);
        var vecC = Vector256.Create((startGain + endGain) * rlen);
        ref float dst = ref MemoryMarshal.GetReference(src);
        for (; i < len - remaining; i += Vector256<float>.Count)
        {
            var vecA = Vector256.LoadUnsafe(ref dst);
            var frac = startGainVec - (Vector256.Create((float)i) + vecB) * vecC;
            var res = Vector256.Multiply(vecA, frac);
            res.StoreUnsafe(ref dst, unchecked((nuint)i));
        }

    CopyRemaining:
        for (; i < len; i++)
        {
            float frac = startGain - (i * rlen) * (endGain + startGain);
            src[i] *= frac;
        }
    }

    public static void FadeSamplesLinStereo(Span<float> src, float startGain, float endGain)
    {
        int len = src.Length;
        int i = 0;
        float rlen = 1f / len;
        if (len < Vector256<float>.Count || !Vector256.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector256<float>.Count;

        var vecB = Vector256.Create(0f, 0f, 2f, 2f, 4f, 4f, 6f, 6f);
        var startGainVec = Vector256.Create(startGain);
        var vecC = Vector256.Create((startGain + endGain) * rlen);
        ref float dst = ref MemoryMarshal.GetReference(src);
        for (; i < len - remaining; i += Vector256<float>.Count)
        {
            var vecA = Vector256.LoadUnsafe(ref dst);
            var frac = startGainVec - (Vector256.Create((float)i) + vecB) * vecC;
            var res = Vector256.Multiply(vecA, frac);
            res.StoreUnsafe(ref dst, unchecked((nuint)i));
        }

    CopyRemaining:
        for (; i < len; i += 2)
        {
            float frac = startGain - (i * rlen) * (endGain + startGain);
            src[i] *= frac;
            src[i + 1] *= frac;
        }
    }

    public static void FadeSamplesSCurveMono(Span<float> src, float startGain, float endGain)
    {
        int len = src.Length;
        int i = 0;
        float rlen = 1f / len;
        if (len < Vector256<float>.Count || !Vector256.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector256<float>.Count;

        var vecB = Vector256.Create(0f, 1f, 2f, 3f, 4f, 5f, 6f, 7f);
        var startGainVec = Vector256.Create(startGain);
        var vecC = Vector256.Create(startGain + endGain);
        var vecRLen = Vector256.Create(rlen);
        ref float dst = ref MemoryMarshal.GetReference(src);
        for (; i < len - remaining; i += Vector256<float>.Count)
        {
            var vecA = Vector256.LoadUnsafe(ref dst);
            var t = (Vector256.Create((float)i) + vecB) * vecRLen;
            var t2 = t * t;
            var t3 = t2 * t;
            t = -2 * t3 + 3 * t2;
            var frac = startGainVec - t * vecC;
            var res = Vector256.Multiply(vecA, frac);
            res.StoreUnsafe(ref dst, unchecked((nuint)i));
        }

    CopyRemaining:
        for (; i < len; i++)
        {
            float t = (i * rlen);
            float t2 = t * t;
            float t3 = t2 * t;
            t = -2 * t3 + 3 * t2;
            float frac = startGain - t * (endGain + startGain);
            src[i] *= frac;
        }
    }

    public static void FadeSamplesSCurveStereo(Span<float> src, float startGain, float endGain)
    {
        int len = src.Length;
        int i = 0;
        float rlen = 1f / len;
        if (len < Vector256<float>.Count || !Vector256.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector256<float>.Count;

        var vecB = Vector256.Create(0f, 0f, 2f, 2f, 4f, 4f, 6f, 6f);
        var startGainVec = Vector256.Create(startGain);
        var vecC = Vector256.Create(startGain + endGain);
        var vecRLen = Vector256.Create(rlen);
        ref float dst = ref MemoryMarshal.GetReference(src);
        for (; i < len - remaining; i += Vector256<float>.Count)
        {
            var vecA = Vector256.LoadUnsafe(ref dst);
            var t = (Vector256.Create((float)i) + vecB) * vecRLen;
            var t2 = t * t;
            var t3 = t2 * t;
            t = -2 * t3 + 3 * t2;
            var frac = startGainVec - t * vecC;
            var res = Vector256.Multiply(vecA, frac);
            res.StoreUnsafe(ref dst, unchecked((nuint)i));
        }

    CopyRemaining:
        for (; i < len; i += 2)
        {
            float t = (i * rlen);
            float t2 = t * t;
            float t3 = t2 * t;
            t = -2 * t3 + 3 * t2;
            float frac = startGain - t * (endGain + startGain); src[i] *= frac;
            src[i + 1] *= frac;
        }
    }
}
