using NAudio.Wave;
using System;

namespace QPlayer.Audio;

/// <summary>
/// A sample provider for <see cref="WaveStream"/> which handles looping and start/end time.
/// </summary>
/// <typeparam name="T"></typeparam>
public class LoopingSampleProvider<T> : ISamplePositionProvider where T : WaveStream, ISampleProvider
{
    private readonly T input;
    private readonly double bytesPerSample;
    private readonly int bytesPerSampleInt;
    private readonly double mcSampleRate;
    private readonly int alignmentSize;

    private bool infinite;
    private int loops;
    private long samplePosition;
    private long totalPosition;
    private long playedLoops = 0;
    private long startTime;
    private long endTime;

    public LoopingSampleProvider(T input, bool infinite = true, int loops = 1)
    {
        this.input = input;
        this.infinite = infinite;
        this.loops = loops;

        var wf = input.WaveFormat;
        mcSampleRate = wf.SampleRate * wf.Channels;
        // This should correspond with the multiple of samples we must read from input.Read(), it should be
        // related to wf.BlockAlign, but for all intents and purposes we just use wf.Channels. This might not 
        // be correct for compressed formats.
        alignmentSize = wf.Channels;//~(wf.BlockAlign - 1) << 2;
        // For length/position we need to know how many bytes there are to a sample
        // For compressed inputs, this might not an integer, for VBR streams, it'll
        // be very innacurate. But if it does land nicely on an integer, use it.
        bytesPerSampleInt = wf.BitsPerSample / 8;
        if (bytesPerSampleInt == 0)
        {
            bytesPerSample = wf.AverageBytesPerSecond / (double)wf.SampleRate;
            var bpsRnd = Math.Round(bytesPerSample);
            if (Math.Abs(bytesPerSample - bpsRnd) < 1e-8)
                bytesPerSampleInt = (int)bpsRnd;
        }
    }

    public WaveFormat WaveFormat => input.WaveFormat;

    /// <summary>
    /// The length in samples (estimate, for compressed inputs) of the input stream.
    /// </summary>
    public long SrcLength => bytesPerSampleInt == 0 ? (long)(input.Length / bytesPerSample) : input.Length / bytesPerSampleInt;

    /// <summary>
    /// The position in samples (estimate) within the input stream.
    /// </summary>
    public long SrcPosition
    {
        get => samplePosition;
        set
        {
            value = Align(value);
            var newPos = bytesPerSampleInt == 0 ? (long)(value * bytesPerSample) : value * bytesPerSampleInt;

            input.Position = newPos;

            samplePosition = value;
        }
    }

    /// <summary>
    /// The current playback time within the input stream.
    /// </summary>
    public TimeSpan SrcCurrentTime
    {
        get => TimeSpan.FromSeconds(SrcPosition / mcSampleRate);
        set => SrcPosition = (long)(value.TotalSeconds * mcSampleRate);
    }

    /// <summary>
    /// The length in samples of the input stream, trimmed by the the <see cref="StartSample"/> and <see cref="EndSample"/>.
    /// </summary>
    public long TrimmedSrcLength
    {
        get
        {
            if (endTime == 0)
                return (SrcLength - startTime);

            return (endTime - startTime);
        }
    }

    public TimeSpan SrcTotalTime => TimeSpan.FromSeconds(TrimmedSrcLength / mcSampleRate);

    /// <summary>
    /// The length in samples of this looping sample provider (estimate, for compressed inputs).
    /// </summary>
    public long Length
    {
        get
        {
            if (infinite)
                return long.MaxValue;

            return TrimmedSrcLength * loops;
        }
    }

    /// <summary>
    /// The total diration of the looped input stream, taking into account the start and end times.
    /// </summary>
    public TimeSpan TotalTime
    {
        get
        {
            if (infinite)
                return TimeSpan.MaxValue;

            return TimeSpan.FromSeconds(Length / mcSampleRate);
        }
    }

    /// <summary>
    /// The current playback time within the looped sample, this starts at 0 at the start time and increases continuously as the input is looped.
    /// </summary>
    public TimeSpan CurrentTime
    {
        get => TimeSpan.FromSeconds(Position / mcSampleRate);
        set => Position = (long)(value.TotalSeconds * mcSampleRate);
    }

    /// <summary>
    /// The current playback sample position within the looped sample, this starts at 0 at the start time and increases continuously as the input is looped.
    /// </summary>
    public long Position
    {
        get => totalPosition;
        set
        {
            totalPosition = value;
            var srcLen = TrimmedSrcLength;
            SrcPosition = value % srcLen + startTime;
            playedLoops = value / srcLen;
        }
    }

    public bool Infinite { get => infinite; set => infinite = value; }
    public int Loops { get => loops; set => loops = value; }
    public long PlayedLoops => playedLoops;
    /// <summary>
    /// The time in samples to start playback from.
    /// </summary>
    public long StartSample { get => startTime; set => startTime = Align(value); }
    /// <summary>
    /// The time in samples to stop playback at (or the 'out' loop point of the loop).
    /// </summary>
    public long EndSample { get => endTime; set => endTime = Align(value); }
    public TimeSpan StartTime
    {
        get => TimeSpan.FromSeconds(startTime / mcSampleRate);
        set => startTime = Align((long)(value.TotalSeconds * mcSampleRate));
    }
    public TimeSpan EndTime
    {
        get => TimeSpan.FromSeconds(endTime / mcSampleRate);
        set => endTime = Align((long)(value.TotalSeconds * mcSampleRate));
    }

    /// <summary>
    /// An event raised at the end of each loop played.
    /// </summary>
    public event Action? LoopCompleted;

    /// <summary>
    /// Resets the internal loop and sample counters. Call this every time before playing the sound file.
    /// </summary>
    public void Reset()
    {
        playedLoops = 0;
        //samplePosition = 0;
        totalPosition = 0;
        SrcPosition = startTime;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = ReadInternal(buffer, offset, count);
        int totalRead = read;

        if (read == 0 || (endTime != 0 && samplePosition >= endTime))
        {
            // We reached the end of the sample, loop
            SrcPosition = startTime;
            playedLoops++;
            LoopCompleted?.Invoke();

            // To prevent the playback manager from removing this sample at the end of the loop,
            // repeateadly keep reading until the count is satisfied
            if (infinite || playedLoops < loops)
            {
                do
                {
                    offset += read;
                    count -= read;
                    read = ReadInternal(buffer, offset, count);
                    totalRead += read;
                } while (count > 0);
            }
        }

        return totalRead;
    }

    public int ReadInternal(float[] buffer, int offset, int count)
    {
        // If we have no more loops to play, return no samples
        if (count == 0 || (!infinite && playedLoops >= loops))
            return 0;

        // Limit count if it would exceed the end time
        if (endTime != 0)
            count = Math.Max(0, (int)Math.Min((long)count, endTime - samplePosition));

        int read = input.Read(buffer, offset, count);
        samplePosition += read;
        totalPosition += read;

        return read;
    }

    private long Align(long pos)
    {
        int align = alignmentSize;
        if (align == 2)
            return pos & (-2);

        return (pos / align) * align;
    }
}
