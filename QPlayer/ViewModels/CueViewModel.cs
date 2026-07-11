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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;
using System.Windows.Threading;
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
    /// <summary>
    /// A number uniquely identifying this cue. This number is used when referencing this cue in the UI or in OSC commands.
    /// We don't strictly enforce QID order or uniqueness for convenience. Note that this property only stores the last 
    /// part of the QID for cues belonging to a group. To get the full QID, use <see cref="FullQID"/> which prepends the 
    /// QIDs of the parents of this cue.
    /// </summary>
    [Reactive("QID"), TemplateProp(nameof(QID_Template))]
    protected decimal qid;
    private decimal QID_Template
    {
        get => qid;
        set
        {
            qid = value;
            UpdateFullQID();
        }
    }
    /// <summary>
    /// The QID of the direct ancestor of this cue (ie: the group cue this cue belongs to) or 
    /// <see langword="null"/> if this cue is not part of a group.
    /// </summary>
    public string? ParentId => parent?.fullQID;
    private CueViewModel? parent;
    /// <summary>
    /// The direct ancestor of this cue (ie: the group cue this cue belongs to) or 
    /// <see langword="null"/> if this cue is not part of a group.
    /// </summary>
    [Reactive("Parent"), ChangesProp(nameof(ParentId)), ModelCustomBinding(nameof(VM2M_Parent), nameof(M2VM_Parent))] 
    private CueViewModel? Parent_Template
    {
        get => parent;
        set
        {
            parent = value;
            UpdateFullQID();
        }
    }
    private string fullQID = string.Empty;
    /// <summary>
    /// The full hierarchical QID of this cue. This is a concatenation of the QIDs of this cue and 
    /// it's ancestors (separated by <c>-</c>). This is used to uniquely identify this cue when 
    /// referencing it in the UI. See <see cref="QID"/> to update this cue's QID.
    /// </summary>
    public string FullQID => fullQID;
    /// <summary>
    /// A colour used to identify this cue in the UI.
    /// </summary>
    [Reactive, ModelCustomBinding(nameof(VM2M_Colour), nameof(M2VM_Colour)), ChangesProp(nameof(ColourBrush)), SkipEqualityCheck]
    private ColorState colour;
    /// <summary>
    /// A short descriptive name for this cue. 
    /// </summary>
    [Reactive, ChangesProp(nameof(NamePreview))] private string name = string.Empty;
    /// <summary>
    /// The <see cref="Name"/> of this cue or some default name if one hasn't been specified yet.
    /// </summary>
    public virtual string NamePreview => name;
    /// <summary>
    /// A longer description of this cue.
    /// </summary>
    [Reactive] private string description = string.Empty;
    /// <summary>
    /// The name of the remote node this cue should control.
    /// </summary>
    [Reactive] private string remoteNode = string.Empty;
    /// <summary>
    /// How this cue should be triggered in the cue stack.
    /// </summary>
    [Reactive] private TriggerMode trigger;
    /// <summary>
    /// Whether this cue is enabled in the cue stack. When disabled, it will be skipped when pressing GO.
    /// </summary>
    [Reactive] private bool enabled = true;
    /// <summary>
    /// A time delay before actually running this cue when it's triggered.
    /// </summary>
    [Reactive] private TimeSpan delay;
    /// <summary>
    /// The total length of this cue.
    /// </summary>
    [Reactive, CustomAccessibility("public virtual"), ModelSkip, SkipEqualityCheck, NoUndo] private TimeSpan duration;
    /// <summary>
    /// Whether this cue should loop when triggered and how it should loop.
    /// </summary>
    [Reactive, ChangesProp(nameof(UseLoopCount))] private LoopMode loopMode;
    /// <summary>
    /// The number of loops of this cue to play before stopping. Only effective 
    /// if <see cref="LoopMode"/> is set to <see cref="LoopMode.Looped"/>.
    /// </summary>
    [Reactive] public int loopCount;

    [Reactive, Readonly, ModelSkip] protected readonly MainViewModel mainViewModel;
    /// <summary>
    /// Whether this cue is the primary selected cue. If this is <see langword="true"/> it implies <see cref="IsMultiSelected"/>.
    /// </summary>
    public bool IsSelected => mainViewModel.SelectedCue == this;
    /// <summary>
    /// Whether this cue is in the multi-selection.
    /// </summary>
    public bool IsMultiSelected => mainViewModel.MultiSelection.Contains(this);
    /// <summary>
    /// The current playback state of this cue.
    /// </summary>
    [Reactive, ModelSkip, NoUndo, CachedNotification] private CueState state;
    /// <summary>
    /// The current playback time of this cue. Note that for looping cues, this value continues to 
    /// increase monotonically with each loop rather than resetting.
    /// </summary>
    [Reactive, CustomAccessibility("public virtual"), SkipEqualityCheck, ModelSkip, NoUndo, CachedNotification]
    private TimeSpan playbackTime;
    /// <summary>
    /// Whether the <see cref="LoopCount"/> property is enabled.
    /// </summary>
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
    [Reactive, Readonly, ModelSkip] private static ObservableCollection<string>? groupTriggerModeVals;

    public bool IsRemoteControlling => mainViewModel.ProjectSettings.EnableRemoteControl
        && !string.IsNullOrEmpty(RemoteNode) && RemoteNode != mainViewModel.ProjectSettings.NodeName;

    /// <summary>
    /// The duration of this cue, as received from a remote node.
    /// </summary>
    public virtual TimeSpan RemoteDuration { set { } }
    #endregion

    public delegate void OnCueCompletedDelegate(CueViewModel cue);
    public event OnCueCompletedDelegate? OnCompleted;

    protected Dispatcher? dispatcher;
    protected DispatcherDelay goDelay;
    private readonly SolidColorBrush colourBrush;
    private CueViewModel? waitCue;
    private readonly string typeName;
    private readonly string typeDisplayName;

    public CueViewModel(MainViewModel mainViewModel)
    {
        this.mainViewModel = mainViewModel;
        colourBrush = new(Colour.ToMediaColor(127));
        dispatcher = Dispatcher.CurrentDispatcher;

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
        GroupTriggerModeVals ??= new ObservableCollection<string>(Enum.GetValues<GroupTriggerMode>().Select(x => EnumToString(x)));
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
        // Debug.WriteLine($"Sel changed: {QID} ==> {new StackTrace()}");
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

        if (!mainViewModel.ActiveCues.Contains(this))
            mainViewModel.ActiveCues.Add(this);
    }

    private void WaitCueOnCompleteHandler(CueViewModel waitCue)
    {
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
            mainViewModel.OSCManager.SendRemoteGo(RemoteNode, FullQID);

        if (Duration == TimeSpan.Zero)
        {
            StopInternal();
            return;
        }
        State = CueState.Playing;
        if (!mainViewModel.ActiveCues.Contains(this))
            mainViewModel.ActiveCues.Add(this);
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
            mainViewModel.OSCManager.SendRemotePause(RemoteNode, FullQID);
    }

    /// <summary>
    /// Stops this cue immediately.
    /// </summary>
    public virtual void Stop()
    {
        StopInternal();

        if (IsRemoteControlling)
            mainViewModel.OSCManager.SendRemoteStop(RemoteNode, FullQID);
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
        mainViewModel.ActiveCues.Remove(this);
        OnCompleted?.Invoke(this);
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
                mainViewModel.OSCManager.SendRemotePreload(RemoteNode, FullQID, (float)startTime.TotalSeconds);
        }
    }

    /// <summary>
    /// Selects this cue, applying the current multi-selection modifiers.
    /// </summary>
    public void SelectExecute()
    {
        mainViewModel.MultiSelect(this);
    }

    /// <summary>
    /// This callback is triggered every 50 or so ms when this cue is active by the main thread.
    /// </summary>
    protected internal virtual void UpdateUIStatus()
    {

    }
    #endregion

    /// <summary>
    /// Checks whether this cue has any parent.
    /// </summary>
    /// <returns></returns>
    public bool HasParent() => Parent != null;

    /// <summary>
    /// Checks whether this cue has the given cue as one of it's parents. If <paramref name="target"/> 
    /// is <see langword="this"/> instance, returns <see langword="false"/>.
    /// </summary>
    /// <param name="target"></param>
    /// <returns></returns>
    public bool HasParent(CueViewModel? target)
    {
        var p = Parent;
        if (target == null)
            return p == null;

        while (p != null)
        {
            if (p == target)
                return true;
            p = p.Parent;
        }
        return false;
    }

    private static readonly PropertyChangedEventArgs FullQIDChanged = new(nameof(FullQID));
    protected internal virtual void UpdateFullQID()
    {
        if (dispatcher == null || !dispatcher.CheckAccess())
            return;

        var old = fullQID;
        if (!HasParent())
            fullQID = QID.ToString(MainViewModel.numberFormat);
        else
            fullQID = $"{Parent!.FullQID}-{QID.ToString(MainViewModel.numberFormat)}";

        mainViewModel.Cues.NotifyQIDChanged(old, fullQID, this);
        OnPropertyChanged(FullQIDChanged);
    }

    private static void VM2M_Colour(CueViewModel vm, Cue m) => m.colour = (SerializedColour)vm.Colour;
    private static void M2VM_Colour(CueViewModel vm, Cue m) => vm.Colour = (ColorState)m.colour;
    private static void VM2M_Parent(CueViewModel vm, Cue m) => m.parent = vm.ParentId;
    private static void M2VM_Parent(CueViewModel vm, Cue m)
    {
        if (m.parent != null && vm.mainViewModel.FindCue(m.parent, out var parentCue))
            vm.Parent = parentCue;
        else
            vm.Parent = null;
    }

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
