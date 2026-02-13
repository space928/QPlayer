using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading;

namespace QPlayer.Audio;

public class AudioLimiterSampleProvider : ISamplePositionProvider
{
    private readonly ISamplePositionProvider source;
    private readonly DelaySampleProvider delay;
    private readonly int channelCount;
    private readonly int fs;
    private readonly float[] envBuff;
    private int attack = 1;
    private int release = 1;
    private float threshold;
    private readonly Lock lockObj;

    // State
    private float minHoldVal = 1;
    private int minHoldTime = int.MaxValue;
    private float relLastMin = 0;
    private float relLastRes = 1;
    private float smoothV1 = 1;
    private float smoothV2 = 1;
    private int meterSampleCount;
    private float meterLastG = 1;

    const int BLOCK_SIZE = 1024;
    const int MAX_ATTACK = 4096;

    public float AttackTime
    {
        get => attack / (float)(fs * channelCount);
        set => attack = Math.Clamp((int)(value * (fs * channelCount)), 5, MAX_ATTACK);
    }

    public float ReleaseTime
    {
        get => release / (float)(fs * channelCount);
        set => release = Math.Max(5, (int)(value * (fs * channelCount)));
    }

    public float Threshold
    {
        get => threshold;
        set => threshold = value;
    }

    public bool Enabled { get; set; } = true;
    public float InputGain { get; set; } = 1;

    public long Position
    {
        get => Math.Max(0, source.Position - attack);
        set => source.Position = Math.Max(0, source.Position - attack);
    }

    public WaveFormat WaveFormat => source.WaveFormat;

    /// <summary>
    /// The number of samples to read between each metering event.
    /// </summary>
    public int SamplesPerNotification { get; set; }

    /// <summary>
    /// An event raised every <see cref="SamplesPerNotification"/> samples with metering information.
    /// </summary>
    public event Action<float>? OnMeter;

    public AudioLimiterSampleProvider(ISamplePositionProvider source)
    {
        this.source = source;
        this.channelCount = source.WaveFormat.Channels;
        this.fs = source.WaveFormat.SampleRate;
        lockObj = new();
        envBuff = new float[BLOCK_SIZE];
        delay = new(source, MAX_ATTACK);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!Enabled)
        {
            return source.Read(buffer, offset, count);
        }

        using var _ = lockObj.EnterScope();

        count = Math.Min(count, BLOCK_SIZE);
        int read = delay.Read(envBuff, 0, count);

        // Clip
        VectorExtensions.ClipGR(envBuff.AsSpan(0, read), InputGain, threshold);

        // Moving min --> vey slow, replaced with min-hold
        /*movingMinDelay.Push(envBuff.AsSpan(0, read));
        for (int i = 0; i < read; i++)
        {
            var first = movingMinDelay.GetDelayed(attack, read - i, out var second);
            float min = VectorExtensions.Min(first);
            if (second.Length > 0)
                min = MathF.Min(min, VectorExtensions.Min(second));
            envBuff[i] = min;
        }*/

        // Min hold
        float minVal = minHoldVal;
        int minTime = minHoldTime;
        for (int i = 0; i < read; i++)
        {
            float v = envBuff[i];
            if (v < minVal || minTime >= attack)
            {
                minTime = 0;
                minVal = v;
            }
            else
            {
                minTime++;
                envBuff[i] = v;
            }
        }
        minHoldVal = minVal;
        minHoldTime = minTime;

        // Exponential release stage
        float gradientFactor = 1f / (release + 1);
        // releaseDelay.Push(envBuff.AsSpan(0, read));
        float last = relLastMin;
        float output = relLastRes;
        for (int i = 0; i < read; i++)
        {
            float min = envBuff[i];
            output += (min - output) * gradientFactor;
            output = MathF.Min(output, min);
            last = min;
            envBuff[i] = output;
        }
        relLastMin = last;
        relLastRes = output;

        // Smoothing - A weighted moving average (a convolution of a window function) --> tooo slowww
        /*smootherDelay.Push(envBuff.AsSpan(0, read));
        float recip = 1f/windowSum;
        for (int i = 0; i < read; i++)
        {
            var first = smootherDelay.GetDelayed(attack, read - i, out var second);
            float sum = VectorExtensions.Convolve(first, smootherWindow);
            if (second.Length > 0)
                sum += VectorExtensions.Convolve(second, smootherWindow.AsSpan(first.Length));
            envBuff[i] = sum * recip;
        }*/
        // Smoothing - Exponential/Constant rate hybrid
        float rate = 5 / (float)attack;
        float v1 = smoothV1;
        float v2 = smoothV2;
        for (int i = 0; i < read; i++)
        {
            var v = envBuff[i];
            v1 = Math.Max(v, v1 + (v - 1) * rate);
            var n = v2 + (v1 - v2) * rate * 3;
            v2 = Math.Clamp(n, v1, 1);
            envBuff[i] = v2;
        }
        smoothV1 = v1;
        smoothV2 = v2;

        // TODO: > 2 channel support?
        if (channelCount == 2)
        {
            // Link stereo limiting by computing the min per 2-samples
            VectorExtensions.StereoMin(envBuff);
        }

        int k = 0;
        float meterMin = meterLastG;
        while (k < read && SamplesPerNotification > 0)
        {
            int toTake = Math.Min(SamplesPerNotification - meterSampleCount, read - k);
            meterMin = MathF.Min(meterMin, VectorExtensions.Min(envBuff.AsSpan(k, toTake)));
            k += toTake;
            meterSampleCount += toTake;
            if (meterSampleCount >= SamplesPerNotification)
            {
                NotifySample(meterMin);//(1 / meterMin - 1);
                meterMin = 1;
            }
        }
        meterLastG = meterMin;

        if (MathF.Abs(envBuff[0]) > 1.5f)
            Debugger.Break();

        read = delay.ReadDelayed(buffer, offset, read, attack);
        VectorExtensions.Multiply(buffer.AsSpan(offset, read), envBuff.AsSpan(0, read));
        VectorExtensions.Multiply(buffer.AsSpan(offset, read), InputGain);
        return read;

        void NotifySample(float meterMin)
        {
            float gr = 1 - meterMin;
            OnMeter?.Invoke(gr);
            meterSampleCount = 0;
        }
    }

    /*[MemberNotNull(nameof(smootherWindow))]
    private void CreateSmootherWindow()
    {
        smootherWindow = new float[attack];
        // Blackmann window
        float alpha = 0.16f;
        float a0 = (1 - alpha) / 2;
        float a1 = 0.5f;
        float a2 = alpha / 2;
        windowSum = 0;
        for (int i = 0; i < smootherWindow.Length; i++)
        {
            float t = MathF.Tau * i / (float)smootherWindow.Length;
            float x = a0 - a1 * MathF.Cos(t) + a2 * MathF.Cos(2 * t);
            smootherWindow[i] = x;
            windowSum += x;
        }
    }*/
}
