using ColorPicker.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;

namespace QPlayer.ViewModels
{
    public interface IConvertibleCue
    {
        public abstract static CueViewModel FromModel(Cue cue, ViewModel mainViewModel);
        public abstract void ToModel(Cue cue);
        public abstract void ToModel(string propertyName);
    }

    public enum CueState
    {
        Ready,
        Playing,
        PlayingLooped,
        Paused,
    }

    public abstract class CueViewModel : ObservableObject
    {
        #region Bindable Properties
        [Reactive] public decimal QID { get; set; }
        [Reactive] public CueType Type { get; set; }
        [Reactive] public decimal? ParentId { get; set; }
        [Reactive] public CueViewModel? Parent => ParentId != null ? (parent ??= mainViewModel?.Cues.FirstOrDefault(x=>x.QID==ParentId)) : null;
        [Reactive] public ColorState Colour { get; set; }
        [Reactive] public string Name { get; set; } = string.Empty;
        [Reactive] public string Description { get; set; } = string.Empty;
        [Reactive] public bool Halt { get; set; }
        [Reactive] public bool Enabled { get; set; }
        [Reactive] public TimeSpan Delay { get; set; }
        [Reactive] public TimeSpan Duration { get; }
        [Reactive] public LoopMode LoopMode { get; set; }
        [Reactive] public int LoopCount { get; set; }

        [Reactive] public ViewModel? MainViewModel => mainViewModel;
        [Reactive] public bool IsSelected => mainViewModel?.SelectedCue == this;
        [Reactive] public CueState State { get; set; }
        [Reactive] public TimeSpan PlaybackTime { get; set; }

        [Reactive] public RelayCommand GoCommand { get; private set; }
        [Reactive] public RelayCommand PauseCommand { get; private set; }
        [Reactive] public RelayCommand StopCommand { get; private set; }
        #endregion

        protected DateTime startTime;
        protected CueViewModel? parent;
        protected Cue? cueModel;
        protected ViewModel? mainViewModel;
        protected Task? playbackTask;

        public CueViewModel(ViewModel mainViewModel)
        {
            this.mainViewModel = mainViewModel;
            mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            GoCommand = new(Go);
            PauseCommand = new(Pause);
            StopCommand = new(Stop);
        }

        private void MainViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            switch (e.PropertyName)
            {
                case nameof(MainViewModel.SelectedCue):
                    OnPropertyChanged(nameof(IsSelected));
                    break;
            }
        }

        #region Command Handlers
        public virtual void Go()
        {
            State = CueState.Playing;
            startTime = DateTime.Now - PlaybackTime;
            if(!mainViewModel?.ActiveCues?.Contains(this) ?? false)
                mainViewModel?.ActiveCues.Add(this);
        }

        public virtual void Pause()
        {
            State = CueState.Paused;
            PlaybackTime = startTime - DateTime.Now;
        }

        public virtual void Stop()
        {
            State = CueState.Ready;
            PlaybackTime = TimeSpan.Zero;
            mainViewModel?.ActiveCues.Remove(this);
        }
        #endregion

        public void OnColourUpdate()
        {
            OnPropertyChanged(nameof(Colour));
        }

        /// <summary>
        /// Binds this view model to a given model, such that updates from the view model are propagated to the model (but NOT vice versa).
        /// </summary>
        /// <param name="cue">the cue to bind to</param>
        /// <exception cref="NullReferenceException"></exception>
        public void Bind(Cue cue)
        {
            cueModel = cue;
            PropertyChanged += (o, e) =>
            {
                CueViewModel vm = (CueViewModel)(o ?? throw new NullReferenceException(nameof(CueViewModel)));
                if(e.PropertyName != null)
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
            if(cueModel == null)
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
        /// Converts this ViewModel to a model.
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

        public static CueViewModel FromModel(Cue cue, ViewModel mainViewModel)
        {
            if(cue.qid == cue.parent)
                throw new ArgumentException($"Circular reference detected! Cue {cue.qid} has itself as a parent!");

            CueViewModel viewModel;
            switch(cue.type)
            {
                case CueType.GroupCue: viewModel = GroupCueViewModel.FromModel(cue, mainViewModel); break;
                case CueType.DummyCue: viewModel = DummyCueViewModel.FromModel(cue, mainViewModel); break;
                case CueType.SoundCue: viewModel = SoundCueViewModel.FromModel(cue, mainViewModel); break;
                case CueType.TimeCodeCue: viewModel = TimeCodeCueViewModel.FromModel(cue, mainViewModel); break;
                default: throw new ArgumentException(null, nameof(cue.type));
            }
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

    public class GroupCueViewModel : CueViewModel, IConvertibleCue
    {
        public GroupCueViewModel(ViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
        }

        public static new CueViewModel FromModel(Cue cue, ViewModel mainViewModel)
        {
            GroupCueViewModel vm = new(mainViewModel);
            if (cue is GroupCue gcue)
            {
                //
            }
            return vm;
        }
    }

    public class DummyCueViewModel : CueViewModel, IConvertibleCue
    {
        public DummyCueViewModel(ViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
        }

        public static new CueViewModel FromModel(Cue cue, ViewModel mainViewModel)
        {
            DummyCueViewModel vm = new(mainViewModel);
            if (cue is DummyCue dcue)
            {
                //
            }
            return vm;
        }
    }

    public class SoundCueViewModel : CueViewModel, IConvertibleCue
    {
        [Reactive] public string Path { get; set; } = string.Empty;
        [Reactive] public DateTime StartTime { get; set; }
        [Reactive] public new TimeSpan Duration { get; set; } = TimeSpan.MaxValue;
        [Reactive] public float FadeIn { get; set; }
        [Reactive] public float FadeOut { get; set; }

        public SoundCueViewModel(ViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(string propertyName)
        {
            base.ToModel(propertyName);
            if(cueModel is SoundCue scue)
            {
                switch (propertyName)
                {
                    case nameof(Path): scue.path = Path; break;
                    case nameof(StartTime): scue.startTime = StartTime; break;
                    case nameof(Duration): scue.duration = Duration; break;
                    case nameof(FadeIn): scue.fadeIn = FadeIn; break;
                    case nameof(FadeOut): scue.fadeOut = FadeOut; break;
                }
            }
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
            if (cue is SoundCue scue)
            {
                scue.path = Path;
                scue.startTime = StartTime;
                scue.duration = Duration;
                scue.fadeIn = FadeIn;
                scue.fadeOut = FadeOut;
            }
        }

        public static new CueViewModel FromModel(Cue cue, ViewModel mainViewModel)
        {
            SoundCueViewModel vm = new(mainViewModel);
            if(cue is SoundCue scue)
            {
                vm.Path = scue.path;
                vm.StartTime = scue.startTime;
                vm.Duration = scue.duration;
                vm.FadeIn = scue.fadeIn;
                vm.FadeOut = scue.fadeOut;
            }
            return vm;
        }
    }

    public class TimeCodeCueViewModel : CueViewModel, IConvertibleCue
    {
        [Reactive] public DateTime StartTime { get; set; }
        [Reactive] public new TimeSpan Duration { get; set; }

        public TimeCodeCueViewModel(ViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(string propertyName)
        {
            base.ToModel(propertyName);
            if (cueModel is TimeCodeCue tccue)
            {
                switch (propertyName)
                {
                    case nameof(StartTime): tccue.startTime = StartTime; break;
                    case nameof(Duration): tccue.duration = Duration; break;
                }
            }
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
            if (cue is TimeCodeCue tccue)
            {
                tccue.startTime = StartTime;
                tccue.duration = Duration;
            }
        }

        public static new CueViewModel FromModel(Cue cue, ViewModel mainViewModel)
        {
            TimeCodeCueViewModel vm = new(mainViewModel);
            if (cue is TimeCodeCue tccue)
            {
                vm.StartTime = tccue.startTime;
                vm.Duration = tccue.duration;
            }
            return vm;
        }
    }

    public class StopCueViewModel : CueViewModel, IConvertibleCue
    {
        [Reactive] public decimal StopTarget { get; set; }
        [Reactive] public StopMode StopMode { get; set; }

        public StopCueViewModel(ViewModel mainViewModel) : base(mainViewModel)
        {
        }

        public override void ToModel(string propertyName)
        {
            base.ToModel(propertyName);
            if (cueModel is StopCue scue)
            {
                switch (propertyName)
                {
                    case nameof(StopTarget): scue.stopQid = StopTarget; break;
                    case nameof(StopMode): scue.stopMode = StopMode; break;
                }
            }
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
            if (cue is StopCue scue)
            {
                scue.stopQid = StopTarget;
                scue.stopMode = StopMode;
            }
        }

        public static new CueViewModel FromModel(Cue cue, ViewModel mainViewModel)
        {
            StopCueViewModel vm = new(mainViewModel);
            if (cue is StopCue scue)
            {
                vm.StopTarget = scue.stopQid;
                vm.StopMode = scue.stopMode;
            }
            return vm;
        }
    }

    [ValueConversion(typeof(TimeSpan[]), typeof(double))]
    public class ElapsedTimeConverter : IMultiValueConverter
    {
        public object Convert(object[] value, Type targetType, object parameter, CultureInfo culture)
        {
            if (((TimeSpan)value[1]).TotalSeconds == 0)
                return 0d;
            return ((TimeSpan)value[0]).TotalSeconds / ((TimeSpan)value[1]).TotalSeconds;
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
}
