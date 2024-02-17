using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Timers;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels
{
    public class SoundCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>, IDisposable
    {
        [Reactive] public string Path { get; set; } = string.Empty;
        [Reactive] public TimeSpan StartTime { get; set; }
        [Reactive] public TimeSpan PlaybackDuration { get; set; } = TimeSpan.Zero;
        [Reactive] public override TimeSpan Duration => PlaybackDuration == TimeSpan.Zero ? audioFile?.TotalTime ?? TimeSpan.Zero : PlaybackDuration;
        [Reactive] public float Volume { get; set; }
        [Reactive] public float FadeIn { get; set; }
        [Reactive] public float FadeOut { get; set; }
        [Reactive] public RelayCommand OpenMediaFileCommand { get; private set; }

        private AudioFileReader? audioFile;
        private FadeInOutSampleProvider? fadeInOutProvider;
        private readonly Timer audioProgressUpdater;
        private readonly Timer fadeOutTimer;

        public SoundCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
        {
            OpenMediaFileCommand = new(OpenMediaFileExecute);
            audioProgressUpdater = new Timer(100);
            audioProgressUpdater.Elapsed += AudioProgressUpdater_Elapsed;
            fadeOutTimer = new Timer
            {
                AutoReset = false
            };
            fadeOutTimer.Elapsed += FadeOutTimer_Elapsed;
            PropertyChanged += (o, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(Path):
                        LoadAudioFile();
                        break;
                    case nameof(Volume):
                        if(audioFile != null)
                            audioFile.Volume = Volume;
                        break;
                }
            };
        }

        private void FadeOutTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if (State != CueState.Playing || fadeInOutProvider == null || mainViewModel == null)
                return;

            fadeInOutProvider.BeginFadeOut(FadeOut * 1000);
        }

        private void AudioProgressUpdater_Elapsed(object? sender, ElapsedEventArgs e)
        {
            if(audioFile == null || mainViewModel == null) 
                return;

            PlaybackTime = audioFile.CurrentTime;
        }

        public void Dispose()
        {
            audioFile?.Dispose();
        }

        public void OpenMediaFileExecute()
        {
            OpenFileDialog openFileDialog = new()
            {
                Multiselect = false,
                Title = "Open Media File",
                CheckFileExists = true,
                FileName = Path,
                Filter = "Supported Media (*.wav;*.mp3;*.ogg;*.flac;*.aif;*.aiff;*.wma)|*.wav;*.mp3;*.ogg;*.flac;*.aif;*.aiff;*.wma|All files (*.*)|*.*"
            };
            if (openFileDialog.ShowDialog() ?? false)
            {
                Path = openFileDialog.FileName;
            }
        }

        public override void Go()
        {
            var oldState = State; // We need to capture the old state here since the base function writes to it
            base.Go();
            if (oldState == CueState.Playing || oldState == CueState.PlayingLooped)
                StopAudio();
            if (audioFile == null || fadeInOutProvider == null || mainViewModel == null)
                return;
            audioProgressUpdater.Start();
            if (FadeOut > 0)
            {
                double fadeOutTime = (Duration - TimeSpan.FromSeconds(FadeOut) - audioFile.CurrentTime).TotalMilliseconds;
                if (fadeOutTime <= int.MaxValue)
                {
                    fadeOutTimer.Interval = fadeOutTime;
                    fadeOutTimer.Start();
                }
            }
            mainViewModel.AudioPlaybackManager.PlaySound(fadeInOutProvider, (x)=>Stop());
            fadeInOutProvider.BeginFadeIn(Math.Max(FadeIn * 1000, 1000/(double)fadeInOutProvider.WaveFormat.SampleRate));
        }

        public override void Pause()
        {
            base.Pause();
            if (audioFile == null || fadeInOutProvider == null || mainViewModel == null)
                return;
            mainViewModel.AudioPlaybackManager.StopSound(fadeInOutProvider);
            audioProgressUpdater.Stop();
            fadeOutTimer.Stop();
        }

        public override void Stop()
        {
            base.Stop();
            StopAudio();
        }

        private void StopAudio()
        {
            if (audioFile == null || fadeInOutProvider == null || mainViewModel == null)
                return;
            mainViewModel.AudioPlaybackManager.StopSound(fadeInOutProvider);
            audioFile.CurrentTime = StartTime;
            PlaybackTime = TimeSpan.Zero;
            audioProgressUpdater.Stop();
            fadeOutTimer.Stop();
        }

        private void LoadAudioFile()
        {
            if (mainViewModel == null)
                return;

            Stop();
            audioFile?.Dispose();

            audioFile = new(mainViewModel.ResolvePath(Path));
            audioFile.CurrentTime = StartTime;
            OnPropertyChanged(nameof(Duration));
            PlaybackTime = TimeSpan.Zero;
            // For some reason these don't get raised automatically...
            OnPropertyChanged(nameof(PlaybackTimeString));
            OnPropertyChanged(nameof(PlaybackTimeStringShort));
            //if (PlaybackDuration == TimeSpan.Zero)
            //    PlaybackDuration = audioFile.TotalTime;
            audioFile.Volume = Volume;
            fadeInOutProvider = new(audioFile, true);
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
                    case nameof(PlaybackDuration): scue.duration = PlaybackDuration; break;
                    case nameof(Volume): scue.volume = Volume; break;
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
                scue.duration = PlaybackDuration;
                scue.volume = Volume;
                scue.fadeIn = FadeIn;
                scue.fadeOut = FadeOut;
            }
        }

        public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
        {
            SoundCueViewModel vm = new(mainViewModel);
            if(cue is SoundCue scue)
            {
                vm.Path = scue.path;
                vm.StartTime = scue.startTime;
                vm.PlaybackDuration = scue.duration;
                vm.Volume = scue.volume;
                vm.FadeIn = scue.fadeIn;
                vm.FadeOut = scue.fadeOut;
            }
            return vm;
        }
    }
}
