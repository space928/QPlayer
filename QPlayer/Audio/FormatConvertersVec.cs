using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace QPlayer.Audio;

public class Pcm8BitToSampleProviderVec(IWaveProvider source) : SampleProviderConverterBase(source)
{
    public override unsafe int Read(float[] buffer, int offset, int count)
    {
        EnsureSourceBuffer(count);
        int read = source.Read(sourceBuffer, 0, count);

        var srcSpan = MemoryMarshal.Cast<byte, sbyte>(sourceBuffer);
        int j = 0;
        int i = offset;
        int end = offset + read;

        if (read > 16 && Avx2.IsSupported)
        {
            ref var srcRef = ref MemoryMarshal.GetReference(srcSpan);
            ref var dstRef = ref MemoryMarshal.GetReference(buffer);
            for (; i < end - (Vector256<float>.Count - 1); i += Vector256<float>.Count)
            {
                var vi = Avx2.ConvertToVector256Int32((byte*)Unsafe.AsPointer(ref Unsafe.Add(ref srcRef, j)));
                var vf = Avx.ConvertToVector256Single(vi);
                vf.StoreUnsafe(ref dstRef, (nuint)i);
                j += Vector256<float>.Count;
            }
        }

        for (; i < offset + read; i++)
        {
            buffer[i] = srcSpan[j] / 128f;
            j++;
        }

        return read;
    }
}

public class Pcm16BitToSampleProviderVec(IWaveProvider source) : SampleProviderConverterBase(source)
{
    public override unsafe int Read(float[] buffer, int offset, int count)
    {
        EnsureSourceBuffer(count << 1);
        int read = source.Read(sourceBuffer, 0, count << 1) >> 1;

        var srcSpan = MemoryMarshal.Cast<byte, short>(sourceBuffer);
        int j = 0;
        int i = offset;
        int end = offset + read;

        if (read > 16 && Avx2.IsSupported)
        {
            ref var srcRef = ref MemoryMarshal.GetReference(srcSpan);
            ref var dstRef = ref MemoryMarshal.GetReference(buffer);
            for (; i < end - (Vector256<float>.Count - 1); i += Vector256<float>.Count)
            {
                var vi = Avx2.ConvertToVector256Int32((short*)Unsafe.AsPointer(ref Unsafe.Add(ref srcRef, j)));
                var vf = Avx.ConvertToVector256Single(vi) * (1 / 32768f);
                vf.StoreUnsafe(ref dstRef, (nuint)i);
                j += Vector256<float>.Count;
            }
        }

        for (; i < end; i++)
        {
            buffer[i] = srcSpan[j] / 32768f;
            j++;
        }

        return read;
    }
}

public class Pcm24BitToSampleProviderVec(IWaveProvider source) : SampleProviderConverterBase(source)
{
    public override int Read(float[] buffer, int offset, int count)
    {
        EnsureSourceBuffer(count * 3);
        int read = source.Read(sourceBuffer, 0, count * 3) / 3;

        ref var srcRef = ref MemoryMarshal.GetReference(sourceBuffer);
        int j = 0;
        for (int i = offset; i < offset + read; i++)
        {
            buffer[i] = (Unsafe.ReadUnaligned<int>(in Unsafe.Add(ref srcRef, j)) & 0xffffff) / 8388608f;
            j += 3;
        }

        return read;
    }
}

public class Pcm32BitToSampleProviderVec(IWaveProvider source) : SampleProviderConverterBase(source)
{
    public override int Read(float[] buffer, int offset, int count)
    {
        EnsureSourceBuffer(count * 4);
        int read = source.Read(sourceBuffer, 0, count * 4) / 4;

        var srcSpan = MemoryMarshal.Cast<byte, int>(sourceBuffer);
        int j = 0;
        int i = offset;
        int end = offset + read;

        if (read > 16 && Avx2.IsSupported)
        {
            ref var srcRef = ref MemoryMarshal.GetReference(srcSpan);
            ref var dstRef = ref MemoryMarshal.GetReference(buffer);
            for (; i < end - (Vector256<float>.Count - 1); i += Vector256<float>.Count)
            {
                var vi = Vector256.LoadUnsafe(in srcRef, (nuint)j);
                var vf = Avx.ConvertToVector256Single(vi) * (1 / 2.14748365E+09f);
                vf.StoreUnsafe(ref dstRef, (nuint)i);
                j += Vector256<int>.Count;
            }
        }

        for (; i < offset + read; i++)
        {
            buffer[i] = srcSpan[j] / 2.14748365E+09f;
            j++;
        }

        return read;
    }
}

public class FloatToSampleProviderVec(IWaveProvider source) : SampleProviderConverterBase(source)
{
    public override int Read(float[] buffer, int offset, int count)
    {
        EnsureSourceBuffer(count * 4);
        int read = source.Read(sourceBuffer, 0, count * 4) / 4;

        var srcSpan = MemoryMarshal.Cast<byte, float>(sourceBuffer);
        srcSpan.CopyTo(buffer.AsSpan(offset, count));

        return read;
    }
}
