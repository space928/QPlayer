using NAudio.Wave;
using QPlayer.ViewModels;
using System;

namespace QPlayer.Audio;

/// <summary>
/// A sample provider for <see cref="WaveStream"/> which handles looping and start/end time.
/// </summary>
/// <typeparam name="T"></typeparam>
public class LoopingSampleProvider : ISamplePositionProvider
{
    private readonly QAudioFileReader input;
    private readonly double mcSampleRate;

    private bool infinite;
    private int loops;
    private long totalPosition;
    private long playedLoops = 0;
    private long startTime;
    private long endTime;
    private bool justSeeked = false;
    private long devampLoop = 0;

    public LoopingSampleProvider(QAudioFileReader input, bool infinite = true, int loops = 1)
    {
        this.input = input;
        this.infinite = infinite;
        this.loops = loops;

        WaveFormat wf = input.WaveFormat;
        mcSampleRate = wf.SampleRate * wf.Channels;
        // This should correspond with the multiple of samples we must read from input.Read(), it should be
        // related to wf.BlockAlign, but for all intents and purposes we just use wf.Channels. This might not 
        // be correct for compressed formats.
        // This is used for seeking within the file, for compressed files (especially if VBR) this isn't very accurate
        // if the peak file is loaded, the timing information within will be used instead.
    }

    public WaveFormat WaveFormat => input.WaveFormat;

    /// <summary>
    /// The length in samples (estimate, for compressed inputs) of the input stream.
    /// </summary>
    public long SrcLength => input.NumSamples;

    /// <summary>
    /// The position in samples (estimate) within the input stream.
    /// </summary>
    public long SrcPosition
    {
        get => input.SamplePosition;
        set
        {
            input.SamplePosition = value;
            justSeeked = true;
            devampLoop = 0;
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

    /// <summary>
    /// Whether this sample provider should loop the sample indefinitely. Ignores the <see cref="Loops"/> value.
    /// </summary>
    public bool Infinite { get => infinite; set => infinite = value; }
    /// <summary>
    /// The maximum number of this sample provider can play. Use <see cref="Reset"/> to reset the loop counter.
    /// </summary>
    public int Loops { get => loops; set => loops = value; }
    /// <summary>
    /// The current number of complete loops of the input sample that have been played since the last call to <see cref="Reset"/>.
    /// </summary>
    public long PlayedLoops => playedLoops;
    /// <summary>
    /// The time in samples to start playback from.
    /// </summary>
    public long StartSample
    {
        get => startTime;
        set
        {
            startTime = input.Align(value);
            input.StartSamplePosition = startTime;
        }
    }
    /// <summary>
    /// The time in samples to stop playback at (or the 'out' loop point of the loop).
    /// </summary>
    public long EndSample { get => endTime; set => endTime = input.Align(value); }
    /// <summary>
    /// The time to start playback from.
    /// </summary>
    public TimeSpan StartTime
    {
        get => TimeSpan.FromSeconds(startTime / mcSampleRate);
        set => StartSample = (long)(value.TotalSeconds * mcSampleRate);
    }
    /// <summary>
    /// The time at which to stop playback (for a single loop).
    /// </summary>
    public TimeSpan EndTime
    {
        get => TimeSpan.FromSeconds(endTime / mcSampleRate);
        set => endTime = input.Align((long)(value.TotalSeconds * mcSampleRate));
    }

    /// <summary>
    /// An event raised at the end of each loop played.
    /// </summary>
    public event Action? LoopCompleted;

    private event Action? DevampAction;

    /// <summary>
    /// Resets the internal loop and sample counters. Call this every time before playing the sound file.
    /// </summary>
    public void Reset()
    {
        playedLoops = 0;
        //samplePosition = 0;
        totalPosition = 0;
        SrcPosition = startTime;
        DevampAction = null;
        devampLoop = 0;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read;
        int totalRead = 0;
        int lastTotal = -1;
        do
        {
            // We always read at least twice, I can't remember exactly why, but seems safe in case the source
            // was waiting for another call to refresh it's buffers.
            read = ReadInternal(buffer, offset + totalRead, count - totalRead);
            totalRead += Math.Max(0, read);

            do
            {
                read = ReadInternal(buffer, offset + totalRead, count - totalRead);
                totalRead += Math.Max(0, read);
            }
            while (totalRead < count && read > 0);

            if (read == -1)
            {
                // This means no more samples are available yet, but more samples might be available later (ie the
                // stream hasn't ended). Hence we pad with some silence if needed and return early.
                if (totalRead < count)
                    buffer.AsSpan(offset + totalRead, count - totalRead).Clear();
                // Unless we just seeked (which is bound to result in a gap) warn of any out of sample errors.
                if (!justSeeked)
                    MainViewModel.Log($"Audio file couldn't be read fast enough for real-time playback! Ensure the disk/cpu isn't overloaded.\n{input.FileName}", MainViewModel.LogLevel.Warning);
                return count;
            }

            justSeeked = false;
            if (totalRead >= count)
                break;

            if (HasReachedLoopEnd(read))
            {
                // We reached the end of the sample, loop
                playedLoops++;
                if (DevampAction != null)
                {
                    // Don't loop, devamp
                    DevampAction();
                    devampLoop = playedLoops + 1;
                    DevampAction = null;
                    endTime = 0;
                }
                else
                {
                    // Go back to the start
                    SrcPosition = startTime;
                }

                LoopCompleted?.Invoke();

                // Safety hatch, in case we keep reading 0 samples.
                if (totalRead <= lastTotal)
                    break;
                lastTotal = totalRead;
            }
        } while (true);

        return totalRead;

        bool HasReachedLoopEnd(int lastRead) => lastRead == 0 || (endTime != 0 && SrcPosition >= endTime);
    }

    /// <summary>
    /// Invokes the given action once, at the end of the next loop. The action is cancelled if this sample provider 
    /// is reset. Note that this callback will be invoked on the audio thread.
    /// </summary>
    /// <param name="action">An action which returns <see langword="true"/> if playback should be stopped after this action returns.</param>
    public void DeVamp(Action onDevampStart)
    {
        DevampAction += onDevampStart;
    }

    /// <summary>
    /// Reads samples from the source until we reach the end of a loop (or file) or 
    /// the buffer is full (whichever happens sooner). Returns 0 if no more loops
    /// should be played.
    /// </summary>
    /// <param name="buffer"></param>
    /// <param name="offset"></param>
    /// <param name="count"></param>
    /// <returns></returns>
    private int ReadInternal(float[] buffer, int offset, int count)
    {
        // If we have no more loops to play, return no samples
        if (count == 0 || (!infinite && playedLoops >= loops) || (devampLoop > 0 && playedLoops >= devampLoop))
            return 0;

        // Limit count if it would exceed the end time
        if (endTime != 0)
            count = Math.Max(0, (int)Math.Min((long)count, endTime - SrcPosition));

        int read = input.Read(buffer, offset, count);
        // SrcPosition += read;
        totalPosition += Math.Max(0, read);

        return read;
    }
}
