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
    [Reactive, Knob, Range()] private float attack = 0.005f;
    [Reactive, Knob, Range()] private float release = 0.05f;

    [Reactive("GR"), ModelSkip] private float gr;

    private AudioLimiterSampleProvider? limiter;
    private ISamplePositionProvider? source;
    private readonly Dispatcher dispatcher;
    private DispatcherOperation? prevOperation;

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

        PropertyChanged += (o, e) => Configure();
    }

    private void Configure()
    {
        if (limiter == null)
            return;

        limiter.Enabled = enabled;
        limiter.InputGain = MathF.Pow(10, inputGain / 20f); ;
        limiter.AttackTime = attack;
        limiter.ReleaseTime = release;
        limiter.Threshold = MathF.Pow(10, threshold / 20f);
        limiter.SamplesPerNotification = limiter.WaveFormat.SampleRate / 30;
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

    public void ProcessSample(float gr)
    {
        var prev = prevOperation;
        if (prev != null && (prev.Status == DispatcherOperationStatus.Executing || prev.Status == DispatcherOperationStatus.Pending))
            return;

        prevOperation = dispatcher.InvokeAsync(() =>
        {
            GR = -LinToDb(1 - gr); // ComputeMeter(GR, gr);
        }, DispatcherPriority.Loaded);

        static float ComputeMeter(float prev, float next)
        {
            float x = prev;
            x = DbToLin(x);
            if (next > x)
                x = next;
            else
                x *= 0.96f;
            x = MathF.Min(MathF.Max(x, 1e-10f), 1);
            return LinToDb(x);
        }

        static float LinToDb(float x)
        {
            return 20 * MathF.Log10(x);
        }

        static float DbToLin(float x)
        {
            return MathF.Pow(10, x / 20);
        }
    }
}
