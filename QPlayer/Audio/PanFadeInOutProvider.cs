using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace QPlayer.Audio;

public class PanFadeInOutProvider : ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private float volume = 1;
    private float pan = 0;

    public PanFadeInOutProvider(ISamplePositionProvider source, bool startSilent = false)
    {
        this.source = source;
        if (startSilent)
            volume = 0;
        else
            volume = 1;
    }

    public long Position
    {
        get => source.Position;
        set => source.Position = value;
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>
    /// The initial volume of the samples processed.
    /// </summary>
    public float Volume
    {
        get => volume;
        set => volume = value;
    }

    /// <summary>
    /// The stereo panning applied to the processed samples.
    /// </summary>
    public float Pan
    {
        get => pan;
        set => pan = value;
    }

    /// <summary>
    /// The fade in duration in samples. 
    /// <para/>
    /// Note that this is in mono samples so for a stereo source, this should be half the number of actual processed samples.
    /// </summary>
    public long FadeInDuration { get; set; }
    /// <summary>
    /// The fade out duration in samples. 
    /// <para/>
    /// Note that this is in mono samples so for a stereo source, this should be half the number of actual processed samples.
    /// </summary>
    public long FadeOutDuration { get; set; }
    /// <summary>
    /// The time in samples when the fade out should start. 
    /// <para/>
    /// Note that this is in mono samples so for a stereo source, this should be half the number of actual processed samples.
    /// </summary>
    public long FadeOutStartTime { get; set; }

    public FadeType FadeType { get; set; }

    public int Read(float[] buffer, int offset, int count)
    {
        int channels = source.WaveFormat.Channels;
        long fadePos = Position / channels;

        int numSource = source.Read(buffer, offset, count);

        int offsetSource = offset;
        int num = numSource;
        if (fadePos < FadeInDuration)
        {
            int numFaded = FadeSamples(buffer, offset, numSource, 0, volume, fadePos, FadeInDuration);
            offset += numFaded;
            num -= numFaded;
        }
        if (fadePos + numSource / channels >= FadeOutStartTime)
        {
            int numFaded = FadeSamples(buffer, offset, num, volume, 0, fadePos - FadeOutStartTime, FadeOutDuration);
            offset += numFaded;
            num -= numFaded;
        }

        // Apply pan if needed
        if (pan != 0 && source.WaveFormat.Channels == 2)
            VectorExtensions.ApplyPan(buffer.AsSpan(offsetSource, numSource), pan);

        // Fast paths for -inf gain and unity gain
        if (volume == 0)
        {
            buffer.AsSpan(offset, num).Clear();
            return numSource;
        }
        else if (volume == 1)
        {
            return numSource;
        }

        // Apply volume to any remaining samples, the common case.
        if (num > 0)
            VectorExtensions.Multiply(buffer.AsSpan(offset, num), volume);

        return numSource;
    }

    private int FadeSamples(float[] buffer, int offset, int count, float startGain, float endGain, long fadeTime, long fadeDuration)
    {
        int i = offset;
        int channels = source.WaveFormat.Channels;

        int toTake = Math.Min(count, (int)(fadeDuration - fadeTime) * channels);
        float delta = endGain - startGain;
        float rlen = 1f / (fadeDuration - 1);

        switch (FadeType)
        {
            case FadeType.Linear:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float frac = startGain + (Math.Max(0, fadeTime) * rlen) * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    fadeTime++;
                }
                break;
            case FadeType.Square:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float t = Math.Max(0, fadeTime) * rlen;
                    t *= t;
                    float frac = startGain + t * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    fadeTime++;
                }
                break;
            case FadeType.InverseSquare:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float t = Math.Max(0, fadeTime) * rlen;
                    t = MathF.Sqrt(t);
                    float frac = startGain + t * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    fadeTime++;
                }
                break;
            case FadeType.SCurve:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float t = Math.Max(0, fadeTime) * rlen;
                    float t2 = t * t;
                    float t3 = t2 * t;
                    t = -2 * t3 + 3 * t2;
                    float frac = startGain + t * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    fadeTime++;
                }
                break;
        }

        return i - offset;
    }
}
