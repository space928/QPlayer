using NAudio.Wave;
using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Audio;

public class EQSampleProvider : ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private readonly object lockObj = new();

    private readonly double[] prevBuff;
    private readonly FilterCoeffs[] coeffsCache;

    public EQSettings? eq;

    public WaveFormat WaveFormat => source.WaveFormat;

    public long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    //public EQBand Band1 { get => band1; set => band1 = value; }
    //public EQBand Band2 { get => band2; set => band2 = value; }
    //public EQBand Band3 { get => band3; set => band3 = value; }
    //public EQBand Band4 { get => band4; set => band4 = value; }

    public EQSampleProvider(ISamplePositionProvider source)
    {
        this.source = source;
        // Make sure the sample buffer is big enough to fit the samples for each band (4 per band)
        prevBuff = new double[4 * 4 * WaveFormat.Channels];
        coeffsCache = new FilterCoeffs[4];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        count = source.Read(buffer, offset, count);

        if (eq == null || !eq.enabled)
            return count;

        // Apply each band one after the other.
        ApplyEQ(buffer, offset, count, 0, eq.band1);
        ApplyEQ(buffer, offset, count, 1, eq.band2);
        ApplyEQ(buffer, offset, count, 2, eq.band3);
        ApplyEQ(buffer, offset, count, 3, eq.band4);

        return count;
    }

    private void ApplyEQ(float[] buffer, int offset, int count, int ind, EQBand band)
    {
        if (band.freq < 5)
            return;

        // The biquad filter terms, normalized such that a0 is 1
        ref var coeffs = ref coeffsCache[ind];
        if (coeffs.band != band)
            coeffs = CalculateFilterCoefficients(band, WaveFormat.SampleRate);
        int channels = WaveFormat.Channels;

        ApplyFilter(prevBuff, ind, channels, coeffs, buffer.AsSpan(offset, count));
    }

    public static FilterCoeffs CalculateFilterCoefficients(in EQBand band, int fs)
    {
        double a0, a1, a2, b0, b1, b2;
        // See: https://semiwiki.com/semiconductor-services/einfochips/296500-digital-filters-for-audio-equalizer-design/
        // For details on calculating filter coefficients
        double A = MathF.Pow(10, band.gain / 40);
        double w0 = 2 * Math.PI * band.freq / fs;
        var (sw0, cw0) = Math.SinCos(w0);
        double alpha = sw0 / (2 * band.q);
        double sqrtA;

        switch (band.shape)
        {
            case EQBandShape.Bell:
                b0 = 1 + alpha * A;
                b1 = -2 * cw0;
                b2 = 1 - alpha * A;
                a0 = 1 + alpha / A;
                a1 = b1;
                a2 = 1 - alpha / A;
                break;
            case EQBandShape.LowShelf:
                sqrtA = Math.Sqrt(A);
                b0 = A * (A + 1 - (A - 1) * cw0 + 2 * sqrtA * alpha);
                b1 = 2 * A * (A - 1 - (A + 1) * cw0);
                b2 = A * (A + 1 - (A - 1) * cw0 - 2 * sqrtA * alpha);
                a0 = A + 1 + (A - 1) * cw0 + 2 * sqrtA * alpha;
                a1 = -2 * (A - 1 + (A + 1) * cw0);
                a2 = A + 1 + (A - 1) * cw0 - 2 * sqrtA * alpha;
                break;
            case EQBandShape.HighShelf:
                sqrtA = Math.Sqrt(A);
                b0 = A * (A + 1 + (A - 1) * cw0 + 2 * sqrtA * alpha);
                b1 = -2 * A * (A - 1 + (A + 1) * cw0);
                b2 = A * (A + 1 + (A - 1) * cw0 - 2 * sqrtA * alpha);
                a0 = A + 1 - (A - 1) * cw0 + 2 * sqrtA * alpha;
                a1 = 2 * (A - 1 - (A + 1) * cw0);
                a2 = A + 1 - (A - 1) * cw0 - 2 * sqrtA * alpha;
                break;
            case EQBandShape.Notch:
                b0 = 1;
                b1 = -2 * cw0;
                b2 = 1;
                a0 = 1 + alpha;
                a1 = b1;
                a2 = 1 - alpha;
                break;
            case EQBandShape.LowPass:
                b0 = (1 - cw0) / 2;
                b1 = 1 - cw0;
                b2 = b0;
                a0 = 1 + alpha;
                a1 = -2 * cw0;
                a2 = 1 - alpha;
                break;
            case EQBandShape.HighPass:
                b0 = (1 + cw0) / 2;
                b1 = -(1 + cw0);
                b2 = b0;
                a0 = 1 + alpha;
                a1 = -2 * cw0;
                a2 = 1 - alpha;
                break;
            case EQBandShape.AllPass:
                b0 = 1 - alpha;
                b1 = -2 * cw0;
                b2 = 1 + alpha;
                a0 = b2;
                a1 = b1;
                a2 = b0;
                break;
            default:
                a0 = b0 = 1;
                a1 = a2 = b1 = b2 = 0;
                break;
        }

        // Normalize coeffs
        double inva = 1 / a0;
        a1 *= inva;
        a2 *= inva;
        b0 *= inva;
        b1 *= inva;
        b2 *= inva;

        FilterCoeffs coeffs;
        coeffs.a1 = a1;
        coeffs.a2 = a2;
        coeffs.b0 = b0;
        coeffs.b1 = b1;
        coeffs.b2 = b2;
        coeffs.band = band;
        return coeffs;
    }

    /// <summary>
    /// Applies a biquad filter to the input signal. Automatically picks a vectorised implementation if available.
    /// </summary>
    /// <param name="hist">The history buffer, must be at least 4*<paramref name="channels"/> long.</param>
    /// <param name="ind">The index into the history buffer to use (this is internally multiplied by 
    /// the number of history samples the filter uses (ie: 4*<paramref name="channels"/>)).</param>
    /// <param name="channels">The number of interleaved channels in the input buffer.</param>
    /// <param name="filter">The filter coefficients to apply to the signal.</param>
    /// <param name="buffer">The signal to filter.</param>
    public static void ApplyFilter(double[] hist, int ind, int channels, FilterCoeffs filter, Span<float> buffer)
    {
        if (Vector128.IsHardwareAccelerated && Fma.IsSupported)
        {
            if (channels == 2)
                ApplyFilterVecStereo(hist, buffer, ind, filter.a1, filter.a2, filter.b0, filter.b1, filter.b2);
            else
                ApplyFilterVec(hist, buffer, channels, ind, filter.a1, filter.a2, filter.b0, filter.b1, filter.b2);
        }
        else
            ApplyFilterScalar(hist, buffer, channels, ind, filter.a1, filter.a2, filter.b0, filter.b1, filter.b2);
    }

    /// <summary>
    /// Applies a biquad filter to the input signal.
    /// </summary>
    /// <param name="prevBuf">The history buffer, must be at least 4*<paramref name="channels"/> long.</param>
    /// <param name="buffer">The sample buffer to process.</param>
    /// <param name="channels">The number of interleaved channels in the input buffer.</param>
    /// <param name="ind">The index into the history buffer to use (this is internally multiplied by 
    /// the number of history samples the filter uses (ie: 4*<paramref name="channels"/>)).</param>
    /// <param name="a1"></param>
    /// <param name="a2"></param>
    /// <param name="b0"></param>
    /// <param name="b1"></param>
    /// <param name="b2"></param>
    public static void ApplyFilterScalar(double[] prevBuf, Span<float> buffer, int channels, int ind,
        double a1, double a2, double b0, double b1, double b2)
    {
        double x0, x1, x2, y1, y2;
        ind *= 4 * channels;
        ref var prevRef = ref prevBuf[ind];
        for (int c = 0; c < channels; c++)
        {
            // Get the previous computed values from the buffer.
            x1 = prevRef;
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            x2 = prevRef;
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            y1 = prevRef;
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            y2 = prevRef;
            //x1 = y1 = x2 = y2 = buffer[offset + c];

            for (int i = c; i < buffer.Length; i += channels)
            {
                x0 = buffer[i];

                // Direct form I of a simple Bi-Quad filter
                double res = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
                if (!double.IsNormal(res))
                    res = 0;

                buffer[i] = (float)res;

                y2 = y1;
                y1 = res;
                x2 = x1;
                x1 = x0;
            }

            // Store the last samples in the buffer for the next chunk
            prevRef = ref Unsafe.Subtract(ref prevRef, 3);
            prevRef = x1;
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            prevRef = x2;
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            prevRef = y1;
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            prevRef = y2;
            prevRef = ref Unsafe.Add(ref prevRef, 1);

            ind += 4;
        }
    }

    /// <summary>
    /// Applies a biquad filter to the input signal.
    /// </summary>
    /// <param name="prevBuf">The history buffer, must be at least 4*<paramref name="channels"/> long.</param>
    /// <param name="buffer">The sample buffer to process.</param>
    /// <param name="channels">The number of interleaved channels in the input buffer.</param>
    /// <param name="ind">The index into the history buffer to use (this is internally multiplied by 
    /// the number of history samples the filter uses (ie: 4*<paramref name="channels"/>)).</param>
    /// <param name="a1"></param>
    /// <param name="a2"></param>
    /// <param name="b0"></param>
    /// <param name="b1"></param>
    /// <param name="b2"></param>
    public static void ApplyFilterVecStereo(double[] prevBuf, Span<float> buffer, int ind,
        double a1, double a2, double b0, double b1, double b2)
    {
        if (!Fma.IsSupported)
            throw new NotImplementedException();
        if ((buffer.Length & 1) != 0)
            throw new ArgumentOutOfRangeException(nameof(buffer), "Buffer must have an even length!");
        // https://shafq.at/vectorizing-iir-filters.html
        var va1 = Vector128.Create(-a1);
        var va2 = Vector128.Create(-a2);
        var vb0 = Vector128.Create(b0);
        var vb1 = Vector128.Create(b1);
        var vb2 = Vector128.Create(b2);
        ref var prevRef = ref Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(prevBuf), ind * 4 * 2);

        // Get the previous computed values from the buffer.
        var x1 = Vector128.LoadUnsafe(in prevRef);
        var x2 = Vector128.LoadUnsafe(in prevRef, 2);
        var y1 = Vector128.LoadUnsafe(in prevRef, 4);
        var y2 = Vector128.LoadUnsafe(in prevRef, 6);

        for (int i = 0; i < buffer.Length; i += 2)
        {
            // This will read and process more samples than needed, I imagine this is fine...
            var x0 = Sse2.ConvertToVector128Double(Vector128.LoadUnsafe(in buffer[i]));

            // Direct form I of a simple Bi-Quad filter
            var bx0 = Sse2.Multiply(vb0, x0);
            var v1 = Fma.MultiplyAdd(vb1, x1, bx0);
            var v2 = Fma.MultiplyAdd(vb2, x2, v1);
            var v3 = Fma.MultiplyAdd(va1, y1, v2);
            var res = Fma.MultiplyAdd(va2, y2, v3);

            // res.GetLower().StoreUnsafe(ref buffer[i]);
            // The above should work, but for now it generates pointless stores to the stack, so use the sse instruction.
            unsafe
            {
                // Something, something, I should pin the buffer before getting a pointer to it to protect it from the GC...
                // buuuut, the Vector128.LoadUnsafe JITs to `vmovups  xmm5, xmmword ptr [r8+4*rdx+0x10]` and doesn't keep
                // any of those pointers on the stack during the loop, so I think I'm safe to do the same thing...
                // I just wanted a nice vmovlps xmmword  ptr [r8+4*rdx+0x10], xmm16
                var resFloat = Sse2.ConvertToVector128Single(res);
                Sse.StoreLow((float*)Unsafe.AsPointer(ref buffer[i]), resFloat);
            }

            y2 = y1;
            y1 = res;
            x2 = x1;
            x1 = x0;
        }

        // Store the last samples in the buffer for the next chunk
        x1.StoreUnsafe(ref prevRef);
        x2.StoreUnsafe(ref prevRef, 2);
        y1.StoreUnsafe(ref prevRef, 4);
        y2.StoreUnsafe(ref prevRef, 6);
    }

    /// <summary>
    /// Applies a biquad filter to the strided input buffer. Requires FMA instructions.
    /// </summary>
    /// <param name="prevBuf">The history buffer used by the filter, must be at least 4 * channels long.</param>
    /// <param name="buffer">The buffer to filter.</param>
    /// <param name="channels">The number of interleaved channels in the signal to filter (ie: buffer = [LRLRLRLR..], channels = 2)</param>
    /// <param name="ind">The index into the history buffer to use, when chaining multiple filters 
    /// (this is the filter index, not the buffer index, so for the second iteration of filtering an index of 1 would be used).</param>
    /// <param name="a1"></param>
    /// <param name="a2"></param>
    /// <param name="b0"></param>
    /// <param name="b1"></param>
    /// <param name="b2"></param>
    public static void ApplyFilterVec(double[] prevBuf, Span<float> buffer, int channels, int ind,
        double a1, double a2,
        double b0, double b1, double b2)
    {
        if (!Fma.IsSupported)
            throw new NotImplementedException();
        // https://shafq.at/vectorizing-iir-filters.html
        Vector128<double> x0, x1, x2, y1, y2;
        var clipScale = Vector128.CreateScalar(0.1f);
        var va1 = Vector128.CreateScalar(-a1);
        var va2 = Vector128.CreateScalar(-a2);
        var vb0 = Vector128.CreateScalar(b0);
        var vb1 = Vector128.CreateScalar(b1);
        var vb2 = Vector128.CreateScalar(b2);
        ind *= 4 * channels;
        ref var prevRef = ref prevBuf[ind];
        for (int c = 0; c < channels; c++)
        {
            // Get the previous computed values from the buffer.
            x1 = Vector128.CreateScalar(prevRef);
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            x2 = Vector128.CreateScalar(prevRef);
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            y1 = Vector128.CreateScalar(prevRef);
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            y2 = Vector128.CreateScalar(prevRef);
            //x1 = y1 = x2 = y2 = buffer[offset + c];

            for (int i = c; i < buffer.Length; i += channels)
            {
                x0 = Vector128.CreateScalar((double)buffer[i]);

                // Direct form I of a simple Bi-Quad filter
                var bx0 = Sse2.MultiplyScalar(vb0, x0);
                var v1 = Fma.MultiplyAddScalar(vb1, x1, bx0);
                var v2 = Fma.MultiplyAddScalar(vb2, x2, v1);
                var v3 = Fma.MultiplyAddScalar(va1, y1, v2);
                var res = Fma.MultiplyAddScalar(va2, y2, v3);

                buffer[i] = (float)res[0];

                y2 = y1;
                y1 = res;
                x2 = x1;
                x1 = x0;

                // Non-linear soft clipping to prevent instability/resonance
                // x / ((1 + 0.1 * x^4) ^ 1/4)
                // y1 = Sse.SqrtScalar(Sse.ReciprocalSqrtScalar(Vector128<float>.One + clipScale * y1 * y1 * y1 * y1));
                // if (Math.Abs(y1[0]) > 3 || double.IsInfinity(y2[0]) || double.IsNaN(y1[0] + y2[0]))
                //     Debugger.Break();
            }

            // Store the last samples in the buffer for the next chunk
            prevRef = ref Unsafe.Subtract(ref prevRef, 3);
            prevRef = x1[0];
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            prevRef = x2[0];
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            prevRef = y1[0];
            prevRef = ref Unsafe.Add(ref prevRef, 1);
            prevRef = y2[0];
            prevRef = ref Unsafe.Add(ref prevRef, 1);

            ind += 4;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FilterCoeffs
    {
        public EQBand band;
        public double a1, a2, b0, b1, b2;
    }
}
