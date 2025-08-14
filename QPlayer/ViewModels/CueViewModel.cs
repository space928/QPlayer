﻿using ColorPicker.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PropertyChanged;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Utilities;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Media;
using Cue = QPlayer.Models.Cue;
using Timer = System.Timers.Timer;

namespace QPlayer.ViewModels
{
    public interface IConvertibleModel<Model, ViewModel>
    {
        /// <summary>
        /// Creates a new ViewModel from the given Model, without binding it.
        /// </summary>
        /// <param name="model">the model to copy properties from</param>
        /// <param name="mainViewModel">the main view model</param>
        /// <returns>a new ViewModel for the given Model.</returns>
        public abstract static ViewModel FromModel(Model model, MainViewModel mainViewModel);
        /// <summary>
        /// Copies the properties in this ViewModel to the given Model object.
        /// </summary>
        /// <param name="model">the model to copy to</param>
        public abstract void ToModel(Model model);
        /// <summary>
        /// Copies the value of a given property to the bound Model.
        /// </summary>
        /// <param name="propertyName">the property to copy</param>
        public abstract void ToModel(string propertyName);
        /// <summary>
        /// Binds this view model to a given model, such that updates from the view model are propagated to the model (but NOT vice versa).
        /// </summary>
        /// <param name="model">the model to bind to</param>
        public abstract void Bind(Model model);
    }

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

    public abstract class CueViewModel : ObservableObject, IConvertibleModel<Cue, CueViewModel>
    {
        #region Bindable Properties
        [Reactive]
        public decimal QID
        {
            get => qid;
            set
            {
                mainViewModel?.NotifyQIDChanged(qid, value, this);
                qid = value;
            }
        }
        [Reactive] public CueType Type { get; set; }
        [Reactive] public decimal? ParentId { get; set; }
        [Reactive] public CueViewModel? Parent => ParentId != null ? (parent ??= mainViewModel?.Cues.FirstOrDefault(x => x.QID == ParentId)) : null;
        [Reactive] public ColorState Colour { get; set; }
        [Reactive] public string Name { get; set; } = string.Empty;
        [Reactive] public string Description { get; set; } = string.Empty;
        [Reactive] public string RemoteNode { get; set; } = string.Empty;
        [Reactive] public TriggerMode Trigger { get; set; }
        [Reactive] public bool Enabled { get; set; } = true;
        [Reactive] public TimeSpan Delay { get; set; }
        [Reactive] public virtual TimeSpan Duration { get; }
        [Reactive] public LoopMode LoopMode { get; set; }
        [Reactive] public int LoopCount { get; set; }

        [Reactive] public MainViewModel? MainViewModel => mainViewModel;
        [Reactive] public bool IsSelected => mainViewModel?.SelectedCue == this;
        [Reactive] public CueState State { get; set; }
        [Reactive] public virtual TimeSpan PlaybackTime { get; set; }
        [Reactive]
        [ReactiveDependency(nameof(LoopMode))]
        public bool UseLoopCount => LoopMode == LoopMode.Looped || LoopMode == LoopMode.LoopedInfinite;
        [Reactive]
        [ReactiveDependency(nameof(PlaybackTime))]
        public string PlaybackTimeString => State switch
        {
            CueState.Delay => $"WAIT {Delay:mm\\:ss\\.ff}",
            CueState.Playing or CueState.PlayingLooped or CueState.Paused => $"{PlaybackTime:mm\\:ss} / {Duration:mm\\:ss}",
            _ => $"{Duration:mm\\:ss\\.ff}",
        };
        [Reactive]
        [ReactiveDependency(nameof(PlaybackTime))]
        public string PlaybackTimeStringShort => State switch
        {
            CueState.Delay => $"WAIT",
            CueState.Playing or CueState.PlayingLooped or CueState.Paused => $"-{PlaybackTime - Duration:mm\\:ss}",
            _ => $"{Duration:mm\\:ss}",
        };
        [Reactive, ReactiveDependency(nameof(Colour))]
        public SolidColorBrush ColourBrush
        {
            get
            {
                colourBrush.Color = Colour.ToMediaColor(127);
                return colourBrush;
            }
        }
        [Reactive][ReactiveDependency(nameof(Type))] 
        public string TypeName => EnumToString(Type);

        [Reactive] public RelayCommand GoCommand { get; private set; }
        [Reactive] public RelayCommand PauseCommand { get; private set; }
        [Reactive] public RelayCommand StopCommand { get; private set; }
        [Reactive] public RelayCommand SelectCommand { get; private set; }

        [Reactive] public static ObservableCollection<CueType>? CueTypeVals { get; private set; }
        [Reactive] public static ObservableCollection<LoopMode>? LoopModeVals { get; private set; }
        [Reactive] public static ObservableCollection<StopMode>? StopModeVals { get; private set; }
        [Reactive] public static ObservableCollection<FadeType>? FadeTypeVals { get; private set; }
        [Reactive] public static ObservableCollection<string>? TriggerModeVals { get; private set; }

        [Reactive] public bool IsRemoteControlling => (mainViewModel?.ProjectSettings?.EnableRemoteControl ?? false)
            && !string.IsNullOrEmpty(RemoteNode) && RemoteNode != mainViewModel.ProjectSettings.NodeName;

        // Suppress warnings, this property will have it's notifications handled by the implementor.
        /// <summary>
        /// The duration of this cue, as received from a remote node.
        /// </summary>
        [SuppressPropertyChangedWarnings] public virtual TimeSpan RemoteDuration { set { } }
        #endregion

        public event EventHandler? OnCompleted;

        protected decimal qid;
        protected SynchronizationContext? synchronizationContext;
        protected CueViewModel? parent;
        protected Cue? cueModel;
        protected MainViewModel? mainViewModel;
        protected Timer goTimer;
        private readonly SolidColorBrush colourBrush;
        private CueViewModel? waitCue;

        public CueViewModel(MainViewModel mainViewModel)
        {
            this.mainViewModel = mainViewModel;
            colourBrush = new(Colour.ToMediaColor(127));
            synchronizationContext = SynchronizationContext.Current;
            mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            goTimer = new()
            {
                AutoReset = false,
            };
            goTimer.Elapsed += (o, e) => synchronizationContext?.Post((x) => Go(), null);

            GoCommand = new(Go);
            PauseCommand = new(Pause);
            StopCommand = new(Stop);
            SelectCommand = new(SelectExecute);

            CueTypeVals ??= new ObservableCollection<CueType>(Enum.GetValues<CueType>());
            LoopModeVals ??= new ObservableCollection<LoopMode>(Enum.GetValues<LoopMode>());
            StopModeVals ??= new ObservableCollection<StopMode>(Enum.GetValues<StopMode>());
            FadeTypeVals ??= new ObservableCollection<FadeType>(Enum.GetValues<FadeType>());
            TriggerModeVals ??= new ObservableCollection<string>(Enum.GetValues<TriggerMode>().Select(x=>EnumToString(x)));
        }

        /// <summary>
        /// This method is invoked by QPlayer when this cue is selected in the inspector.
        /// </summary>
        internal virtual void OnFocussed()
        {

        }

        private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.SelectedCue):
                    OnPropertyChanged(nameof(IsSelected));
                    break;
                case nameof(RemoteDuration):
                    OnPropertyChanged(nameof(Duration));
                    break;
                case nameof(State):
                case nameof(Duration):
                    OnPropertyChanged(nameof(PlaybackTimeString));
                    OnPropertyChanged(nameof(PlaybackTimeStringShort));
                    break;
            }
        }

        #region Command Handlers
        /// <summary>
        /// Starts this cues after it's delay has elapsed.
        /// </summary>
        /// <param name="waitForCue">Optionally, a cue to wait for it's <see cref="OnCompleted"/> event before starting this cue.</param>
        public virtual void DelayedGo(CueViewModel? waitForCue = null)
        {
            if (waitForCue != null && waitForCue.Duration != TimeSpan.Zero)
            {
                State = CueState.Delay;
                // Unregister the previous waiter, if it's still set
                if (waitCue != null)
                    waitCue.OnCompleted -= WaitCueOnCompleteHandler;
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
            goTimer.Interval = Delay.TotalMilliseconds;
            goTimer.Start();

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
            goTimer.Stop();
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
        /// Stops this cue without informing remote clients or executing cue specific stopping code.
        /// This is called when a remote node needs to tell us that a cue has finished playing.
        /// </summary>
        internal void StopInternal()
        {
            if (waitCue != null)
            {
                // This cue has been stopped/cancelled, stop waiting for the wait cue.
                waitCue.OnCompleted -= WaitCueOnCompleteHandler;
                waitCue = null;
            }    
            goTimer.Stop();
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
                OnPropertyChanged(nameof(PlaybackTimeString));
                OnPropertyChanged(nameof(PlaybackTimeStringShort));

                if (IsRemoteControlling)
                    mainViewModel?.OSCManager.SendRemotePreload(RemoteNode, qid, (float)startTime.TotalSeconds);
            }
        }

        public void SelectExecute()
        {
            if (mainViewModel != null)
                mainViewModel.SelectedCue = this;
        }
        #endregion

        public void OnColourUpdate()
        {
            OnPropertyChanged(nameof(Colour));
        }

        public void Bind(Cue cue)
        {
            cueModel = cue;
            PropertyChanged += PropertyChangedHandler;
        }

        public void UnBind()
        {
            cueModel = null;
            PropertyChanged -= PropertyChangedHandler;
        }

        private void PropertyChangedHandler(object? o, PropertyChangedEventArgs e)
        {
            CueViewModel vm = (CueViewModel)(o ?? throw new NullReferenceException(nameof(CueViewModel)));
            if (e.PropertyName != null)
                vm.ToModel(e.PropertyName);
        }

        /// <summary>
        /// Propagates an update from the given property on this view model to it's bound model.
        /// </summary>
        /// <param name="propertyName">the property to update</param>
        /// <exception cref="ArgumentNullException"></exception>
        public virtual void ToModel(string propertyName)
        {
            if (cueModel == null)
                throw new ArgumentNullException(null, nameof(cueModel));
            switch (propertyName)
            {
                case nameof(QID): cueModel.qid = QID; break;
                case nameof(Type): cueModel.type = Type; break;
                case nameof(Parent): cueModel.parent = Parent?.QID; break;
                case nameof(Colour): cueModel.colour = (SerializedColour)Colour; break;
                case nameof(Name): cueModel.name = Name; break;
                case nameof(Description): cueModel.description = Description; break;
                case nameof(RemoteNode): cueModel.remoteNode = RemoteNode; break;
                case nameof(Trigger): cueModel.trigger = Trigger; break;
                case nameof(Enabled): cueModel.enabled = Enabled; break;
                case nameof(Delay): cueModel.delay = Delay; break;
                case nameof(LoopMode): cueModel.loopMode = LoopMode; break;
                case nameof(LoopCount): cueModel.loopCount = LoopCount; break;
            }
        }

        /// <summary>
        /// Converts this MainViewModel to a model.
        /// </summary>
        /// <param name="cue">the model to update</param>
        public virtual void ToModel(Cue cue)
        {
            cue.qid = QID;
            cue.type = Type;
            cue.parent = Parent?.QID;
            cue.colour = (SerializedColour)Colour;
            cue.name = Name;
            cue.description = Description;
            cue.remoteNode = RemoteNode;
            cue.trigger = Trigger;
            cue.enabled = Enabled;
            cue.delay = Delay;
            cue.loopMode = LoopMode;
            cue.loopCount = LoopCount;
        }

        public static CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            if (cue.qid == cue.parent)
                throw new ArgumentException($"Circular reference detected! Cue {cue.qid} has itself as a parent!");
            CueViewModel viewModel = cue.type switch
            {
                CueType.GroupCue => GroupCueViewModel.FromModel(cue, mainViewModel),
                CueType.DummyCue => DummyCueViewModel.FromModel(cue, mainViewModel),
                CueType.SoundCue => SoundCueViewModel.FromModel(cue, mainViewModel),
                CueType.TimeCodeCue => TimeCodeCueViewModel.FromModel(cue, mainViewModel),
                CueType.StopCue => StopCueViewModel.FromModel(cue, mainViewModel),
                CueType.VolumeCue => VolumeCueViewModel.FromModel(cue, mainViewModel),
                _ => throw new ArgumentException($"Unknown cue type '{cue.type}'!"),
            };
            viewModel.mainViewModel = mainViewModel;

            viewModel.QID = cue.qid;
            viewModel.ParentId = cue.parent;
            viewModel.Type = cue.type;
            viewModel.Colour = (ColorState)cue.colour;
            viewModel.Name = cue.name;
            viewModel.Description = cue.description;
            viewModel.RemoteNode = cue.remoteNode;
            viewModel.Trigger = cue.trigger;
            viewModel.Enabled = cue.enabled;
            viewModel.Delay = cue.delay;
            viewModel.LoopMode = cue.loopMode;
            viewModel.LoopCount = cue.loopCount;

            return viewModel;
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
}
