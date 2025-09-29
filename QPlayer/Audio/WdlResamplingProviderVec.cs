using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace QPlayer.Audio;

public class WdlResamplingProviderVec : ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private readonly WaveFormat waveFormat;
    private readonly WdlResampler resampler;
    private readonly int channels;

    public long Position { get => source.Position; set => source.Position = value; }

    public WaveFormat WaveFormat => waveFormat;

    public WdlResamplingProviderVec(ISamplePositionProvider source, int newSampleRate, int channels)
    {
        this.source = source;
        waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(newSampleRate, channels);
        this.channels = waveFormat.Channels;
        resampler = new WdlResampler(source.WaveFormat.SampleRate, newSampleRate, interp: true, 2, sinc: false);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int dstFrames = count / channels;
        int srcFrames = resampler.ResamplePrepare(dstFrames, channels, out float[] srcBuff, out int srcOffset);
        int nsamples_in = source.Read(srcBuff, srcOffset, srcFrames * channels) / channels;
        return resampler.ResampleOut(buffer, offset, nsamples_in, dstFrames, channels) * channels;
    }
}
