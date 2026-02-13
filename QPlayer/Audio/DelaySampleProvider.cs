using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading;

namespace QPlayer.Audio;

public class DelaySampleProvider : DelayBuffer, ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private readonly WaveFormat waveFormat;

    public WaveFormat WaveFormat => waveFormat;

    public long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    public DelaySampleProvider(ISamplePositionProvider source, uint maxDelayTime) : base(maxDelayTime)
    {
        this.source = source;
        this.waveFormat = source.WaveFormat;
    }

    /// <summary>
    /// Reads the specified number of samples from the input stream and pushes them to the delay buffer.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public int Read(float[] buffer, int offset, int count)
    {
        // Read enough samples to fill at most half the buffer (half the buffer represents the maximum delay time)
        int read = source.Read(buffer, offset, Math.Min(count, maxDelayTime));

        // Now copy these samples into the delay buffer
        Push(buffer.AsSpan(offset, read));

        return read;
    }

    /// <summary>
    /// Reads the specified number of samples from the delay buffer with the specified amount of delay. 
    /// This should imediately preceed a call to <see cref="Read(float[], int, int)"/> and should read 
    /// the same number of samples the read call returned.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    public int ReadDelayed(float[] buffer, int offset, int count, int delaySamples)
    {
        var firstHalf = GetDelayed(count, delaySamples, out var secondHalf);
        firstHalf.CopyTo(buffer.AsSpan(offset));
        secondHalf.CopyTo(buffer.AsSpan(offset + firstHalf.Length));

        return firstHalf.Length + secondHalf.Length;
    }
}
