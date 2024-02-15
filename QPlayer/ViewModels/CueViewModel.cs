using ColorPicker.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels
{
    public interface IConvertibleCue
    {
        public abstract static CueViewModel FromModel(Cue cue);
        public abstract void ToModel(Cue cue);
        public abstract void ToModel(string propertyName);
    }

    public abstract class CueViewModel : ObservableObject, IConvertibleCue
    {
        #region Bindable Properties
        [Reactive] public decimal QID { get; set; }
        [Reactive] public CueType Type { get; set; }
        [Reactive] public GroupCueViewModel? Parent { get; set; }
        [Reactive] public ColorState Colour { get; set; }
        [Reactive] public string Name { get; set; } = string.Empty;
        [Reactive] public string Description { get; set; } = string.Empty;
        [Reactive] public bool Halt { get; set; }
        [Reactive] public bool Enabled { get; set; }
        [Reactive] public TimeSpan Delay { get; set; }
        #endregion

        protected Cue? cueModel;

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
        }

        /// <summary>
        /// Creates a view model for a given cue model.
        /// </summary>
        /// <param name="qid">the id of the cue to create a model for</param>
        /// <param name="cues">a dictionary of all the cue models (needed to find parent cues)</param>
        /// <param name="cueViewModels">(mutable) a dictionary of existing cue view models (used to cache parent view models as they are constructed)</param>
        /// <returns>A new (or cached) view model for the given cue model.</returns>
        /// <exception cref="ArgumentException"></exception>
        public static CueViewModel FromModel(decimal qid, Dictionary<decimal, Cue> cues, Dictionary<decimal, CueViewModel> cueViewModels)
        {
            if(!cues.ContainsKey(qid))
                throw new ArgumentException(null, nameof(qid));
            var cue = cues[qid];
            if (cue.qid != qid)
                throw new ArgumentException($"The QID of the cue didn't match it's dictionary value! This should be impossible!");
            if(cue.qid == cue.parent)
                throw new ArgumentException($"Circular reference detected! Cue {qid} has itself as a parent!");
            if(cueViewModels.ContainsKey(cue.qid))
                return cueViewModels[cue.qid];

            CueViewModel viewModel;
            switch(cue.type)
            {
                case CueType.GroupCue: viewModel = GroupCueViewModel.FromModel(cue); break;
                case CueType.DummyCue: viewModel = DummyCueViewModel.FromModel(cue); break;
                case CueType.SoundCue: viewModel = SoundCueViewModel.FromModel(cue); break;
                case CueType.TimeCodeCue: viewModel = TimeCodeCueViewModel.FromModel(cue); break;
                default: throw new ArgumentException(null, nameof(cue.type));
            }

            if (cue.parent != null)
            {
                if (cueViewModels.ContainsKey((decimal)cue.parent))
                {
                    if (cueViewModels[(decimal)cue.parent] is GroupCueViewModel gcViewModel)
                        viewModel.Parent = gcViewModel;
                }
                else
                {
                    CueViewModel parentVm = FromModel((decimal)cue.parent, cues, cueViewModels);
                    if (cueViewModels[(decimal)cue.parent] is GroupCueViewModel gcViewModel)
                        viewModel.Parent = gcViewModel;
                    cueViewModels[(decimal)cue.parent] = parentVm;
                }
            }

            viewModel.QID = cue.qid;
            viewModel.Type = cue.type;
            viewModel.Colour = cue.colour.ToColorState();
            viewModel.Name = cue.name;
            viewModel.Description = cue.description;
            viewModel.Halt = cue.halt;
            viewModel.Enabled = cue.enabled;
            viewModel.Delay = cue.delay;

            cueViewModels[qid] = viewModel;
            return viewModel;
        }

        public static CueViewModel FromModel(Cue cue)
        {
            throw new NotImplementedException();
        }
    }

    public class GroupCueViewModel : CueViewModel, IConvertibleCue
    {
        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
        }

        public static new CueViewModel FromModel(Cue cue)
        {
            GroupCueViewModel vm = new();
            if (cue is GroupCue gcue)
            {
                //
            }
            return vm;
        }
    }

    public class DummyCueViewModel : CueViewModel, IConvertibleCue
    {
        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
        }

        public static new CueViewModel FromModel(Cue cue)
        {
            DummyCueViewModel vm = new();
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
        [Reactive] public TimeSpan Duration { get; set; } = TimeSpan.MaxValue;
        [Reactive] public float FadeIn { get; set; }
        [Reactive] public float FadeOut { get; set; }

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

        public static new CueViewModel FromModel(Cue cue)
        {
            SoundCueViewModel vm = new();
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
        [Reactive] public TimeSpan Duration { get; set; }

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

        public static new CueViewModel FromModel(Cue cue)
        {
            TimeCodeCueViewModel vm = new();
            if (cue is TimeCodeCue tccue)
            {
                vm.StartTime = tccue.startTime;
                vm.Duration = tccue.duration;
            }
            return vm;
        }
    }
}
