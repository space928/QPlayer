using ColorPicker.Models;
using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels;

/// <summary>
/// The current state of the Cue's playback.
/// 
/// Legal state transitions are as follows:
/// 
///   /----<---\--<---\-----<----\
///   |        |/--> Paused <--\ |
/// Ready --> Delay --> Playing/PlayingLooped
///      \------>------/ 
/// </summary>
public enum CueState
{
    /// <summary>
    /// The Cue is currently stopped and ready to be played
    /// </summary>
    Ready,
    /// <summary>
    /// The Cue is currently waiting to start.
    /// </summary>
    Delay,
    Playing,
    PlayingLooped,
    Paused,
}

public abstract partial class CueViewModel : BindableViewModel<Cue>
{
    #region Bindable Properties
    [Reactive("QID"), TemplateProp(nameof(QID_Template))]
    protected decimal qid;
    private decimal QID_Template
    {
        get => qid;
        set
        {
            mainViewModel?.NotifyQIDChanged(qid, value, this);
            qid = value;
        }
    }
    [Reactive, ModelBindsTo(nameof(Cue.parent))] private decimal? parentId;
    public CueViewModel? Parent
    {
        get
        {
            if (parentId == null)
                return null;
            if (mainViewModel?.FindCue(parentId.Value, out var parent) ?? false)
                return parent;
            return null;
        }
    }
    [Reactive, ModelCustomBinding(nameof(VM2M_Colour), nameof(M2VM_Colour)), ChangesProp(nameof(ColourBrush)), SkipEqualityCheck]
    private ColorState colour;
    [Reactive] private string name = string.Empty;
    [Reactive] private string description = string.Empty;
    [Reactive] private string remoteNode = string.Empty;
    [Reactive] private TriggerMode trigger;
    [Reactive] private bool enabled = true;
    [Reactive] private TimeSpan delay;
    [Reactive, CustomAccessibility("public virtual"), ModelSkip, SkipEqualityCheck, NoUndo] private TimeSpan duration;
    [Reactive, ChangesProp(nameof(UseLoopCount))] private LoopMode loopMode;
    [Reactive] public int loopCount;

    [Reactive, Readonly, ModelSkip] protected MainViewModel? mainViewModel;
    public bool IsSelected => mainViewModel?.SelectedCue == this;
    public bool IsMultiSelected => mainViewModel?.MultiSelection?.Contains(this) ?? false;
    [Reactive, ModelSkip, NoUndo] private CueState state;
    [Reactive, CustomAccessibility("public virtual"), SkipEqualityCheck, ModelSkip, NoUndo]
    private TimeSpan playbackTime;
    public bool UseLoopCount => LoopMode == LoopMode.Looped || LoopMode == LoopMode.LoopedInfinite;

    public SolidColorBrush ColourBrush
    {
        get
        {
            colourBrush.Color = Colour.ToMediaColor(127);
            return colourBrush;
        }
    }
    public string TypeName => typeName;
    public string TypeDisplayName => typeDisplayName;

    [Reactive, Readonly, ModelSkip] private RelayCommand goCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand pauseCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand stopCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand selectCommand;
    [Reactive, Readonly, ModelSkip] private static ObservableCollection<string>? loopModeVals;
    [Reactive, Readonly, ModelSkip] private static ObservableCollection<StopMode>? stopModeVals;
    [Reactive, Readonly, ModelSkip] private static ObservableCollection<FadeType>? fadeTypeVals;
    [Reactive, Readonly, ModelSkip] private static ObservableCollection<string>? triggerModeVals;

    public bool IsRemoteControlling => (mainViewModel?.ProjectSettings?.EnableRemoteControl ?? false)
        && !string.IsNullOrEmpty(RemoteNode) && RemoteNode != mainViewModel.ProjectSettings.NodeName;

    /// <summary>
    /// The duration of this cue, as received from a remote node.
    /// </summary>
    public virtual TimeSpan RemoteDuration { set { } }
    #endregion

    public event EventHandler? OnCompleted;

    protected SynchronizationContext? synchronizationContext;
    protected CueViewModel? parent;
    protected DispatcherDelay goDelay;
    private readonly SolidColorBrush colourBrush;
    private CueViewModel? waitCue;
    private readonly string typeName;
    private readonly string typeDisplayName;

    public CueViewModel(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        colourBrush = new(Colour.ToMediaColor(127));
        synchronizationContext = SynchronizationContext.Current;

        if (CueFactory.ViewModelToCueType.TryGetValue(GetType(), out var registered))
        {
            typeName = registered.name;
            typeDisplayName = registered.displayName;
        }
        else
        {
            typeName = GetType().Name;
            typeDisplayName = typeName;
        }

        goDelay = new(Go);

        goCommand = new(Go);
        pauseCommand = new(Pause);
        stopCommand = new(Stop);
        selectCommand = new(SelectExecute);

        LoopModeVals ??= new ObservableCollection<string>(Enum.GetValues<LoopMode>().Select(x => EnumToString(x)));
        StopModeVals ??= new ObservableCollection<StopMode>(Enum.GetValues<StopMode>());
        FadeTypeVals ??= new ObservableCollection<FadeType>(Enum.GetValues<FadeType>());
        TriggerModeVals ??= new ObservableCollection<string>(Enum.GetValues<TriggerMode>().Select(x => EnumToString(x)));
    }

    /// <summary>
    /// This method is invoked by QPlayer when this cue is selected in the inspector.
    /// </summary>
    internal virtual void OnFocussed()
    {

    }

    internal void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(IsSelected));
        OnPropertyChanged(nameof(IsMultiSelected));
    }

    #region Command Handlers
    /// <summary>
    /// Starts this cues after it's delay has elapsed.
    /// </summary>
    /// <param name="waitForCue">Optionally, a cue to wait for it's <see cref="OnCompleted"/> event before starting this cue.</param>
    public virtual void DelayedGo(CueViewModel? waitForCue = null)
    {
        PluginLoader.OnGo(this);

        if (waitForCue != null && waitForCue.Duration != TimeSpan.Zero)
        {
            State = CueState.Delay;
            // Unregister the previous waiter, if it's still set
            waitCue?.OnCompleted -= WaitCueOnCompleteHandler;
            // Start waiting for this cue to complete
            waitCue = waitForCue;
            waitForCue.OnCompleted += WaitCueOnCompleteHandler;
            return;
        }

        if (Delay == TimeSpan.Zero)
        {
            Go();
            return;
        }

        State = CueState.Delay;
        goDelay.Start(Delay);

        if (!mainViewModel?.ActiveCues?.Contains(this) ?? false)
            mainViewModel?.ActiveCues.Add(this);
    }

    private void WaitCueOnCompleteHandler(object? sender, EventArgs args)
    {
        if (sender is not CueViewModel waitCue)
            return; // Should never happen...

        waitCue.OnCompleted -= WaitCueOnCompleteHandler;
        this.waitCue = null;

        DelayedGo();
    }

    /// <summary>
    /// Starts this cue immediately.
    /// </summary>
    public virtual void Go()
    {
        if (IsRemoteControlling)
            mainViewModel?.OSCManager.SendRemoteGo(RemoteNode, QID);

        if (Duration == TimeSpan.Zero)
        {
            OnCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }
        State = CueState.Playing;
        if (!mainViewModel?.ActiveCues?.Contains(this) ?? false)
            mainViewModel?.ActiveCues.Add(this);
    }

    /// <summary>
    /// Pauses this cue. It can be resumed again by calling Go.
    /// 
    /// Not all cues support pausing. For unsupported cues, this should Stop().
    /// </summary>
    public virtual void Pause()
    {
        goDelay.Cancel();
        State = CueState.Paused;

        if (IsRemoteControlling)
            mainViewModel?.OSCManager.SendRemotePause(RemoteNode, qid);
    }

    /// <summary>
    /// Stops this cue immediately.
    /// </summary>
    public virtual void Stop()
    {
        StopInternal();

        if (IsRemoteControlling)
            mainViewModel?.OSCManager.SendRemoteStop(RemoteNode, qid);
    }

    /// <summary>
    /// Fades out the current cue.
    /// </summary>
    /// <param name="duration">The duration in seconds to fade over.</param>
    /// <param name="fadeType">The type of fade to use.</param>
    public virtual void FadeOutAndStop(float duration, FadeType? fadeType = null)
    {
        Stop();
    }

    /// <summary>
    /// Continues playing past the end of the loop marker until the end of this cue. 
    /// Optionally, starts a fade out at the end of loop.
    /// </summary>
    /// <param name="onDevampStart">An action to be invoked when the last loop ends.</param>
    /// <param name="fadeDuration">The length of the fadeout to start at the end of the 
    /// last loop. Specify <c>-1</c> to play without any fadeout, or <c>0</c> to instantly 
    /// stop the cue at the end of the loop.</param>
    /// <param name="fadeType">The type of fade to apply.</param>
    public virtual void DeVamp(Action? onDevampStart, float fadeDuration = -1, FadeType? fadeType = null)
    {
        onDevampStart?.Invoke();
        Stop();
    }

    /// <summary>
    /// Stops this cue without informing remote clients or executing cue specific stopping code.
    /// This is called when a remote node needs to tell us that a cue has finished playing.
    /// </summary>
    internal void StopInternal()
    {
        // This cue has been stopped/cancelled, stop waiting for the wait cue.
        waitCue?.OnCompleted -= WaitCueOnCompleteHandler;
        waitCue = null;
        goDelay.Cancel();
        State = CueState.Ready;
        mainViewModel?.ActiveCues.Remove(this);
        OnCompleted?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Sets the playback time of the cue to the given time, and puts it in the paused state.
    /// </summary>
    /// <param name="startTime">the time to start the cue at.</param>
    public virtual void Preload(TimeSpan startTime)
    {
        if (State == CueState.Ready || State == CueState.Paused)
        {
            PlaybackTime = startTime;
            State = CueState.Paused;

            if (IsRemoteControlling)
                mainViewModel?.OSCManager.SendRemotePreload(RemoteNode, qid, (float)startTime.TotalSeconds);
        }
    }

    /// <summary>
    /// Selects this cue, applying the current multi-selection modifiers.
    /// </summary>
    public void SelectExecute()
    {
        mainViewModel?.MultiSelect(this);
    }

    /// <summary>
    /// This callback is triggered every 50 or so ms when this cue is active by the main thread.
    /// </summary>
    protected internal virtual void UpdateUIStatus()
    {

    }
    #endregion

    private static void VM2M_Colour(CueViewModel vm, Cue m) => m.colour = (SerializedColour)vm.Colour;
    private static void M2VM_Colour(CueViewModel vm, Cue m) => vm.Colour = (ColorState)m.colour;

    public static string EnumToString<T>(T type) where T : Enum
    {
        StringBuilder sb = new(type.ToString());
        bool wasCapital = false;
        for (int i = 1; i < sb.Length; i++)
        {
            char c = sb[i];
            if (char.IsUpper(c))
            {
                if (!wasCapital)
                {
                    sb.Insert(i, ' ');
                    i++;
                }
                wasCapital = true;
            }
            else
            {
                wasCapital = false;
            }
        }
        return sb.ToString();
    }
}
