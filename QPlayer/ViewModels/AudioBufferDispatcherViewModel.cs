using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Audio;
using System;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace QPlayer.ViewModels;

public class AudioBufferDispatcherViewModel : ObservableObject
{
    private readonly DispatcherTimer timer;
    private bool shouldUpdate = false;
    public ObservableArray<string> Workers { get; init; }
    public bool ShouldUpdate
    {
        get => shouldUpdate;
        set
        {
            shouldUpdate = value;
            if (value)
                timer.Start();
            else
                timer.Stop();
        }
    }

    public AudioBufferDispatcherViewModel()
    {
        Workers = new(AudioBufferingDispatcher.Default.ActiveWorkDebug);
        timer = new(DispatcherPriority.Input);
        timer.Tick += Timer_Tick;
        timer.Interval = TimeSpan.FromSeconds(1 / 60d);
        timer.Start();
    }

    private void Timer_Tick(object? sender, System.EventArgs e)
    {
        Workers.NotifyChange();
    }
}
