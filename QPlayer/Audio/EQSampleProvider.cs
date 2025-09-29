using NAudio.Wave;
using QPlayer.Models;
using System;
using System.Collections.Generic;
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

    private readonly float[] prevBuff;

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
        prevBuff = new float[4 * 4 * WaveFormat.Channels];
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
        float a0, a1, a2, b0, b1, b2;

        // See: https://semiwiki.com/semiconductor-services/einfochips/296500-digital-filters-for-audio-equalizer-design/
        // For details on calculating filter coefficients
        float fs = WaveFormat.SampleRate;
        int channels = WaveFormat.Channels;
        float A = MathF.Pow(10, band.gain / 40);
        float w0 = 2 * MathF.PI * band.freq / fs;
        var (sw0, cw0) = MathF.SinCos(w0);
        float alpha = sw0 / (2 * band.q);
        float sqrtA;

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
                sqrtA = MathF.Sqrt(A);
                b0 = A * (A + 1 - (A - 1) * cw0 + 2 * sqrtA * alpha);
                b1 = 2 * A * (A - 1 - (A + 1) * cw0);
                b2 = A * (A + 1 - (A - 1) * cw0 - 2 * sqrtA * alpha);
                a0 = A + 1 + (A - 1) * cw0 + 2 * sqrtA * alpha;
                a1 = -2 * (A - 1 + (A + 1) * cw0);
                a2 = A + 1 + (A - 1) * cw0 - 2 * sqrtA * alpha;
                break;
            case EQBandShape.HighShelf:
                sqrtA = MathF.Sqrt(A);
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
            default:
                a0 = b0 = 1;
                a1 = a2 = b1 = b2 = 0;
                break;
        }

        // Normalize coeffs
        float inva = 1 / a0;
        a1 *= inva;
        a2 *= inva;
        b0 *= inva;
        b1 *= inva;
        b2 *= inva;

        if (Vector128.IsHardwareAccelerated && Fma.IsSupported)
        {
            if (channels == 2)
                ApplyFilterVecStereo(prevBuff, buffer, offset, count, ind, a1, a2, b0, b1, b2);
            else
                ApplyFilterVec(prevBuff, buffer, offset, count, channels, ind, a1, a2, b0, b1, b2);
        }
        else
            ApplyFilterScalar(prevBuff, buffer, offset, count, channels, ind, a1, a2, b0, b1, b2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ApplyFilterScalar(float[] prevBuf, float[] buffer, int offset, int count, int channels, int ind, float a1, float a2, float b0, float b1, float b2)
    {
        float x0, x1, x2, y1, y2;
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

            for (int i = offset + c; i < count; i += channels)
            {
                x0 = buffer[i];

                // Direct form I of a simple Bi-Quad filter
                float res = b0 * x0 + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

                buffer[i] = res;

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

    private static void ApplyFilterVecMC(float[] prevBuf, float[] buffer, int offset, int count, int channels, int ind, float a1, float a2, float b0, float b1, float b2)
    {
        // TODO: Finish implementing proper n-channel filter vectorisation...
        // Currently the masked writes are proving fiddly.
        // https://shafq.at/vectorizing-iir-filters.html
        Vector128<float> x0, x1, x2, y1, y2;
        var va1 = Vector128.Create(-a1);
        var va2 = Vector128.Create(-a2);
        var vb0 = Vector128.Create(b0);
        var vb1 = Vector128.Create(b1);
        var vb2 = Vector128.Create(b2);
        ind *= 4 * channels;
        ref var prevRef = ref prevBuf[ind];
        int inc = Vector128<float>.Count;
        for (int c = 0; c < channels; c += Vector128<float>.Count)
        {
            // Get the previous computed values from the buffer.
            x1 = Vector128.LoadUnsafe(in prevRef);
            prevRef = ref Unsafe.Add(ref prevRef, inc);
            x2 = Vector128.LoadUnsafe(in prevRef);
            prevRef = ref Unsafe.Add(ref prevRef, inc);
            y1 = Vector128.LoadUnsafe(in prevRef);
            prevRef = ref Unsafe.Add(ref prevRef, inc);
            y2 = Vector128.LoadUnsafe(in prevRef);
            prevRef = ref Unsafe.Add(ref prevRef, inc);

            for (int i = offset + c; i < count; i += inc)
            {
                x0 = Vector128.LoadUnsafe(in buffer[i]);

                // Direct form I of a simple Bi-Quad filter
                var bx0 = Sse.Multiply(vb0, x0);
                var v1 = Fma.MultiplyAdd(vb1, x1, bx0);
                var v2 = Fma.MultiplyAdd(vb2, x2, v1);
                var v3 = Fma.MultiplyAdd(va1, y1, v2);
                var res = Fma.MultiplyAdd(va2, y2, v3);

                switch (inc)
                {
                    case 1:
                        buffer[i] = res[0];
                        break;
                    case 2:
                        res.GetLower().StoreUnsafe(ref buffer[i]);
                        break;
                    case 3:
                        res.GetLower().StoreUnsafe(ref buffer[i]);
                        buffer[i + 2] = res[2];
                        break;
                    default:
                        res.StoreUnsafe(ref buffer[i]);
                        break;
                }

                y2 = y1;
                y1 = res;
                x2 = x1;
                x1 = x0;
            }

            // Store the last samples in the buffer for the next chunk
            prevRef = ref Unsafe.Subtract(ref prevRef, 4 * inc);
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

    public static void ApplyFilterVecStereo(float[] prevBuf, float[] buffer, int offset, int count, int ind, float a1, float a2, float b0, float b1, float b2)
    {
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

        for (int i = offset; i < count; i += 2)
        {
            // This will read and process more samples than needed, I imagine this is fine...
            var x0 = Vector128.LoadUnsafe(in buffer[i]);

            // Direct form I of a simple Bi-Quad filter
            var bx0 = Sse.Multiply(vb0, x0);
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
                Sse.StoreLow((float*)Unsafe.AsPointer(ref buffer[i]), res);
            }

            y2 = y1;
            y1 = res;
            x2 = x1;
            x1 = x0;
        }

        // Store the last samples in the buffer for the next chunk
        x1.GetLower().StoreUnsafe(ref prevRef);
        x2.GetLower().StoreUnsafe(ref prevRef, 2);
        y1.GetLower().StoreUnsafe(ref prevRef, 4);
        y2.GetLower().StoreUnsafe(ref prevRef, 6);
    }

    public static void ApplyFilterVec(float[] prevBuf, float[] buffer, int offset, int count, int channels, int ind, float a1, float a2, float b0, float b1, float b2)
    {
        // https://shafq.at/vectorizing-iir-filters.html
        Vector128<float> x0, x1, x2, y1, y2;
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

            for (int i = offset + c; i < count; i += channels)
            {
                x0 = Vector128.CreateScalar(buffer[i]);

                // Direct form I of a simple Bi-Quad filter
                var bx0 = Sse.MultiplyScalar(vb0, x0);
                var v1 = Fma.MultiplyAddScalar(vb1, x1, bx0);
                var v2 = Fma.MultiplyAddScalar(vb2, x2, v1);
                var v3 = Fma.MultiplyAddScalar(va1, y1, v2);
                var res = Fma.MultiplyAddScalar(va2, y2, v3);

                buffer[i] = res[0];

                y2 = y1;
                y1 = res;
                x2 = x1;
                x1 = x0;
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
}
