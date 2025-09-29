using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Audio;

public class MonoToStereoSampleProviderVec : ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private readonly WaveFormat waveFormat;
    private float[] sourceBuff;

    public long Position { get => source.Position << 1; set => source.Position = value >> 1; }

    public WaveFormat WaveFormat => waveFormat;

    public MonoToStereoSampleProviderVec(ISamplePositionProvider source)
    {
        this.source = source;
        waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        sourceBuff = [];
    }

    public int Read(float[] buffer, int offset, int count)
    {
        var srcBuff = sourceBuff;
        int srcCount = count >> 1;
        if (srcBuff.Length < srcCount)
            sourceBuff = srcBuff = new float[srcCount];

        int read = source.Read(srcBuff, 0, srcCount);
        if (read == 0)
            return 0;

        int i = 0;
        // Use refs to avoid bounds checking
        ref var dst = ref buffer[offset];
        ref var src = ref srcBuff[i];
        if (Avx2.IsSupported)
        {
            // Vectorised path, loads 8 samples into an xmm register, then uses the unpack instructions to duplicate each float
            for (; i <= read - Vector256<float>.Count; i += Vector256<float>.Count)
            {
                var srcVec = Vector256.LoadUnsafe<float>(ref src);
                // [1,2,3,4,5,6,7,8] => [1,1,2,2,3,3,4,4]
                var a = Avx.UnpackLow(srcVec, srcVec);
                // [1,2,3,4,5,6,7,8] => [5,5,6,6,7,7,8,8]
                var b = Avx.UnpackHigh(srcVec, srcVec);
                a.StoreUnsafe(ref dst);
                dst = ref Unsafe.Add(ref dst, Vector256<float>.Count);
                b.StoreUnsafe(ref dst);
                dst = ref Unsafe.Add(ref dst, Vector256<float>.Count);
                src = ref Unsafe.Add(ref src, Vector256<float>.Count);
            }
        }
        for (; i < read; i++)
        {
            var srcVal = src;
            dst = srcVal;
            dst = ref Unsafe.Add(ref dst, 1);
            dst = srcVal;
            dst = ref Unsafe.Add(ref dst, 1);
            src = ref Unsafe.Add(ref src, 1);
        }

        return read << 1;
    }
}
