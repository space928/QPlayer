using NAudio.Wave;
using NAudio.Wave.SampleProviders;
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

public class MeteringSampleProviderVec : ISampleProvider
{
    private ISampleProvider source;
    private readonly float[] maxSamples;
    private readonly float[] rmsSamples;
    private readonly int channels;

    private int sampleCount;

    /// <summary>
    /// The number of samples to read between each metering event.
    /// </summary>
    public int SamplesPerNotification { get; set; }

    public WaveFormat WaveFormat => source.WaveFormat;

    public ISampleProvider Source
    {
        get => source;
        set => source = value;
    }

    /// <summary>
    /// An event raised every <see cref="SamplesPerNotification"/> samples with metering information.
    /// </summary>
    public event Action<MeteringEvent>? OnMeter;

    /// <summary>
    /// 
    /// </summary>
    /// <param name="source"></param>
    public MeteringSampleProviderVec(ISampleProvider source) : this(source, source.WaveFormat.SampleRate / 10)
    {
    }

    public MeteringSampleProviderVec(ISampleProvider source, int samplesPerNotification)
    {
        this.source = source;
        channels = source.WaveFormat.Channels;
        maxSamples = new float[channels];
        rmsSamples = new float[channels];
        SamplesPerNotification = samplesPerNotification;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int num = source.Read(buffer, offset, count);
        while (num < count)
            num += source.Read(buffer, offset + num, count - num);
        if (OnMeter == null)
            return num;

        int i = 0;
        if (channels == 2)
            i += ReadStereo(buffer, offset, num);

        for (; i < num; i += channels)
        {
            for (int j = 0; j < channels; j++)
            {
                float val = Math.Abs(buffer[offset + i + j]);
                maxSamples[j] = Math.Max(maxSamples[j], val);
                rmsSamples[j] += val * val;
            }

            sampleCount++;
            if (sampleCount >= SamplesPerNotification)
            {
                OnMeter(new()
                {
                    samplesMeasured = sampleCount,
                    peakL = maxSamples[0],
                    peakR = maxSamples[1],
                    rmsL = MathF.Sqrt(rmsSamples[0] / sampleCount),
                    rmsR = MathF.Sqrt(rmsSamples[1] / sampleCount)
                });
                sampleCount = 0;
                maxSamples.AsSpan().Clear();
                rmsSamples.AsSpan().Clear();
            }
        }

        return num;
    }

    private int ReadStereo(float[] buffer, int offset, int count)
    {
        if (!Vector256.IsHardwareAccelerated || count < Vector256<float>.Count * 2)
            return 0;

        ref var buffRef = ref buffer[offset];
        var maxSample = Vector64.LoadUnsafe(ref maxSamples[0]).ToVector128();
        var rmsSample = Vector64.LoadUnsafe(ref rmsSamples[0]).ToVector128();
        var _sampleCount = sampleCount;
        var _samplesPerfNotif = SamplesPerNotification;
        var permImm = Vector256.Create(4, 5, 6, 7, 0, 0, 0, 0);
        nuint i = 0;
        nuint maxElem = (nuint)Math.Max(0, count - Vector256<float>.Count);
        for (; i < maxElem; i += (nuint)Vector256<float>.Count)
        {
            /*
             * We need to compute the sum and max of each left and right sample.
             * These samples are interleaved LRLR... we do a bunch of shuffles and permutes
             * to line up each sample within the vector so we can add/max vertically in as
             * few steps as possible.
             * Note that we choose to use a vertical add as opposed to a horizontal add
             * and prefer shuffle to permute when possible for increased uop throughput.
             * 
             *  p0 = [1,2,3,4,5,6,7,8] [LRLRLRLRLR]
             *  p1 = perm [5,6,7,8,x,x,x,x]
             *  p2 = vec + p1 => [15,26,37,48,..,..,..,..]
             *  p3 = shuf [37,48,..,..,..,..,..,..,..]   imm = [2 + 3 << 2]
             *  p4 = p2 + p3
             *  L = p4[0]; R = p4[1]
             */
            var vec = Vector256.LoadUnsafe(ref buffRef, i);

            var vecAbs = Vector256.Abs(vec);
            var max = Avx.Max(vecAbs, Avx2.PermuteVar8x32(vecAbs, permImm)).GetLower();
            max = Sse.Max(max, Sse2.Shuffle(max.AsInt32(), (byte)(2 + (3 << 2))).AsSingle());
            maxSample = Sse.Max(maxSample, max);

            var vecSqr = vec * vec;
            var rms = (vecSqr + Avx2.PermuteVar8x32(vecSqr, permImm)).GetLower();
            rms += Sse2.Shuffle(rms.AsInt32(), (byte)(2 + (3 << 2))).AsSingle();
            rmsSample += rms;

            _sampleCount += Vector256<float>.Count / 2;
            if (_sampleCount > _samplesPerfNotif)
                NotifySample(ref maxSample, ref rmsSample, ref _sampleCount);
        }

        // Write our locals back out to the instance fields
        sampleCount = _sampleCount;
        maxSample.GetLower().StoreUnsafe(ref maxSamples[0]);
        rmsSample.GetLower().StoreUnsafe(ref rmsSamples[0]);

        return (int)i;

        void NotifySample(ref Vector128<float> maxSample, ref Vector128<float> rmsSample, ref int _sampleCount)
        {
            var samplesRecip = Vector128.Create(1f / _sampleCount);
            rmsSample = Sse.Sqrt(Sse.Multiply(rmsSample, samplesRecip));

            OnMeter!(new()
            {
                samplesMeasured = sampleCount,
                peakL = maxSample[0],
                peakR = maxSample[1],
                rmsL = rmsSample[0],
                rmsR = rmsSample[1]
            });
            _sampleCount = 0;
            maxSample = default;
            rmsSample = default;
        }
    }

    public static void ComputePeakRMSStereo(ReadOnlySpan<float> buffer, out Vector64<float> max, out Vector64<float> rms)
    {
        ref var buffRef = ref MemoryMarshal.GetReference(buffer);
        var maxSample = Vector128<float>.Zero;
        var rmsSample = Vector128<float>.Zero;
        nuint i = 0;
        nuint maxElem = (nuint)Math.Max(0, buffer.Length - Vector256<float>.Count);
        if (Vector256.IsHardwareAccelerated)
        {
            var permImm = Vector256.Create(4, 5, 6, 7, 0, 0, 0, 0);
            for (; i < maxElem; i += (nuint)Vector256<float>.Count)
            {
                /*
                 * We need to compute the sum and max of each left and right sample.
                 * These samples are interleaved LRLR... we do a bunch of shuffles and permutes
                 * to line up each sample within the vector so we can add/max vertically in as
                 * few steps as possible.
                 * Note that we choose to use a vertical add as opposed to a horizontal add
                 * and prefer shuffle to permute when possible for increased uop throughput.
                 * 
                 *  p0 = [1,2,3,4,5,6,7,8] [LRLRLRLRLR]
                 *  p1 = perm [5,6,7,8,x,x,x,x]
                 *  p2 = vec + p1 => [15,26,37,48,..,..,..,..]
                 *  p3 = shuf [37,48,..,..,..,..,..,..,..]   imm = [2 + 3 << 2]
                 *  p4 = p2 + p3
                 *  L = p4[0]; R = p4[1]
                 */
                var vec = Vector256.LoadUnsafe(ref buffRef, i);

                var vecAbs = Vector256.Abs(vec);
                var maxVec = Avx.Max(vecAbs, Avx2.PermuteVar8x32(vecAbs, permImm)).GetLower();
                maxVec = Sse.Max(maxVec, Sse2.Shuffle(maxVec.AsInt32(), (byte)(2 + (3 << 2))).AsSingle());
                maxSample = Sse.Max(maxSample, maxVec);

                var vecSqr = vec * vec;
                var rmsVec = (vecSqr + Avx2.PermuteVar8x32(vecSqr, permImm)).GetLower();
                rmsVec += Sse2.Shuffle(rmsVec.AsInt32(), (byte)(2 + (3 << 2))).AsSingle();
                rmsSample += rmsVec;
            }
        }

        for (; i < (nuint)buffer.Length; i += 2)
        {
            var s = Vector64.LoadUnsafe(ref buffRef, i).ToVector128Unsafe();
            s = Vector128.Abs(s);
            maxSample = Vector128.Max(s, maxSample);
            rmsSample += s * s;
        }

        var samplesRecip = Vector128.Create(1f / i);
        rmsSample = Sse.Sqrt(Sse.Multiply(rmsSample, samplesRecip));

        max = maxSample.GetLower();
        rms = rmsSample.GetLower();
    }

    public static void ComputePeakRMSMono(ReadOnlySpan<float> buffer, out float max, out float rms)
    {
        ref var buffRef = ref MemoryMarshal.GetReference(buffer);
        float maxSample = 0;
        float rmsSample = 0;
        nuint i = 0;
        if (Vector256.IsHardwareAccelerated && buffer.Length > Vector256<float>.Count * 2)
        {
            var maxSampleVec = Vector256<float>.Zero;
            var rmsSampleVec = Vector256<float>.Zero;
            for (; i < (nuint)buffer.Length; i += (nuint)Vector256<float>.Count * 2)
            {
                /*
                 * We need to compute the sum and max of each sample.
                 * We do a bunch of shuffles and permutes to line up 
                 * each sample within the vector so we can add/max 
                 * vertically in as few steps as possible.
                 * Note that we choose to use a vertical add as opposed to a horizontal add
                 * and prefer shuffle to permute when possible for increased uop throughput.
                 * 
                 *  p0 = [1,2,3,4,5,6,7,8] [LLLLLLLL]
                 *  p1 = shuf [3,4,.,.,7,8,.,.]   imm = [2 + 3 << 2]
                 *  p2 = vec + p1 => [13,24,..,..,57,68,..,..]
                 *  p3 = shuf [24,..,..,..,68,..,..,..,..]
                 *  p4 = p2 + p3
                 *  s  = p4.Lower[0] + p4.Upper[0]
                 * 
                 *  p0 = [1,2,3,4,5,6,7,8] [LLLLLLLL]
                 *  p1 = perm [5,6,7,8,x,x,x,x]
                 *  p2 = vec + p1 => [15,26,37,48,..,..,..,..]
                 *  p3 = shuf [37,48,..,..,..,..,..,..,..]   imm = [2 + 3 << 2]
                 *  p4 = p2 + p3
                 *  L = p4[0]; R = p4[1]
                 */
                var vecA = Vector256.LoadUnsafe(ref buffRef, i);
                var vecB = Vector256.LoadUnsafe(ref buffRef, i + (nuint)Vector256<float>.Count);

                var vmax = Avx.Max(Vector256.Abs(vecA), Vector256.Abs(vecB));
                maxSampleVec = Avx.Max(maxSampleVec, vmax);

                var vsqr = vecA * vecA + vecB * vecB;
                rmsSampleVec += vsqr;
            }

            // Reduce the accumulated vectors
            var maxReduc = Avx.Max(maxSampleVec, Avx2.Shuffle(maxSampleVec.AsInt32(), 2 + (3 << 2)).AsSingle());
            maxReduc = Avx.Max(maxReduc, Avx2.Shuffle(maxReduc.AsInt32(), 2).AsSingle());
            maxSample = MathF.Max(maxSample, MathF.Max(maxReduc[0], maxReduc[4]));

            var rmsReduc = rmsSampleVec + Avx2.Shuffle(rmsSampleVec.AsInt32(), 2 + (3 << 2)).AsSingle();
            rmsReduc += Avx2.Shuffle(rmsReduc.AsInt32(), 2).AsSingle();
            rmsSample += rmsReduc[0] + rmsReduc[4];
        }

        for (; i < (nuint)buffer.Length; i++)
        {
            var s = Unsafe.Add(ref buffRef, i);
            s = MathF.Abs(s);
            maxSample = MathF.Max(s, maxSample);
            rmsSample += s * s;
        }

        rmsSample = MathF.Sqrt(rmsSample / i);

        max = maxSample;
        rms = rmsSample;
    }
}

public struct MeteringEvent
{
    public int samplesMeasured;
    public float rmsL;
    public float rmsR;
    public float peakL;
    public float peakR;
}
