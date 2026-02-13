using NAudio.Wave;
using QPlayer.Models;
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
    private readonly double mcSampleRate;
    private readonly int alignmentSize;
    private readonly int channelCount;

    private bool infinite;
    private int loops;
    private long samplePosition;
    private long totalPosition;
    private long playedLoops = 0;
    private long startTime;
    private long endTime;
    private PeakFile? peakFile;

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
        channelCount = alignmentSize = wf.Channels;//~(wf.BlockAlign - 1) << 2;
        // This is used for seeking within the file, for compressed files (especially if VBR) this isn't very accurate
        // if the peak file is loaded, the timing information withi will be used instead.
        bytesPerSample = wf.AverageBytesPerSecond / (double)wf.SampleRate;
    }

    public WaveFormat WaveFormat => input.WaveFormat;

    /// <summary>
    /// The length in samples (estimate, for compressed inputs) of the input stream.
    /// </summary>
    public long SrcLength
    {
        get
        {
            if (peakFile.HasValue)
                return peakFile.Value.length * channelCount; // The peak file stores the mono length, hence multiply by channel count
            else
                return (long)(input.Length / bytesPerSample);
        }
    }

    /// <summary>
    /// The position in samples (estimate) within the input stream.
    /// </summary>
    public long SrcPosition
    {
        get => samplePosition;
        set
        {
            value = Align(value);
            if (peakFile.HasValue)
                input.Position = ComputeBytePosFromPeakFile(value);
            else
                input.Position = (long)(value * bytesPerSample);

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
            var srcLen = Math.Max(1, TrimmedSrcLength);
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

    public PeakFile? PeakFile
    {
        get => peakFile;
        set => peakFile = value;
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
        if (read < count)
        {
            // Try again, if we actually got 0 samples this time, it must be the end of the file.
            read = ReadInternal(buffer, offset + read, count - read);
            totalRead += read;
        }

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

    private long ComputeBytePosFromPeakFile(long samplePos)
    {
        var lookup = peakFile!.Value.samplePosToBytePos;
        var increment = peakFile!.Value.samplePosIncrement;

        // return (long)((samplePos / (double)peakFile!.Value.length / 2) * input.Length);

        var lookupPos = samplePos / increment;
        var interp = samplePos & (increment - 1);

        var startPos = lookupPos == 0 ? 0 : lookup[lookupPos - 1];
        // var endPos = lookup[lookupPos];

        return startPos + (long)(bytesPerSample * interp);// (endPos - startPos)
    }
}
