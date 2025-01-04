using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Linq;
using System.Timers;
using Cue = QPlayer.Models.Cue;
using QPlayer.Audio;

namespace QPlayer.ViewModels
{
    public class VolumeCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
    {
        [Reactive, ReactiveDependency(nameof(FadeTime))] 
        public override TimeSpan Duration => TimeSpan.FromSeconds(FadeTime);
        [Reactive] public decimal Target { get; set; }
        [Reactive] public float Volume { get; set; }
        [Reactive] public float FadeTime { get; set; }
        [Reactive] public FadeType FadeType { get; set; }

        private readonly Timer playbackProgressUpdater;
        private DateTime startTime;

        public VolumeCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        {
            playbackProgressUpdater = new Timer
            {
                AutoReset = true,
                Interval = 100
            };
            playbackProgressUpdater.Elapsed += PlaybackProgressUpdater_Elapsed;
            PropertyChanged += (o, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(FadeTime):
                        OnPropertyChanged(nameof(Duration));
                        OnPropertyChanged(nameof(PlaybackTimeString));
                        OnPropertyChanged(nameof(PlaybackTimeStringShort));
                        break;
                }
            };
        }

        private void PlaybackProgressUpdater_Elapsed(object? sender, ElapsedEventArgs e)
        {
            PlaybackTime = DateTime.Now.Subtract(startTime);
            if (PlaybackTime >= Duration)
            {
                synchronizationContext?.Post(x => Stop(), null);
            }
        }

        public override void Go()
        {
            base.Go();
            // Volume cues don't support preloading
            PlaybackTime = TimeSpan.Zero;
            startTime = DateTime.Now;
            playbackProgressUpdater.Start();
            var cue = mainViewModel?.Cues.FirstOrDefault(x => x.QID == Target);
            if(cue != null)
            {
                if (cue is SoundCueViewModel soundCue)
                    soundCue.Fade(Volume, FadeTime, FadeType);
                else
                    Stop();
            } else
            {
                Stop();
            }
        }

        public override void Stop()
        {
            base.Stop();
            playbackProgressUpdater.Stop();
            PlaybackTime = TimeSpan.Zero;
        }

        public override void Pause()
        {
            // Pausing isn't supported on stop cues
            //base.Pause();
            Stop();
        }

        public override void ToModel(string propertyName)
        {
            base.ToModel(propertyName);
            if (cueModel is VolumeCue scue)
            {
                switch (propertyName)
                {
                    case nameof(Target): scue.soundQid = Target; break;
                    case nameof(Volume): scue.volume = Volume; break;
                    case nameof(FadeTime): scue.fadeTime = FadeTime; break;
                    case nameof(FadeType): scue.fadeType = FadeType; break;
                }
            }
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
            if (cue is VolumeCue scue)
            {
                scue.soundQid = Target;
                scue.volume = Volume;
                scue.fadeTime = FadeTime;
                scue.fadeType = FadeType;
            }
        }

        public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            VolumeCueViewModel vm = new(mainViewModel);
            if (cue is VolumeCue scue)
            {
                vm.Target = scue.soundQid;
                vm.Volume = scue.volume;
                vm.FadeTime = scue.fadeTime;
                vm.FadeType = scue.fadeType;
            }
            return vm;
        }
    }
}
