using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QPlayer.Models;
using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace QPlayer.Audio;

/// <summary>
/// A smart audio reader, which picks an appropriate decoder for the given file path, opens the file, 
/// creates any needed stream converters to get a 32bit float sample stream, and buffers audio intelligently.
/// <para/>
/// The <see cref="Read(float[], int, int, bool)"/> methods are entirely lock-free and intended to be called 
/// from a dedicated audio thread, while all the other methods are intended to be called from the main thread. 
/// This is with the exception of the <see cref="FillBuffer"/> methods which can be called from a separate 
/// audio buffering thread (this is done automatically, if an <see cref="AudioBufferingDispatcher"/> is 
/// passed in the constructor). Please be aware that the threading model for this class is quite complicated 
/// (there are potentially 3+ threads with contention over this object), most methods are not reentrant unless 
/// explicitly mentioned (or trivial).
/// <para/>
/// The lifecycle of this object is described as follows:
/// <code>
/// MainThread:
///     QAudioFileReader::ctor()
///     \--> registers the new object with the AudioBufferingDispatcher
///     ...
///     SamplePosition.set() --> repositions the stream when needed, this blocks until the current FillBuffer operation is complete
///     
/// AudioBufferingDispatcher worker:
///     while true:
///         if audioFile.NeedsFilling:
///             FillBuffer() --> fills one of the internal audio buffers if needed
/// 
/// AudioThread:
///     Read() --> gets as many samples as are available (or requested) from an audio buffer, never blocks, but may not return as many samples as requested
/// </code>
/// </summary>
public class QAudioFileReader : WaveStream, ISampleProvider
{
    private WaveStream? readerStream;
    private readonly AudioBufferingDispatcher? dispatcher;
    private readonly SemaphoreSlim readerSem;
    private readonly long length;
    private readonly WaveFormat waveFormat;
    private readonly ISampleProvider sampleProvider;
    private readonly int channelCount;
    private readonly double samplesPerByte;
    private readonly double bytesPerSample;
    private readonly int alignmentSize;
    private readonly float[] audioBufferA;
    private readonly float[] audioBufferB;
    private readonly float[] audioBufferStart; // A separate buffer to store just the start of the file, this is neeed for seemless looping.

    private volatile bool readBufferA;
    private volatile int bufACount = -1;
    private volatile int bufBCount = -1;
    private volatile int bufStartCount = -1;
    private volatile bool reachedEnd;
    private int bufferPos;
    private bool isMediaFoundationReader;
    private PeakFile? peakFile;
    private long samplePosition;
    private long startSamplePosition;
    private long nextReaderBytePosition = -1;

    // TODO: For large projects, this could get heavy on the memory usage, this is currently >1MB of memory per-cue.
    // For memory-constrained platforms (like rpi) this could cause problems. We could introduce some smarts (dynamically
    // renting arrays for the A/B buffers as needed, and uses a smaller fixed start buffer).
    private const int BUFFER_LENGTH = 48000 * 2; // 1 second at 48KHz stereo

    public string FileName { get; init; }
    public override long Length => length;
    public override long Position
    {
        get => readerStream!.Position;
        set => throw new InvalidOperationException("Use SamplePosition instead.");// SamplePosition = value << 2;//readerStream!.Position = Math.Clamp(value, 0, length - 1);
    }
    public override WaveFormat WaveFormat => waveFormat;
    public WaveFormat ConvertedWaveFormat => sampleProvider.WaveFormat;
    public WaveStream? ReaderStream => readerStream;
    public bool IsMediaFoundationReader => isMediaFoundationReader;

    public PeakFile? PeakFile
    {
        get => peakFile;
        set => peakFile = value;
    }

    /// <summary>
    /// The number of samples in this stream.
    /// </summary>
    public long NumSamples
    {
        get
        {
            if (peakFile.HasValue)
                return peakFile.Value.length * channelCount; // The peak file stores the frame count, hence multiply by channel count
            else
                return (long)(Length * samplesPerByte);
        }
    }

    /// <summary>
    /// The position in samples within the input stream.
    /// </summary>
    public long SamplePosition
    {
        get => samplePosition;
        set
        {
            value = Align(value);
            SeekReader(value);
        }
    }

    /// <summary>
    /// The starting position in samples within the input stream. Setting this clears the buffer 
    /// containing the start of the file used for seamless looping.
    /// </summary>
    public long StartSamplePosition
    {
        get => startSamplePosition;
        set
        {
            // This compare should be safe as this setter is the only place this field is set, and it's not expected to be reentrant.
            if (startSamplePosition != value)
            {
                // That being said, we still need these writes to happen atomically and in order, hence we use volatile writes.
                Volatile.Write(ref startSamplePosition, value);
                bufStartCount = -1;
            }
        }
    }

    /// <summary>
    /// Gets how many samples are still available in the current buffer.
    /// </summary>
    internal int SamplesRemaining => (readBufferA ? bufACount : bufBCount) - bufferPos;
    /// <summary>
    /// Gets whether this reader has an empty buffer waiting to be filled with <see cref="FillBuffer"/>.
    /// </summary>
    internal bool NeedsFilling => (readBufferA ? bufBCount : bufACount) < 0;
    /// <summary>
    /// Gets whether this reader has an empty start buffer waiting to be filled <see cref="FillStartBuffer"/>.
    /// </summary>
    internal bool NeedsStartFilling => bufStartCount < 0;

    public QAudioFileReader(string fileName, AudioBufferingDispatcher? dispatcher = null)
    {
        FileName = fileName;
        CreateReaderStream(fileName);
        sampleProvider = ConvertWaveProviderIntoSampleProvider(readerStream);
        length = readerStream.Length;
        waveFormat = readerStream.WaveFormat;

        //mcSampleRate = waveFormat.SampleRate * waveFormat.Channels;
        // This should correspond with the multiple of samples we must read from input.Read(), it should be
        // related to wf.BlockAlign, but for all intents and purposes we just use wf.Channels. This might not 
        // be correct for compressed formats.
        channelCount = alignmentSize = waveFormat.Channels;//~(wf.BlockAlign - 1) << 2;
        // This is used for seeking within the file, for compressed files (especially if VBR) this isn't very accurate
        // if the peak file is loaded, the timing information within will be used instead.
        samplesPerByte = ((double)waveFormat.SampleRate * waveFormat.Channels) / waveFormat.AverageBytesPerSecond;
        bytesPerSample = 1 / samplesPerByte;

        audioBufferA = new float[BUFFER_LENGTH];
        audioBufferB = new float[BUFFER_LENGTH];
        audioBufferStart = new float[BUFFER_LENGTH / 4];

        readerSem = new(1);

        this.dispatcher = dispatcher;// ?? AudioBufferingDispatcher.Default;
        this.dispatcher?.RegisterAudioFile(this);
    }

    /// <inheritdoc cref="Read(float[], int, int, bool)"/>
    public int Read(float[] buffer, int offset, int count)
    {
        // The logic in here is quite delicate, test thoroughly when optimising.
        // TODO: We need to ensure that this always returns a multiple of the channel count of samples, otherwise downstream consumers will break.
        int retSamples;
        int nextBuffPos = bufferPos;
        bool _readBufA = readBufferA;
        var buf = _readBufA ? audioBufferA : audioBufferB;
        var len = _readBufA ? bufACount : bufBCount;
        var bufSpan = nextBuffPos < len ? buf.AsSpan()[nextBuffPos..len] : Span<float>.Empty;

        if (count < bufSpan.Length)
        {
            // We have enough buffered samples, so just return those. This should be the usual code
            // path given a sufficiently large buffer.
            bufSpan[..count].CopyTo(buffer.AsSpan(offset));

            nextBuffPos += count;
            retSamples = count;
            goto Done;
        }
        else
        {
            // Note that this code path also executes when count == len, this ensures that the buffers 
            // get swapped as early as possible.
            bufSpan.CopyTo(buffer.AsSpan(offset));
            int written = bufSpan.Length;
            var nextLen = _readBufA ? bufBCount : bufACount;
            // If the next buffer is ready, try to read from it
            if (nextLen >= 0)
            {
                int remain = count - written;
                // Flip the buffers
                if (_readBufA) // Invalidate all the samples in the buffer we were reading from so they can be written to
                    bufACount = -1;
                else
                    bufBCount = -1;
                _readBufA ^= true; // As soon as this is swapped, our original bufSpan could be invalid, and nextBufCount is invalid.
                readBufferA = _readBufA;
                buf = _readBufA ? audioBufferA : audioBufferB;
                //Debug.WriteLine($"Read swapped buffers read = {(readBufferA ? 'A': 'B')} avail = {nextLen}");

                // Get as many samples as we can
                bufSpan = buf.AsSpan(0, Math.Min(remain, nextLen));
                // Copy them to the remainder of the destination buffer
                bufSpan.CopyTo(buffer.AsSpan(offset + written));

                nextBuffPos = bufSpan.Length;
                retSamples = written + bufSpan.Length;
                goto Done;
            }
            else
            {
                // Check to see if we have buffered samples in the start buffer
                long startDelta = Volatile.Read(ref samplePosition) - Volatile.Read(ref startSamplePosition);
                int _bufStartCount = bufStartCount;
                if (startDelta >= 0 && startDelta < _bufStartCount)
                {
                    // We're in luck! The start buffer has our samples! Return samples from there until the main buffers fill back up.
                    // Get as many samples as we can
                    int _startDelta = (int)startDelta;
                    int remainingCount = count - written;
                    int available = _bufStartCount - _startDelta;
                    int toCopy = Math.Min(available, remainingCount);
                    bufSpan = audioBufferStart.AsSpan(_startDelta, toCopy);
                    // Copy them to the remainder of the destination buffer
                    bufSpan.CopyTo(buffer.AsSpan(offset + written));

                    written += toCopy;
                    // Make sure to consume the samples from the main AB buffers even though we didn't actually
                    // take them (they might not even exist yet), audioStartBuffer should always be smaller than
                    // the audio buffers so I don't think we should need to worry about bufferPos overflowing.
                    nextBuffPos += written; 
                    retSamples = written;
                    goto Done;
                }
                else
                {
                    // Debug.WriteLine($"Read ran out of samples buf = {(readBufferA ? 'A' : 'B')} read = {written} pos = {bufferPos}");
                    // Next buffer isn't ready yet
                    nextBuffPos = buf.Length;
                    retSamples = written;

                    if (retSamples == 0)
                    {
                        if (!reachedEnd)
                            retSamples = -1; // No samples for now, but more might be coming...
                    }

                    goto Done;
                }
            }
        }

    Done:
        bufferPos = nextBuffPos;
        Interlocked.Add(ref samplePosition, retSamples);
        return retSamples;
    }

    /// <summary>
    /// Fill the specified buffer with 32 bit floating point samples. This method is not reentrant.
    /// </summary>
    /// <remarks>
    /// This method might not entirely fill the requested <paramref name="count"/> of samples if they're not available yet. 
    /// Furthermore, it may return <c>-1</c> if no samples are currently available but the end of the stream hasn't been 
    /// reached yet. A return value of <c>0</c> always indicates that the stream has ended.
    /// </remarks>
    /// <param name="buffer">The buffer to fill.</param>
    /// <param name="offset">The offset into <paramref name="buffer"/> to start writing.</param>
    /// <param name="count">The number of samples requested.</param>
    /// <param name="offline">When <see langword="true"/>, syncronously waits for the buffer to fill before returning.</param>
    /// <returns>The nunber of samples read from the source.</returns>
    public int Read(float[] buffer, int offset, int count, bool offline)
    {
        if (!offline)
            return Read(buffer, offset, count);

        // In offline reading, keep reading until the buffer is full or we read 0 samples, indicating the end of a stream.
        int totalRead = Read(buffer, offset, count);
        if (totalRead == 0)
            return 0;
        else if (totalRead < 0)
            totalRead = 0;

        while (totalRead < count)
        {
            FillBuffer();
            int read = Read(buffer, offset + totalRead, count - totalRead);
            totalRead += Math.Max(0, read); // Clamp any -1 (which indicates samples are not ready yet)
            if (read == 0) // End of stream
                break;
        }

        return totalRead;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        var floats = Unsafe.As<byte[], float[]>(ref buffer);
        return Read(floats, offset >> 2, count >> 2) << 2;
    }

    /// <summary>
    /// Fills the internal buffer of this reader with samples. Must be called sufficiently frequently to avoid starving the audio file reader.
    /// This method can be called from external threads, but is not reentrant.
    /// </summary>
    public int FillBuffer()
    {
        bool writeBufB = readBufferA;
        var buf = writeBufB ? audioBufferB : audioBufferA;
        // Debug.WriteLine($"Filling buffer {(writeBufB ? 'B' : 'A')}");
        if ((writeBufB ? bufBCount : bufACount) >= 0)
            return -1;

        int totalRead = 0;
        try
        {
            readerSem.Wait(); 
            // Read the next reader position and reset it atomically to avoid the need for a lock
            var newPos = Interlocked.Exchange(ref nextReaderBytePosition, -1);
            if (newPos != -1)
            {
                // The stream needs repositioning
                readerStream!.Position = newPos;
            }

            int read = 0;
            do
            {
                read = sampleProvider.Read(buf, totalRead, buf.Length - totalRead);
                totalRead += read;
            } while (read > 0 && totalRead < buf.Length);

            if (read == 0)
                reachedEnd = true;
        }
        finally
        {
            readerSem.Release();
        }

        if (writeBufB)
            bufBCount = totalRead;
        else
            bufACount = totalRead;
        // Debug.WriteLine($"    \\--> filled");
        return totalRead;
    }

    /// <summary>
    /// Fills the special 'start' buffer of this reader with samples. Must be called sufficiently frequently to avoid starving the audio file reader.
    /// This method can be called from external threads, but is not reentrant.
    /// <seealso cref="StartSamplePosition"/>
    /// </summary>
    /// 
    public int FillStartBuffer()
    {
        // Debug.WriteLine($"Filling start buffer");
        if (bufStartCount >= 0)
            return -1;

        int totalRead = 0;
        long readerStartPos = 0;
        try
        {
            readerSem.Wait();
            // Seek the reader back to the start
            // Note that because we have the lock here it prevents any potential races reading and
            // writing to position. Additionally, we can't use the SeekReader method as it has other
            // side effects.
            readerStartPos = readerStream!.Position;
            readerStream!.Position = ComputeBytePos(startSamplePosition);
            int read = 0;
            do
            {
                read = sampleProvider.Read(audioBufferStart, totalRead, audioBufferStart.Length - totalRead);
                totalRead += read;
            } while (read > 0 && totalRead < audioBufferStart.Length);

            if (read == 0)
                reachedEnd = true;
        }
        finally
        {
            // Go back to exactly where we were (hoping that the reader stream honours this correctly)
            readerStream!.Position = readerStartPos;
            readerSem.Release();
        }

        bufStartCount = totalRead;
        // Debug.WriteLine($"    \\--> filled");
        return totalRead;
    }

    /// <summary>
    /// Seeks the internal wave reader to the given sample position. Automatically invalidates the sample buffer if needed.
    /// </summary>
    /// <param name="newPos"></param>
    private void SeekReader(long newPos)
    {
        long delta = newPos - samplePosition;
        long newBufPos = bufferPos + delta;
        samplePosition = newPos;
        long availableSamples = readBufferA ? bufACount : bufBCount;
        // Debug.WriteLine($"Seeking to {newPos} delta: {delta}");
        if (delta == 0)
            return; // The reader didn't actually seek, do nothing (this is needed to prevent accidentally invalidating a good buffer).
        if (newBufPos >= 0 && newBufPos < availableSamples)
        {
            // The new position is inside the active read buffer, hence we can just reposition the read buffer without seeking the reader stream.
            bufferPos = (int)newBufPos;
            return;
        }

        // Seek the reader stream (asynchronously)
        long bytePos = ComputeBytePos(newPos);
        Volatile.Write(ref nextReaderBytePosition, bytePos);
        reachedEnd = false;

        // Invalidate both buffers
        bufACount = -1;
        bufBCount = -1;
        // Debug.WriteLine($"    seek: invalidated buffers");
    }

    private long ComputeBytePos(long samplePos)
    {
        long bytePos;
        if (isMediaFoundationReader)
        {
            // Lerp 0 to input.Length by our current playback fraction.
            var frac = samplePos / (double)NumSamples;
            frac *= Length;
            bytePos = (long)frac;
        }
        else if (peakFile.HasValue)
            bytePos = ComputeBytePosFromPeakFile(samplePos);
        else
            bytePos = (long)(samplePos * bytesPerSample);

        return bytePos;
    }

    [MemberNotNull(nameof(readerStream))]
    private void CreateReaderStream(string fileName)
    {
        if (fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            readerStream = new WaveFileReader(fileName);
            if (readerStream.WaveFormat.Encoding != WaveFormatEncoding.Pcm && readerStream.WaveFormat.Encoding != WaveFormatEncoding.IeeeFloat)
            {
                readerStream = WaveFormatConversionStream.CreatePcmStream(readerStream);
                readerStream = new BlockAlignReductionStream(readerStream);
            }
        }
        else if (fileName.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase))
        {
            if (Environment.OSVersion.Version.Major < 6)
            {
                readerStream = new Mp3FileReader(fileName);
            }
            else
            {
                readerStream = new MediaFoundationReader(fileName);
                isMediaFoundationReader = true;
            }
        }
        else if (fileName.EndsWith(".aiff", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".aif", StringComparison.OrdinalIgnoreCase))
        {
            readerStream = new AiffFileReader(fileName);
        }
        else
        {
            readerStream = new MediaFoundationReader(fileName);
        }
    }

    public static ISampleProvider ConvertWaveProviderIntoSampleProvider(IWaveProvider waveProvider)
    {
        if (waveProvider.WaveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            return waveProvider.WaveFormat.BitsPerSample switch
            {
                8 => new Pcm8BitToSampleProviderVec(waveProvider),
                16 => new Pcm16BitToSampleProviderVec(waveProvider),
                24 => new Pcm24BitToSampleProviderVec(waveProvider),
                32 => new Pcm32BitToSampleProviderVec(waveProvider),
                _ => throw new InvalidOperationException("Unsupported bit depth"),
            };
        }

        if (waveProvider.WaveFormat.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            return waveProvider.WaveFormat.BitsPerSample switch
            {
                64 => new WaveToSampleProvider64(waveProvider),
                32 => new FloatToSampleProviderVec(waveProvider),
                _ => throw new InvalidOperationException("Unsupported bit depth"),
            };
        }

        throw new ArgumentException("Unsupported source encoding");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && readerStream != null)
        {
            dispatcher?.UnregisterAudioFile(this);
            readerStream.Dispose();
            readerStream = null;
        }

        base.Dispose(disposing);
    }

    /// <summary>
    /// Aligns a position in samples to the start of the current frame. Effecitvely this rounds the input 
    /// position down to a multiple of the channel count. Note that this is only valid for positive values.
    /// </summary>
    /// <param name="pos">The position in samples to align.</param>
    /// <returns>The aligned position in samples.</returns>
    public long Align(long pos)
    {
        if (pos <= 0)
            return 0;

        int align = alignmentSize;
        if (align == 2)
            return pos & (-2);

        return pos - (pos % align);
    }

    private long ComputeBytePosFromPeakFile(long samplePos)
    {
        var lookup = peakFile!.Value.samplePosToBytePos;
        var increment = peakFile!.Value.samplePosIncrement;

        // return (long)((newPos / (double)peakFile!.Value.length / 2) * input.Length);

        var lookupPos = samplePos / increment;
        var interp = samplePos & (increment - 1);

        var startPos = lookupPos == 0 ? 0 : lookup[Math.Clamp(lookupPos - 1, 0, lookup.Length - 1)];
        // var endPos = lookup[lookupPos];

        return startPos + (long)(bytesPerSample * interp);// (endPos - startPos)
    }
}
