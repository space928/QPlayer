using NAudio.Wave;
using System;

namespace QPlayer.Audio;

public class LoopingSampleProvider<T> : WaveStream, ISampleProvider where T : WaveStream, ISampleProvider
{
    private readonly T input;
    private readonly bool infinite;
    private readonly int loops;
    private long playedLoops = 0;

    public LoopingSampleProvider(T input, bool infinite = true, int loops = 1)
    {
        this.input = input;
        this.infinite = infinite;
        this.loops = loops;
    }

    public override WaveFormat WaveFormat => input.WaveFormat;

    public override long Length => infinite ? long.MaxValue / 32 : input.Length * loops;

    public override long Position
    {
        get => input.Position;
        set
        {
            input.Position = value % input.Length;
            playedLoops = value / input.Length;
        }
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int samplesRead = 0;
        int readOffset = offset;
        while (samplesRead < count)
        {
            int read = input.Read(buffer, readOffset, count - samplesRead);
            readOffset += read;
            if (read == 0)
            {
                input.Position = 0;
                readOffset = 0;
                playedLoops++;

                if (!infinite && playedLoops == loops)
                    break;
            }
        }
        return samplesRead;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int samplesRead = 0;
        int readOffset = offset;
        while (samplesRead < count)
        {
            int read = input.Read(buffer, readOffset, count - samplesRead);
            readOffset += read;
            if (read == 0)
            {
                input.Position = 0;
                readOffset = 0;
                playedLoops++;

                if (!infinite && playedLoops == loops)
                    break;
            }
        }
        return samplesRead;
    }
}
