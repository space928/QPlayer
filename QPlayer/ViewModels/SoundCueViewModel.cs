using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Timers;
using Cue = QPlayer.Models.Cue;

namespace QPlayer.ViewModels
{
    public class SoundCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>, IDisposable
    {
        [Reactive] public string Path { get; set; } = string.Empty;
        [Reactive] public TimeSpan StartTime { get; set; }
        [Reactive] public TimeSpan PlaybackDuration { get; set; } = TimeSpan.Zero;
        [Reactive] public override TimeSpan Duration => PlaybackDuration == TimeSpan.Zero ? (audioFile?.TotalTime ?? TimeSpan.Zero) - StartTime : PlaybackDuration;
        [Reactive] public override TimeSpan PlaybackTime 
        { 
            get => IsAudioFileValid ? audioFile.CurrentTime - StartTime : TimeSpan.Zero;
            set
            {
                if(IsAudioFileValid) 
                    audioFile.CurrentTime = value + StartTime;
            }
        }
        [Reactive] public float Volume { get; set; }
        [Reactive] public float FadeIn { get; set; }
        [Reactive] public float FadeOut { get; set; }
        [Reactive] public RelayCommand OpenMediaFileCommand { get; private set; }

        private AudioFileReader? audioFile;
        private FadeInOutSampleProvider? fadeInOutProvider;
        private readonly Timer audioProgressUpdater;
        private readonly Timer fadeOutTimer;
        private readonly Timer fadeOutEndTimer;

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
            fadeOutEndTimer = new Timer
            {
                AutoReset = false
            };
            fadeOutEndTimer.Elapsed += (o, e) =>
            {
                fadeOutEndTimer.Enabled = false;
                synchronizationContext?.Post((x) => Stop(), null);
            };
            PropertyChanged += (o, e) =>
            {
                switch (e.PropertyName)
                {
                    case nameof(Path):
                        LoadAudioFile();
                        break;
                    case nameof(Volume):
                        if (audioFile != null)
                            audioFile.Volume = Volume;
                        break;
                    case nameof(StartTime):
                        OnPropertyChanged(nameof(Duration));
                        OnPropertyChanged(nameof(PlaybackTimeString));
                        OnPropertyChanged(nameof(PlaybackTimeStringShort));
                        break;
                }
            };
        }

        private void FadeOutTimer_Elapsed(object? sender, ElapsedEventArgs e)
        {
            synchronizationContext?.Post((x) => {
                if (fadeInOutProvider == null || mainViewModel == null)
                    return;

                switch (State)
                {
                    case CueState.Ready:
                    case CueState.Paused:
                        return;
                    case CueState.PlayingLooped:
                    case CueState.Playing:
                        break;
                    case CueState.Delay:
                        Stop();
                        break;
                }

                fadeInOutProvider.BeginFadeOut(FadeOut * 1000);
            }, null);
        }

        private void AudioProgressUpdater_Elapsed(object? sender, ElapsedEventArgs e)
        {
            synchronizationContext?.Post((x) => {
                OnPropertyChanged(nameof(PlaybackTime));
                OnPropertyChanged(nameof(PlaybackTimeString));
                OnPropertyChanged(nameof(PlaybackTimeStringShort));
            }, null);
        }

        /// <summary>
        /// Checks whether the current audio file is valid. This also checks that the underlying stream object still exists.
        /// </summary>
        [MemberNotNullWhen(true, nameof(audioFile))]
        private bool IsAudioFileValid
        {
            get 
            {
                try { return (audioFile?.Position ?? -1) >= 0; } catch { return false; }
            }
        }

        public void Dispose()
        {
            UnloadAudioFile();
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
            
            if (!IsAudioFileValid || fadeInOutProvider == null || mainViewModel == null)
                return;

            // There are a few edge cases where this can happen (notably when starting a cue while it's in the delay state).
            if (mainViewModel.AudioPlaybackManager.IsPlaying(fadeInOutProvider))
                StopAudio();

            audioProgressUpdater.Start();
            if (FadeOut > 0)
            {
                double fadeOutTime = (Duration - TimeSpan.FromSeconds(FadeOut) - audioFile.CurrentTime - StartTime).TotalMilliseconds;
                if (fadeOutTime <= int.MaxValue)
                {
                    fadeOutTimer.Interval = fadeOutTime;
                    fadeOutTimer.Start();
                }
            }
            if (fadeOutEndTimer.Enabled)
                fadeOutEndTimer.Start();
            mainViewModel.AudioPlaybackManager.PlaySound(fadeInOutProvider, (x)=>Stop());
            fadeInOutProvider.BeginFadeIn(Math.Max(FadeIn * 1000, 1000/(double)fadeInOutProvider.WaveFormat.SampleRate));
        }

        public override void Pause()
        {
            base.Pause();
            if (fadeInOutProvider == null || mainViewModel == null)
                return;
            mainViewModel.AudioPlaybackManager.StopSound(fadeInOutProvider);
            audioProgressUpdater.Stop();
            fadeOutTimer.Stop();
            fadeOutEndTimer.Stop();
        }

        public override void Stop()
        {
            base.Stop();
            StopAudio();
        }

        /// <summary>
        /// Fades out the current playing sound
        /// </summary>
        /// <param name="duration"></param>
        public void FadeOutAndStop(float duration)
        {
            if (fadeInOutProvider == null || mainViewModel == null)
                return;
            switch (State)
            {
                case CueState.Ready:
                case CueState.Paused:
                    return;
                case CueState.PlayingLooped:
                case CueState.Playing:
                    break;
                case CueState.Delay:
                    Stop();
                    break;
            }

            if (duration == 0)
            {
                Stop();
                return;
            }

            fadeInOutProvider.BeginFadeOut(duration * 1000);
            fadeOutEndTimer.Interval = duration * 1000;
            fadeOutEndTimer.Enabled = true;
            fadeOutEndTimer.Start();
        }

        /// <summary>
        /// Stops all playing audio, stops the timers, and rewinds the audioFile to it's start time.
        /// </summary>
        private void StopAudio()
        {
            if (!IsAudioFileValid || fadeInOutProvider == null || mainViewModel == null)
                return;
            mainViewModel.AudioPlaybackManager.StopSound(fadeInOutProvider);
            PlaybackTime = TimeSpan.Zero;
            audioProgressUpdater.Stop();
            fadeOutTimer.Stop();
            fadeOutEndTimer.Stop();
        }

        private void UnloadAudioFile()
        {
            Stop();
            audioFile?.Dispose();
            audioFile = null;
            OnPropertyChanged(nameof(Duration));
            OnPropertyChanged(nameof(PlaybackTimeString));
            OnPropertyChanged(nameof(PlaybackTimeStringShort));
        }

        private void LoadAudioFile()
        {
            if (mainViewModel == null)
                return;

            UnloadAudioFile();

            var path = mainViewModel.ResolvePath(Path);
            // Empty paths should fail silently
            if (string.IsNullOrEmpty(path))
                return;
            if (!File.Exists(path))
            {
                MainViewModel.Log($"Sound file does not exist! Path: '{path}'", MainViewModel.LogLevel.Warning);
                return;
            }

            try
            {
                audioFile = new(path);
                OnPropertyChanged(nameof(Duration));
                PlaybackTime = TimeSpan.Zero;
                // For some reason these don't get raised automatically...
                OnPropertyChanged(nameof(PlaybackTimeString));
                OnPropertyChanged(nameof(PlaybackTimeStringShort));
                //if (PlaybackDuration == TimeSpan.Zero)
                //    PlaybackDuration = audioFile.TotalTime;
                audioFile.Volume = Volume;
                fadeInOutProvider = new(audioFile, true);
            } catch (Exception ex)
            {
                MainViewModel.Log($"Error while loading audio file ({path}): \n" + ex, MainViewModel.LogLevel.Error);
            }
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
