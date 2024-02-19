using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Linq;
using System.Timers;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels
{
    public class StopCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
    {
        [Reactive, ReactiveDependency(nameof(FadeOutTime))] 
        public override TimeSpan Duration => TimeSpan.FromSeconds(FadeOutTime);
        [Reactive] public decimal StopTarget { get; set; }
        [Reactive] public StopMode StopMode { get; set; }
        [Reactive] public float FadeOutTime { get; set; }

        private readonly Timer playbackProgressUpdater;
        private DateTime startTime;

        public StopCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
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
                    case nameof(FadeOutTime):
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
            // Stop cues don't support preloading
            PlaybackTime = TimeSpan.Zero;
            startTime = DateTime.Now;
            playbackProgressUpdater.Start();
            var cue = mainViewModel?.Cues.FirstOrDefault(x => x.QID == StopTarget);
            if(cue != null)
            {
                if (cue is SoundCueViewModel soundCue)
                    soundCue.FadeOutAndStop(FadeOutTime);
                else
                {
                    cue.Stop();
                    Stop();
                }
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
            if (cueModel is StopCue scue)
            {
                switch (propertyName)
                {
                    case nameof(StopTarget): scue.stopQid = StopTarget; break;
                    case nameof(StopMode): scue.stopMode = StopMode; break;
                    case nameof(FadeOutTime): scue.fadeOutTime = FadeOutTime; break;
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
                scue.fadeOutTime = FadeOutTime;
            }
        }

        public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            StopCueViewModel vm = new(mainViewModel);
            if (cue is StopCue scue)
            {
                vm.StopTarget = scue.stopQid;
                vm.StopMode = scue.stopMode;
                vm.FadeOutTime = scue.fadeOutTime;
            }
            return vm;
        }
    }
}
