using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace QPlayer.Utilities;

/// <summary>
/// A simple delay, which allows an action to be invoked on the dispatcher thread after some interval.
/// </summary>
public class DispatcherDelay : IDisposable
{
    private readonly DispatcherPriority priority;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private readonly Action action;
    private TimeSpan delay;

    /// <summary>
    /// The current configured delay from calling <see cref="Start()"/> after which the action will be invoked.
    /// </summary>
    public TimeSpan Delay
    {
        get => delay;
        set
        {
            delay = value;
            timer.Interval = value;
        }
    }

    public DispatcherDelay(Action action, DispatcherPriority priority = DispatcherPriority.Normal, Dispatcher? dispatcher = null)
    {
        this.action = action;
        this.priority = priority;
        this.dispatcher = dispatcher ?? Dispatcher.CurrentDispatcher;
        timer = new DispatcherTimer(priority, this.dispatcher);
        timer.Tick += Tick;
    }

    private void Tick(object? sender, EventArgs args)
    {
        action();
        timer.Stop();
    }

    /// <summary>
    /// Invokes the configured action after the given delay. Updates the <see cref="Delay"/> property.
    /// </summary>
    /// <param name="delay"></param>
    public void Start(TimeSpan delay)
    {
        Delay = delay;
        Start();
    }

    /// <summary>
    /// Invokes the configured action after the last configured delay. If the delay is 
    /// <see cref="TimeSpan.Zero"/>, then the action is invoked immediately.
    /// </summary>
    public void Start()
    {
        if (delay == TimeSpan.Zero)
        {
            if (priority >= DispatcherPriority.Normal && Thread.CurrentThread == dispatcher.Thread)
                action();
            else
                timer.Dispatcher.BeginInvoke(action, priority);
            return;
        }

        timer.Start();
    }

    /// <summary>
    /// Cancels the active delay.
    /// </summary>
    public void Cancel()
    {
        timer.Stop();
    }

    /// <summary>
    /// Cancels the active delay and disposes of this object.
    /// </summary>
    public void Dispose()
    {
        timer.Stop();
        timer.Tick -= Tick;
    }
}
