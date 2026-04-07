using CommunityToolkit.Mvvm.Input;
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
    // [Reactive, Knob, Range()] private float attack = 0.5f;
    [Reactive, Knob, Range()] private float release = 30f;
    [Reactive, ModelSkip] private float compRatio = 0.2f;
    [Reactive, ModelSkip] private float compGain = 1;
    [Reactive, ModelSkip] private bool write = false;

    [Reactive("GR"), ModelSkip] private float gr;
    [Reactive("DBG_Meter"), ModelSkip] private float dbg_meter;

    [Reactive] private readonly RelayCommand autoGainCommand;

    private readonly MainViewModel mainVM;
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

    public AudioLimiterViewModel(MainViewModel mainVM)
    {
        dispatcher = Dispatcher.CurrentDispatcher;
        this.mainVM = mainVM;
        autoGainCommand = new(() => AutoGain());

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

    /// <summary>
    /// Sets the <see cref="InputGain"/> to a level that ensures none of the sound cues in the 
    /// cue stack will exceed 0dBfs when playing individually.
    /// </summary>
    /// <param name="headroomDb">The amount of headroom to leave in dB from full-scale, positive 
    /// values attenuate, negative values will push cues into the limiter.</param>
    public void AutoGain(float headroomDb = 0)
    {
        float peak = float.MinValue;
        foreach (var cue in mainVM.Cues)
        {
            if (cue is not SoundCueViewModel scue)
                continue;

            if (scue.WaveForm.PeakFile is not PeakFile peakFile)
                continue;

            peak = MathF.Max(peak, peakFile.peak * DbToLin(scue.Volume));
            // Note that this doesn't take EQ gain into consideration, but this can probably just be caught by the limiter anyway so it's no big deal.
        }

        // Convert to gain in dB needed to attenuate the maximum peak level, and adjus the gain to give the requested headroom.
        float gain = -LinToDb(peak);
        gain -= headroomDb;

        InputGain = gain;
    }

    private static float LinToDb(float x) => 20 * MathF.Log10(x);
    private static float DbToLin(float x) => MathF.Pow(10, x / 20);

    private void Configure()
    {
        if (limiter == null)
            return;

        limiter.Enabled = enabled;
        limiter.InputGain = MathF.Pow(10, inputGain / 20f);
        limiter.AttackTime = release * 0.001f / 30;
        limiter.ReleaseTime = release * 0.001f;
        limiter.Threshold = MathF.Pow(10, threshold / 20f);
        limiter.SamplesPerNotification = limiter.WaveFormat.SampleRate / 30;

        limiter.Hold = release * 0.001f / 6;
        limiter.CompRatio = 0.75f;
        limiter.CompGain = compGain;
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
