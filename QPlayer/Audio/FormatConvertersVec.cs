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

        fixed (byte* srcBytes = &sourceBuffer[0])
        {
            var srcPtr = (sbyte*)srcBytes;
            nuint i = (nuint)offset;
            nuint end = i + (nuint)read;

            if (read > 16 && Avx2.IsSupported)
            {
                ref var dstRef = ref MemoryMarshal.GetReference(buffer);
                for (; i < end - (nuint)(Vector256<float>.Count - 1); i += (nuint)Vector256<float>.Count)
                {
                    var vi = Avx2.ConvertToVector256Int32(srcPtr);
                    var vf = Avx.ConvertToVector256Single(vi) * (1 / 128f);
                    vf -= Vector256<float>.One;
                    vf.StoreUnsafe(ref dstRef, i);
                    srcPtr += Vector256<float>.Count;
                }
            }

            for (; i < end; i++)
            {
                buffer[i] = *srcPtr * (1 / 128f) - 1;
                srcPtr++;
            }
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

        fixed (byte* srcBytes = &sourceBuffer[0])
        {
            var srcPtr = (short*)srcBytes;
            nuint i = (nuint)offset;
            nuint end = i + (nuint)read;

            if (read > 16 && Avx2.IsSupported)
            {
                ref var dstRef = ref MemoryMarshal.GetReference(buffer);
                for (; i < end - (nuint)(Vector256<float>.Count - 1); i += (nuint)Vector256<float>.Count)
                {
                    var vi = Avx2.ConvertToVector256Int32(srcPtr);
                    var vf = Avx.ConvertToVector256Single(vi) * (1 / 32768f);
                    vf.StoreUnsafe(ref dstRef, i);
                    srcPtr += Vector256<float>.Count;
                }
            }

            for (; i < end; i++)
            {
                buffer[i] = *srcPtr * (1 / 32768f);
                srcPtr++;
            }
        }

        return read;
    }
}

public class Pcm24BitToSampleProviderVec(IWaveProvider source) : SampleProviderConverterBase(source)
{
    public override unsafe int Read(float[] buffer, int offset, int count)
    {
        EnsureSourceBuffer(count * 3);
        int read = source.Read(sourceBuffer, 0, count * 3) / 3;

        fixed (byte* srcBytes = &sourceBuffer[0])
        {
            var srcPtr = srcBytes;
            nint i = offset;
            nint end = offset + read;

            for (; i < end; i++)
            {
                int x = *srcPtr;
                x <<= 8;
                buffer[i] = x * 4.65661287e-10f; // 1/2^31
                srcPtr += 3;
            }
        }

        return read;
    }
}

public class Pcm32BitToSampleProviderVec(IWaveProvider source) : SampleProviderConverterBase(source)
{
    public override unsafe int Read(float[] buffer, int offset, int count)
    {
        EnsureSourceBuffer(count * 4);
        int read = source.Read(sourceBuffer, 0, count * 4) / 4;

        fixed (byte* srcBytes = &sourceBuffer[0])
        {
            var srcPtr = (int*)srcBytes;
            nuint i = (nuint)offset;
            nuint end = i + (nuint)read;

            if (read > 16 && Avx2.IsSupported)
            {
                ref var dstRef = ref MemoryMarshal.GetReference(buffer);
                for (; i < end - (nuint)(Vector256<float>.Count - 1); i += (nuint)Vector256<float>.Count)
                {
                    var vi = Avx.LoadVector256(srcPtr);
                    var vf = Avx.ConvertToVector256Single(vi) * 4.65661287e-10f; // 1/2^31
                    vf.StoreUnsafe(ref dstRef, (nuint)i);
                    srcPtr += Vector256<float>.Count;
                }
            }

            for (; i < end; i++)
            {
                buffer[i] = *srcPtr * 4.65661287e-10f; // 1/2^31
                srcPtr++;
            }
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

        var srcSpan = MemoryMarshal.Cast<byte, float>(sourceBuffer)[..read];
        srcSpan.CopyTo(buffer.AsSpan(offset, count));

        return read;
    }
}
