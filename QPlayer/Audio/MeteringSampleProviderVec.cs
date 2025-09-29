using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Audio;

public class MeteringSampleProviderVec : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly float[] maxSamples;
    private readonly float[] rmsSamples;
    private readonly int channels;

    private int sampleCount;

    /// <summary>
    /// The number of samples to read between each metering event.
    /// </summary>
    public int SamplesPerNotification { get; set; }

    public WaveFormat WaveFormat => source.WaveFormat;

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
        if (OnMeter == null)
            return num;

        // TODO: Vectorise
        for (int i = 0; i < num; i += channels)
        {
            for (int j = 0; j < channels; j++)
            {
                float val = Math.Abs(buffer[offset + i + j]);
                maxSamples[j] = Math.Max(maxSamples[j], val);
                rmsSamples[j] += val;
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
}

public struct MeteringEvent
{
    public int samplesMeasured;
    public float rmsL;
    public float rmsR;
    public float peakL;
    public float peakR;
}
