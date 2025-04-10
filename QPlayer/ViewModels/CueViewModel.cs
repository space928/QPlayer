﻿using ColorPicker.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using System.Windows;
using System.Windows.Data;
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
                mainViewModel?.OnQIDChanged(qid, value, this);
                qid = value;
            }
        }
        [Reactive] public CueType Type { get; set; }
        [Reactive] public decimal? ParentId { get; set; }
        [Reactive] public CueViewModel? Parent => ParentId != null ? (parent ??= mainViewModel?.Cues.FirstOrDefault(x => x.QID == ParentId)) : null;
        [Reactive] public ColorState Colour { get; set; }
        [Reactive] public string Name { get; set; } = string.Empty;
        [Reactive] public string Description { get; set; } = string.Empty;
        [Reactive] public bool Halt { get; set; }
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

        [Reactive] public RelayCommand GoCommand { get; private set; }
        [Reactive] public RelayCommand PauseCommand { get; private set; }
        [Reactive] public RelayCommand StopCommand { get; private set; }
        [Reactive] public RelayCommand SelectCommand { get; private set; }

        [Reactive] public static ObservableCollection<CueType>? CueTypeVals { get; private set; }
        [Reactive] public static ObservableCollection<LoopMode>? LoopModeVals { get; private set; }
        [Reactive] public static ObservableCollection<StopMode>? StopModeVals { get; private set; }
        [Reactive] public static ObservableCollection<FadeType>? FadeTypeVals { get; private set; }
        #endregion

        public event EventHandler? OnCompleted;

        protected decimal qid;
        protected SynchronizationContext? synchronizationContext;
        protected CueViewModel? parent;
        protected Cue? cueModel;
        protected MainViewModel? mainViewModel;
        protected Timer goTimer;

        public CueViewModel(MainViewModel mainViewModel)
        {
            this.mainViewModel = mainViewModel;
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
        }

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
        public virtual void DelayedGo()
        {
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

        /// <summary>
        /// Starts this cue immediately.
        /// </summary>
        public virtual void Go()
        {
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
        }

        /// <summary>
        /// Stops this cue immediately.
        /// </summary>
        public virtual void Stop()
        {
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
            PropertyChanged += (o, e) =>
            {
                CueViewModel vm = (CueViewModel)(o ?? throw new NullReferenceException(nameof(CueViewModel)));
                if (e.PropertyName != null)
                    vm.ToModel(e.PropertyName);
            };
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
                case nameof(Colour): cueModel.colour = Colour.ToColor(); break;
                case nameof(Name): cueModel.name = Name; break;
                case nameof(Description): cueModel.description = Description; break;
                case nameof(Halt): cueModel.halt = Halt; break;
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
            cue.colour = Colour.ToColor();
            cue.name = Name;
            cue.description = Description;
            cue.halt = Halt;
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
                _ => throw new ArgumentException(null, nameof(cue.type)),
            };
            viewModel.mainViewModel = mainViewModel;

            viewModel.QID = cue.qid;
            viewModel.ParentId = cue.parent;
            viewModel.Type = cue.type;
            viewModel.Colour = cue.colour.ToColorState();
            viewModel.Name = cue.name;
            viewModel.Description = cue.description;
            viewModel.Halt = cue.halt;
            viewModel.Enabled = cue.enabled;
            viewModel.Delay = cue.delay;
            viewModel.LoopMode = cue.loopMode;
            viewModel.LoopCount = cue.loopCount;

            return viewModel;
        }
    }

    [ValueConversion(typeof(TimeSpan[]), typeof(double))]
    public class ElapsedTimeConverter : IMultiValueConverter
    {
        public object Convert(object[] value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value[0] == DependencyProperty.UnsetValue || value[1] == DependencyProperty.UnsetValue)
                return 0d;
            if (((TimeSpan)value[1]).TotalSeconds == 0)
                return 0d;
            return ((TimeSpan)value[0]).TotalSeconds / ((TimeSpan)value[1]).TotalSeconds * 100;
        }

        public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    [ValueConversion(typeof(float), typeof(GridLength))]
    public class FloatGridLengthConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return new GridLength((float)value);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            GridLength gridLength = (GridLength)value;
            return (float)gridLength.Value;
        }
    }

    [ValueConversion(typeof(TimeSpan), typeof(string))]
    public class TimeSpanStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return Convert((TimeSpan)value, parameter is string useHours && useHours == "True");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string timeSpan = (string)value;

            if (ConvertBack(timeSpan, out TimeSpan ret, parameter is string useHours && useHours == "True"))
                return ret;

            return DependencyProperty.UnsetValue;
        }

        public static string Convert(TimeSpan value, bool useHours = false)
        {
            if (useHours)
                return value.ToString(@"hh\:mm\:ss\.ff");
            else
                return value.ToString(@"mm\:ss\.ff");
        }

        public static bool ConvertBack(string value, out TimeSpan result, bool useHours = false)
        {
            if (useHours)
                return TimeSpan.TryParse(value, out result);
            else
                return TimeSpan.TryParse($"00:{value}", out result);
        }
    }

    [ValueConversion(typeof(double), typeof(bool))]
    public class GreaterThanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (double)value > double.Parse((string)parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }

    [ValueConversion(typeof(double), typeof(double))]
    public class MultiplyByConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return (double)value * double.Parse((string)parameter);
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            return DependencyProperty.UnsetValue;
        }
    }
}
