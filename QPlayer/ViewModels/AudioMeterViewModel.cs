using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Audio;
using QPlayer.SourceGenerator;
using System;
using System.Windows.Threading;

namespace QPlayer.ViewModels;

public partial class AudioMeterViewModel : ObservableObject
{
    [Reactive] private float peakL;
    [Reactive] private float peakR;
    [Reactive] private float rMSL;
    [Reactive] private float rMSR;

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
            PeakL = ComputeMeter(PeakL, meter.peakL);
            PeakR = ComputeMeter(PeakR, meter.peakR);
            RMSL = ComputeMeter(RMSL, meter.rmsL);
            RMSR = ComputeMeter(RMSR, meter.rmsR);
        }, DispatcherPriority.Input);

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