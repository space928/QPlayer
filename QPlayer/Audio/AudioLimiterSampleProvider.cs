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
    private readonly DelayBuffer delay;
    private readonly int channelCount;
    private readonly int fs;
    private readonly float[] envBuff;
    private readonly float[] compressBuff;
    private readonly EQSampleProvider.FilterCoeffs emphasisFilt;
    private readonly EQSampleProvider.FilterCoeffs compLPF;
    private EQSampleProvider.FilterCoeffs smootherLPF;
    private int attack = 1;
    private int release = 1;
    private int hold = 1;
    private float threshold;

#if DEBUG && false
    private readonly WaveFileWriter waveWriter;
    private bool wasWriting;
#endif

    // State
    private float minHoldVal = 1;
    private int minHoldTime = int.MaxValue;
    private float relLastMin = 0;
    private float relLastRes = 1;
    private int meterSampleCount;
    private float meterLastG = 1;
    private readonly double[] emphHist = new double[16];
    private readonly double[] compEqHist = new double[16];
    private readonly double[] smoothEqHist = new double[16];

    const int BLOCK_SIZE = 4096;
    const int MAX_LOOKAHEAD = 1024 * 32; // Roughly 0.4 s at 48Khz stereo

    public float AttackTime
    {
        get => attack / (float)(fs * channelCount);
        set
        {
            attack = Math.Clamp((int)(value * fs) * channelCount, 5 * channelCount, MAX_LOOKAHEAD);
            UpdateSmootherCoeffs(attack);
        }
    }

    public float ReleaseTime
    {
        get => release / (float)(fs * channelCount);
        set => release = Math.Max(5 * channelCount, (int)(value * fs) * channelCount);
    }

    public float Threshold
    {
        get => threshold;
        set => threshold = value;
    }

    public bool Enabled
    {
        get => field;
        set
        {
            if (field != value)
                Reset();
            field = value;
        }
    } = true;
    public float InputGain { get; set; } = 1;

    public float Hold
    {
        get => hold / (float)(fs * channelCount);
        set => hold = Math.Max(5, (int)(value * (fs * channelCount)));
    }
    public float CompRatio { get; set; }
    public float CompGain { get; set; }

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
    public event Action<float, float>? OnMeter;

    public bool WriteWave { get; set; }

    public AudioLimiterSampleProvider(ISamplePositionProvider source)
    {
        this.source = source;
        this.channelCount = source.WaveFormat.Channels;
        this.fs = source.WaveFormat.SampleRate;
        envBuff = new float[BLOCK_SIZE];
        compressBuff = new float[BLOCK_SIZE];
        delay = new(MAX_LOOKAHEAD);
        emphasisFilt = EQSampleProvider.CalculateFilterCoefficients(new Models.EQBand()
        {
            freq = 1200,
            gain = 6,
            q = 0.4f,
            shape = Models.EQBandShape.Bell
        }, WaveFormat.SampleRate);
        compLPF = EQSampleProvider.CalculateFilterCoefficients(new Models.EQBand()
        {
            freq = 12,
            gain = 0,
            q = 0.9f,
            shape = Models.EQBandShape.LowPass
        }, WaveFormat.SampleRate);
        UpdateSmootherCoeffs(attack);

#if DEBUG && false
        waveWriter = new($"qplayer-test-lim-{GetHashCode()}.wav", new(48000, 2));
#endif
    }

    public void Reset()
    {
        minHoldVal = 1;
        minHoldTime = int.MaxValue;
        relLastMin = 0;
        relLastRes = 1;
        meterSampleCount = 0;
        meterLastG = 1;
        emphHist.AsSpan().Clear();
        compEqHist.AsSpan().Clear();
        smoothEqHist.AsSpan().Clear();
        delay.Clear();
        OnMeter?.Invoke(0, 0);
        UpdateSmootherCoeffs(attack);
    }

    private void UpdateSmootherCoeffs(int attack)
    {
        int fs = WaveFormat.SampleRate * channelCount;
        float t = MathF.Max(attack / (float)fs, 1e-6f);
        smootherLPF = EQSampleProvider.CalculateFilterCoefficients(new Models.EQBand()
        {
            freq = 0.7f / t,
            gain = 0,
            q = 0.7f,
            shape = Models.EQBandShape.LowPass
        }, fs);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (!Enabled)
        {
            return source.Read(buffer, offset, count);
        }

        count = Math.Min(Math.Min(count, BLOCK_SIZE), MAX_LOOKAHEAD);
        int read = source.Read(envBuff, 0, count);
        var envSpan = envBuff.AsSpan(0, read);
        var compSpan = compressBuff.AsSpan(0, read);

        VectorExtensions.Multiply(envSpan, InputGain);
        delay.Push(envSpan);

        int _attack = attack; // Math.Clamp((int)(AttackTime * (1 + CrestFactor * lastCrest) * fs) * channelCount, 5 * channelCount, MAX_LOOKAHEAD);
        int _release = Math.Max(release, _attack);

        // Smooth compression
        delay.GetDelayed(compSpan, _attack);
        EQSampleProvider.ApplyFilter(emphHist, 0, channelCount, emphasisFilt, compSpan);
        VectorExtensions.SoftGR(compSpan, threshold, CompRatio, 0.5f, CompGain);
        EQSampleProvider.ApplyFilter(compEqHist, 0, channelCount, compLPF, compSpan);

        // Peak EQ to emphasise perceptually loudest frequencies
        // EQSampleProvider.ApplyFilter(emphHist, 0, channelCount, emphasisFilt, envSpan(0, read));

        // Clip
        VectorExtensions.ClipGR(envSpan, threshold);

        // Min hold
        int holdTime = Math.Max(_attack, hold);
        float decayRate = MathF.PI / (float)holdTime * 0.2f; // 0.15f seems good
        float minVal = minHoldVal;
        int minTime = minHoldTime;
        for (int i = 0; i < read; i++)
        {
            float v = envSpan[i];
            if (v < minVal || minTime >= holdTime)
            {
                minTime = 0;
                minVal = v;
            }
            else
            {
                minTime++;
                // Cosine shaped decay on the hold to prevent snapping
                float d = MathF.Cos(minTime * decayRate);
                minVal = minVal * d - d + 1;
                envSpan[i] = minVal;
            }
        }
        minHoldVal = minVal;
        minHoldTime = minTime;

        // Exponential release stage
        float gradientFactor = 1f / (_release + 1);
        // releaseDelay.Push(envSpan(0, read));
        float last = relLastMin;
        float output = relLastRes;
        for (int i = 0; i < read; i++)
        {
            float min = envSpan[i];
            output += (min - output) * gradientFactor;
            output = MathF.Min(output, min);
            last = min;
            envSpan[i] = output;
        }
        relLastMin = last;
        relLastRes = output;

        // Smoothing - Exponential/Constant rate hybrid
        /*float rate = 5 / (float)_attack;
        float v1 = smoothV1;
        float v2 = smoothV2;
        for (int i = 0; i < read; i++)
        {
            var v = envSpan[i];
            v1 = Math.Max(v, v1 + (v - 1) * rate);
            var n = v2 + (v1 - v2) * rate * 3;
            v2 = Math.Clamp(n, v1, 1);
            envSpan[i] = v2;
        }
        smoothV1 = v1;
        smoothV2 = v2;*/
        EQSampleProvider.ApplyFilter(smoothEqHist, 0, channelCount, smootherLPF, envSpan);

        // Combine with compressor
        float combineSmoothing = 0;// 0.1f;
        if (combineSmoothing <= 1e-8f)
            VectorExtensions.Min(envSpan, compSpan);
        else
            VectorExtensions.SmoothMin(envSpan, compSpan, combineSmoothing);

        // TODO: > 2 channel support?
        // TODO: Partial linking can sound better
        if (channelCount == 2)
        {
            // Link stereo limiting by computing the min per 2-samples
            VectorExtensions.StereoMin(envSpan);
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

        var delayed = delay.GetDelayed(buffer.AsSpan(offset, read), attack);
        VectorExtensions.MulClip(delayed, envSpan, threshold);

#if DEBUG && false
        if (WriteWave)
        {
            // Spit out GR into right channel
            for (int i = 1; i < read; i += 2)
                delayed[i] = envSpan[i];
            waveWriter.WriteSamples(buffer, offset, read);
            wasWriting = true;
        }
        else if (wasWriting)
        {
            wasWriting = false;
            waveWriter.Flush();
        }
#endif

        return read;

        void NotifySample(float meterMin)
        {
            float gr = 1 - meterMin;
            OnMeter?.Invoke(gr, gr >= compressBuff[0] ? compressBuff[0] : 1);
            //OnMeter?.Invoke(gr, lastCrest);
            //OnMeter?.Invoke(lastCrest*10);
            //OnMeter?.Invoke(_attack / (float)(fs * channelCount));
            meterSampleCount = 0;
        }
    }
}
