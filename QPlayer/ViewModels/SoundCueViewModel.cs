using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Media;
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
                if (IsAudioFileValid)
                    audioFile.CurrentTime = value + StartTime;
            }
        }
        [Reactive] public float Volume { get; set; }
        [Reactive] public float FadeIn { get; set; }
        [Reactive] public float FadeOut { get; set; }
        [Reactive] public FadeType FadeType { get; set; }
        [Reactive] public RelayCommand OpenMediaFileCommand { get; private set; }

        [Reactive] public WaveFormRenderer WaveForm => waveFormRenderer;

        private AudioFileReader? audioFile;
        private FadingSampleProvider? fadeInOutProvider;
        private readonly Timer audioProgressUpdater;
        private readonly Timer fadeOutTimer;
        private readonly WaveFormRenderer waveFormRenderer;

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
                        if (fadeInOutProvider != null)
                            fadeInOutProvider.Volume = Volume;
                        break;
                    case nameof(StartTime):
                        OnPropertyChanged(nameof(Duration));
                        OnPropertyChanged(nameof(PlaybackTimeString));
                        OnPropertyChanged(nameof(PlaybackTimeStringShort));
                        break;
                }
            };
            waveFormRenderer = new WaveFormRenderer();
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
                double fadeOutTime = (Duration - TimeSpan.FromSeconds(FadeOut) - audioFile.CurrentTime + StartTime).TotalMilliseconds;
                if (fadeOutTime > 0 && fadeOutTime <= int.MaxValue)
                {
                    fadeOutTimer.Interval = fadeOutTime;
                    fadeOutTimer.Start();
                }
            }
            mainViewModel.AudioPlaybackManager.PlaySound(fadeInOutProvider, (x)=>Stop());
            fadeInOutProvider.BeginFade(1, Math.Max(FadeIn * 1000, 1000/(double)fadeInOutProvider.WaveFormat.SampleRate), FadeType);
        }

        public override void Pause()
        {
            base.Pause();
            if (fadeInOutProvider == null || mainViewModel == null)
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

        /// <summary>
        /// Fades out the current playing sound.
        /// </summary>
        /// <param name="duration">The duration in seconds to fade over</param>
        /// <param name="fadeType">The type of fade to use</param>
        public void FadeOutAndStop(float duration, FadeType? fadeType = null)
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

            fadeInOutProvider.BeginFade(0, duration * 1000, fadeType ?? FadeType, FadeOut_Completed);
        }

        /// <summary>
        /// Fades this sound to the given volume in a given amount of time.
        /// </summary>
        /// <param name="volume">The volume to fade to</param>
        /// <param name="duration">The duration in seconds to fade over</param>
        /// <param name="fadeType">The type of fade to use</param>
        public void Fade(float volume, float duration, FadeType? fadeType = null)
        {
            if (fadeInOutProvider == null || mainViewModel == null)
                return;
            switch (State)
            {
                case CueState.Ready:
                case CueState.Paused:
                case CueState.Delay: 
                    return;
                case CueState.PlayingLooped:
                case CueState.Playing:
                    break;
            }

            if (duration == 0)
            {
                return;
            }

            fadeInOutProvider.BeginFade(volume, duration * 1000, fadeType ?? FadeType);
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

                fadeInOutProvider.BeginFade(0, FadeOut * 1000, FadeType, FadeOut_Completed);
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

        private void FadeOut_Completed(bool completed)
        {
            if (completed)
                Stop();
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
                // audioFile.Volume = Volume;
                fadeInOutProvider = new(audioFile, true);

                Task.Run(async () =>
                {
                    return await PeakFileWriter.LoadOrGeneratePeakFile(path);
                }).ContinueWith(x =>
                {
                    waveFormRenderer.PeakFile = x.Result;
                });
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
                    case nameof(Path): scue.path = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Path), false) ?? Path; break;
                    case nameof(StartTime): scue.startTime = StartTime; break;
                    case nameof(PlaybackDuration): scue.duration = PlaybackDuration; break;
                    case nameof(Volume): scue.volume = Volume; break;
                    case nameof(FadeIn): scue.fadeIn = FadeIn; break;
                    case nameof(FadeOut): scue.fadeOut = FadeOut; break;
                    case nameof(FadeType): scue.fadeType = FadeType; break;
                }
            }
        }

        public override void ToModel(Cue cue)
        {
            base.ToModel(cue);
            if (cue is SoundCue scue)
            {
                scue.path = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Path), false) ?? Path;
                scue.startTime = StartTime;
                scue.duration = PlaybackDuration;
                scue.volume = Volume;
                scue.fadeIn = FadeIn;
                scue.fadeOut = FadeOut;
                scue.fadeType = FadeType;
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
                vm.FadeType = scue.fadeType;
            }
            return vm;
        }
    }
}
