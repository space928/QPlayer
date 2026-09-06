using CommunityToolkit.Mvvm.ComponentModel;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Threading;

namespace QPlayer.Utilities;

/// <summary>
/// A throttle object allows an action to be throttled to a maximum rate using the dispatcher.
/// </summary>
public class Throttle
{
    private readonly Action onChanged;
    private readonly Dispatcher dispatcher;
    private readonly DispatcherTimer timer;
    private int requestCount;

    /// <summary>
    /// Constructs a new <see cref="Throttle"/> to run with the given minimum <paramref name="timeout"/> between invocations to the <paramref name="onChanged"/> action.
    /// </summary>
    /// <param name="timeout">The minimum amount of time to wait between invocations of the action.</param>
    /// <param name="onChanged">The action to invoke on the <see cref="Dispatcher"/> thread when <see cref="Invoke"/> is called.</param>
    /// <exception cref="Exception"></exception>
    public Throttle(TimeSpan timeout, Action onChanged)
    {
        requestCount = 0;
        this.onChanged = onChanged;
        dispatcher = Dispatcher.FromThread(Thread.CurrentThread) ?? throw new Exception("Must be called from a thread with an active dispatcher");

        timer = new(DispatcherPriority.Normal, dispatcher);
        timer.Interval = timeout;
        timer.Tick += Timer_Tick;
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        timer.Stop();
        if (requestCount <= 0)
            return;

        requestCount = 0;
        onChanged();
    }

    /// <summary>
    /// Tries to invoke this defined action. If this method has been called before the <c>timeout</c> between 
    /// the last call has elapsed, then a single call to the action will be scheduled for after the timeout.
    /// </summary>
    public void Invoke()
    {
        requestCount++;
        if (!timer.IsEnabled)
        {
            // Allow the callback to occur immeadiately if we're not already waiting for the timer.
            requestCount--;
            onChanged();
            timer.Start();
        }
    }
}

/// <summary>
/// A throttle object installs an event handler in the target <see cref="ObservableObject"/> and allows a 
/// callback to be invoked when the targetted property is changed while limiting the rate at which the 
/// callback can occur.
/// </summary>
public class PropThrottle : Throttle
{
    private readonly ObservableObject target;
    private readonly string prop;

    public PropThrottle(ObservableObject target, string prop, TimeSpan timeout, Action onChanged) : base(timeout, onChanged)
    {
        this.target = target;
        this.prop = prop;

        target.PropertyChanged += Target_PropertyChanged;
    }

    public void Dispose()
    {
        target.PropertyChanged -= Target_PropertyChanged;
    }

    private void Target_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != prop)
            return;

        Invoke();
    }
}

