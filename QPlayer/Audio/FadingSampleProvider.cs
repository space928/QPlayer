using NAudio.Wave;
using QPlayer.Audio;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace QPlayer.Audio;

internal class FadingSampleProvider : ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private readonly Lock lockObj = new();
    private FadeState state;
    private long fadeTime;
    private long fadeDuration;
    private float startVolume = 1;
    private float endVolume = 1;
    private FadeType fadeType;
    private Action<bool>? onCompleteAction;
    private SynchronizationContext? synchronizationContext;

    public FadingSampleProvider(ISamplePositionProvider source, bool startSilent = false)
    {
        this.source = source;
        state = FadeState.Ready;
        if (startSilent)
            startVolume = 0;
        else
            startVolume = 1;
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
        get => startVolume;
        set => startVolume = value;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int numSource = source.Read(buffer, offset, count);
        int num = numSource;
        if (state == FadeState.Fading)
        {
            int numFaded = FadeSamples(buffer, offset, numSource);
            offset += numFaded;
            num -= numFaded;
        }

        // Fast paths for -inf gain and unity gain
        if (startVolume == 0)
        {
            buffer.AsSpan(offset, num).Clear();
            return numSource;
        }
        else if (startVolume == 1)
        {
            return numSource;
        }

        // Apply volume to any remaining samples, the common case.
        if (num > 0)
            VectorExtensions.Multiply(buffer.AsSpan(offset, num), startVolume);

        return numSource;
    }

    /// <summary>
    /// Starts a new fade operation, cancelling any active fade operation.
    /// </summary>
    /// <param name="volume">The volume to fade to</param>
    /// <param name="durationMS">The time to fade over in milliseconds</param>
    /// <param name="fadeType">The type of fade to use</param>
    /// <param name="onComplete">Optionally, an event to raise when the fade is completed. <c>true</c> is passed to the 
    /// event handler if the fade completed normally, <c>false</c> if it was cancelled. The event is invoked on the 
    /// thread that called this method.</param>
    /// <param name="useSyncContext">Whether the onComplete action should be invoked using the current thread's 
    /// synchronization context.</param>
    public void BeginFade(float volume, double durationMS, FadeType fadeType = FadeType.Linear, Action<bool>? onComplete = null, bool useSyncContext = true)
    {
        lock (lockObj)
        {
            EndFade();

            fadeTime = 0;
            fadeDuration = (int)(durationMS * source.WaveFormat.SampleRate * 1e-3);
            endVolume = volume;
            this.fadeType = fadeType;
            onCompleteAction = onComplete;
            synchronizationContext = useSyncContext ? SynchronizationContext.Current : null;
            state = FadeState.Fading;
        }
    }

    /// <summary>
    /// Cancels the active fade operation.
    /// </summary>
    public void EndFade()
    {
        if (state != FadeState.Fading)
            return;

        lock (lockObj)
        {
            state = FadeState.Ready;
            float t = GetFadeFraction(fadeTime / (float)fadeDuration, fadeType);
            startVolume = endVolume * t + startVolume * (1 - t);
            if (synchronizationContext != null)
                synchronizationContext.Post(x => onCompleteAction?.Invoke(false), null);
            else
                onCompleteAction?.Invoke(false);
            //onCompleteAction = null;
            //synchronizationContext = null;
        }
    }

    private int FadeSamples(float[] buffer, int offset, int count)
    {
        int i = offset;
        int channels = source.WaveFormat.Channels;
        long _fadeTime = fadeTime;
        long _fadeDuration = fadeDuration;
        FadeType _fadeType = fadeType;

        var fadeEnd = _fadeTime + _fadeDuration;
        float tStart = _fadeTime / (float)_fadeDuration;
        float tEnd = fadeEnd / (float)_fadeDuration;
        int toTake = Math.Min(count, (int)(_fadeDuration - _fadeTime) * channels);
        float startGain = startVolume;
        float delta = endVolume - startVolume;
        float rlen = 1f / _fadeDuration;

        switch (fadeType)
        {
            case FadeType.Linear:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float frac = startGain + (_fadeTime * rlen) * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    _fadeTime++;
                }
                break;
            case FadeType.Square:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float t = _fadeTime * rlen;
                    t *= t;
                    float frac = startGain + t * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    _fadeTime++;
                }
                break;
            case FadeType.InverseSquare:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float t = _fadeTime * rlen;
                    t = MathF.Sqrt(t);
                    float frac = startGain + t * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    _fadeTime++;
                }
                break;
            case FadeType.SCurve:
                for (i = offset; i < offset + toTake; i += channels)
                {
                    float t = _fadeTime * rlen;
                    float t2 = t * t;
                    float t3 = t2 * t;
                    t = -2 * t3 + 3 * t2;
                    float frac = startGain + t * delta;
                    for (int c = 0; c < channels; c++)
                        buffer[i + c] *= frac;
                    _fadeTime++;
                }
                break;
        }

        if (_fadeTime >= _fadeDuration - channels)
            FadeCompleted();

        fadeTime = _fadeTime;

        return i - offset;

        void FadeCompleted()
        {
            startVolume = endVolume;
            state = FadeState.Ready;
            if (synchronizationContext != null)
                synchronizationContext.Post(x => onCompleteAction?.Invoke(true), null);
            else
                onCompleteAction?.Invoke(true);
        }
    }

    private static float GetFadeFraction(float t, FadeType type)
    {
        switch (type)
        {
            case FadeType.SCurve:
                // Cubic hermite spline
                float t2 = t * t;
                float t3 = t2 * t;
                t = -2 * t3 + 3 * t2;
                break;
            case FadeType.Square:
                t *= t;
                break;
            case FadeType.InverseSquare:
                t = MathF.Sqrt(t);
                break;
            case FadeType.Linear:
            default:
                break;
        }
        return t;
    }
}

internal enum FadeState
{
    Ready,
    Fading
}

public enum FadeType
{
    Linear,
    SCurve,
    Square,
    InverseSquare,
}
