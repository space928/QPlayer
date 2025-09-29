using QPlayer.Audio;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using System;
using System.Windows.Threading;

namespace QPlayer.ViewModels;

public class AudioMeterViewModel : ReactiveObject
{
    [Reactive] public float PeakL { get; set; }
    [Reactive] public float PeakR { get; set; }
    [Reactive] public float RMSL { get; set; }
    [Reactive] public float RMSR { get; set; }

    private readonly Dispatcher dispatcher;
    private DispatcherOperation? prevOperation;

    public AudioMeterViewModel(Dispatcher dispatcher)
    {
        this.dispatcher = dispatcher;
        PeakL = -99;
        PeakR = -99;
        RMSL = -99;
        RMSR = -99;
    }

    public void ProcessSample(MeteringEvent meter)
    {
        var prev = prevOperation;
        if (prev != null && (prev.Status == DispatcherOperationStatus.Executing || prev.Status == DispatcherOperationStatus.Pending))
            return;

        prevOperation = dispatcher.InvokeAsync(() =>
        {
            PeakL = ComputeMeter(PeakL, ref meter.peakL);
            PeakR = ComputeMeter(PeakR, ref meter.peakR);
            RMSL = ComputeMeter(RMSL, ref meter.rmsL);
            RMSR = ComputeMeter(RMSR, ref meter.rmsR);
        }, DispatcherPriority.Input);

        static float ComputeMeter(float prev, ref float next)
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