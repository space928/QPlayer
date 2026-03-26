using NAudio.Wave;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Threading;

namespace QPlayer.ViewModels;

[Model(typeof(AudioLimiterSettings))]
[View(typeof(AudioLimiterControl))]
public partial class AudioLimiterViewModel : BindableViewModel<AudioLimiterSettings>
{
    [Reactive] private bool enabled;
    [Reactive, Knob, Range()] private float inputGain = 0f;
    [Reactive, Knob, Range()] private float threshold = -1.5f;
    [Reactive, Knob, Range()] private float attack = 0.5f;
    [Reactive, Knob, Range()] private float release = 30f;
    [Reactive, ModelSkip] private float crestFactor = 0;
    [Reactive, ModelSkip] private float hold = 0;
    [Reactive, ModelSkip] private float tilt = 0;
    [Reactive, ModelSkip] private float dbg = 1;
    [Reactive, ModelSkip] private float eq = 1;
    [Reactive, ModelSkip] private bool write = false;

    [Reactive("GR"), ModelSkip] private float gr;
    [Reactive("DBG_Meter"), ModelSkip] private float dbg_meter;

    private AudioLimiterSampleProvider? limiter;
    private ISamplePositionProvider? source;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private MeterEvent meterEventA;
    private MeterEvent meterEventB;
    private bool swap;

    public ISamplePositionProvider? InputSampleProvider
    {
        get => source;
        set
        {
            source = value;
            if (value != null)
            {
                limiter = new(value);
                limiter.OnMeter += ProcessSample;
                Configure();
            }
            else
            {
                limiter = null;
            }
        }
    }
    public AudioLimiterSampleProvider? LimiterSampleProvider => limiter;

    public AudioLimiterViewModel()
    {
        dispatcher = Dispatcher.CurrentDispatcher;

        PropertyChanged += (o, e) =>
        {
            if (e.PropertyName != nameof(GR) && e.PropertyName != nameof(DBG_Meter))
                Configure();
        };

        timer = new(DispatcherPriority.Loaded, dispatcher);
        timer.Tick += Timer_Tick;
        timer.Interval = TimeSpan.FromMilliseconds(20);
        timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        MeterEvent meter;
        if (swap)
            meter = meterEventB;
        else
            meter = meterEventA;

        GR = -LinToDb(1 - meter.gr); // ComputeMeter(GR, gr);
        DBG_Meter = LinToDb(meter.dbg);
    }

    public void ProcessSample(float gr, float dbg_m)
    {
        if (swap)
            meterEventA = new(gr, dbg_m);
        else
            meterEventB = new(gr, dbg_m);
        swap ^= true;
        return;
    }

    static float LinToDb(float x)
    {
        return 20 * MathF.Log10(x);
    }

    private void Configure()
    {
        if (limiter == null)
            return;

        limiter.Enabled = enabled;
        limiter.InputGain = MathF.Pow(10, inputGain / 20f);
        limiter.AttackTime = release * 0.001f / 20;
        limiter.ReleaseTime = release * 0.001f;
        limiter.Threshold = MathF.Pow(10, threshold / 20f);
        limiter.SamplesPerNotification = limiter.WaveFormat.SampleRate / 30;

        limiter.CrestFactor = crestFactor;
        limiter.Hold = release * 0.001f / 6;
        limiter.Tilt = tilt;
        limiter.Dbg = dbg;
        limiter.Eq = eq;
        limiter.WriteWave = write;
    }

    public override void SyncToModel()
    {
        base.SyncToModel();

        Configure();
    }

    public override void SyncFromModel()
    {
        base.SyncFromModel();

        Configure();
    }

    private readonly struct MeterEvent(float gr, float dbg)
    {
        public readonly float gr = gr;
        public readonly float dbg = dbg;
    }
}
