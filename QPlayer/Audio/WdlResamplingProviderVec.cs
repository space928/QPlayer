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

// Until the resampling bug can be fixed, we'll use the old resampler
using Resampler = QPlayer.Audio.WdlResampler;
// using Resampler = NAudio.Dsp.WdlResampler;

namespace QPlayer.Audio;

public class WdlResamplingProviderVec : ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private readonly WaveFormat waveFormat;
    private readonly Resampler resampler;
    private readonly int channels;

    public long Position { get => source.Position; set => source.Position = value; }

    public WaveFormat WaveFormat => waveFormat;

    public WdlResamplingProviderVec(ISamplePositionProvider source, int newSampleRate, int channels)
    {
        this.source = source;
        waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(newSampleRate, channels);
        this.channels = waveFormat.Channels;
        
        resampler = new Resampler(source.WaveFormat.SampleRate, newSampleRate, interp: true, 2, sinc: false);
        
        /*resampler = new();
        resampler.SetMode(true, 2, false);
        //resampler.SetMode(false, 0, true);
        resampler.SetRates(source.WaveFormat.SampleRate, newSampleRate);
        resampler.SetFeedMode(false);*/
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int dstFrames = count / channels;
        int srcFrames = resampler.ResamplePrepare(dstFrames, channels, out float[] srcBuff, out int srcOffset);
        int nframes_in = source.Read(srcBuff, srcOffset, srcFrames * channels) / channels;
        return resampler.ResampleOut(buffer, offset, nframes_in, dstFrames, channels) * channels;
    }
}
