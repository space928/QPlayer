using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using NAudio.Wave;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Views;
using QPlayer.SourceGenerator;
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using System.Timers;

namespace QPlayer.ViewModels;

[Model(typeof(SoundCue))]
[View(typeof(CueEditor))]
public partial class SoundCueViewModel : CueViewModel
{
    [Reactive, ModelCustomBinding(nameof(VM2M_Path), null)] private string path = string.Empty;
    [Reactive, ChangesProp(nameof(Duration))] private TimeSpan startTime;
    [Reactive, ModelBindsTo(nameof(SoundCue.duration)), ChangesProp(nameof(Duration))] private TimeSpan playbackDuration = TimeSpan.Zero;
    public override TimeSpan Duration
    {
        get
        {
            if (LoopMode == LoopMode.LoopedInfinite)
                return TimeSpan.FromTicks(1);

            return (loopingAudioStream?.TotalTime ?? TimeSpan.Zero);
        }
    }
    public override TimeSpan PlaybackTime
    {
        get => IsAudioFileValid ? loopingAudioStream.CurrentTime : TimeSpan.Zero;
        set
        {
            if (IsAudioFileValid)
                loopingAudioStream.CurrentTime = value;
            base.PlaybackTime = value;
        }
    }
    public TimeSpan SamplePlaybackTime => IsAudioFileValid ? loopingAudioStream.SrcCurrentTime : TimeSpan.Zero;
    public TimeSpan SampleDuration => (loopingAudioStream?.SrcTotalTime ?? TimeSpan.Zero);
    [Reactive] private float volume;
    [Reactive] private float fadeIn;
    [Reactive] private float fadeOut;
    [Reactive] private FadeType fadeType;

    [Reactive, PrivateSetter, ModelSkip] private RelayCommand openMediaFileCommand;
    [Reactive("EQ"), PrivateSetter] private EQViewModel eq;
    public WaveFormRenderer WaveForm => waveFormRenderer;

    private bool shouldSendRemoteStatus;
    private string? thisNodeName;
    private AudioFileReader? audioFile;
    private LoopingSampleProvider<AudioFileReader>? loopingAudioStream;
    private FadingSampleProvider? fadeInOutProvider;
    //private readonly Timer audioProgressUpdater;
    private readonly Timer fadeOutTimer;
    private readonly WaveFormRenderer waveFormRenderer;

    public SoundCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        OpenMediaFileCommand = new(OpenMediaFileExecute);
        /*audioProgressUpdater = new Timer(50);
        audioProgressUpdater.Elapsed += AudioProgressUpdater_Elapsed;*/
        EQ = new();
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
                    if (loopingAudioStream != null)
                    {
                        loopingAudioStream.StartTime = StartTime;
                        loopingAudioStream.EndTime = PlaybackDuration + StartTime;
                    }
                    break;
                case nameof(PlaybackDuration):
                    if (loopingAudioStream != null)
                        loopingAudioStream.EndTime = PlaybackDuration + StartTime;
                    break;
                case nameof(LoopMode):
                case nameof(LoopCount):
                    if (loopingAudioStream != null)
                    {
                        loopingAudioStream.Infinite = LoopMode == LoopMode.LoopedInfinite;
                        loopingAudioStream.Loops = LoopMode == LoopMode.OneShot ? 1 : LoopCount;
                        OnPropertyChanged(nameof(Duration));
                    }
                    break;
                case nameof(Duration):
                    OnPropertyChanged(nameof(SampleDuration));
                    OnPropertyChanged(nameof(PlaybackTime));
                    break;
                case nameof(PlaybackTime):
                    OnPropertyChanged(nameof(SamplePlaybackTime));
                    break;
            }
        };
        waveFormRenderer = new WaveFormRenderer(this);
    }

    /// <summary>
    /// Checks whether the current audio file is valid. This also checks that the underlying stream object still exists.
    /// </summary>
    [MemberNotNullWhen(true, nameof(audioFile))]
    [MemberNotNullWhen(true, nameof(loopingAudioStream))]
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

    internal override void OnFocussed()
    {
        base.OnFocussed();

        WaveForm.Update();
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
        if (IsRemoteControlling)
            return;
        if (oldState == CueState.Playing || oldState == CueState.PlayingLooped)
            StopAudio();

        if (!IsAudioFileValid || fadeInOutProvider == null || mainViewModel == null)
            return;

        // There are a few edge cases where this can happen (notably when starting a cue while it's in the delay state).
        if (mainViewModel.AudioPlaybackManager.IsPlaying(fadeInOutProvider))
            StopAudio();

        shouldSendRemoteStatus = mainViewModel.ProjectSettings.EnableRemoteControl
            && (string.IsNullOrEmpty(RemoteNode) || RemoteNode == mainViewModel.ProjectSettings.NodeName);
        thisNodeName = mainViewModel.ProjectSettings.NodeName;

        if (oldState != CueState.Paused)
        {
            loopingAudioStream.Reset();
            loopingAudioStream.StartTime = StartTime;
            loopingAudioStream.EndTime = StartTime + PlaybackDuration;
        }

        //audioProgressUpdater.Start();
        if (FadeOut > 0)
        {
            double fadeOutTime = (Duration - TimeSpan.FromSeconds(FadeOut) - loopingAudioStream.CurrentTime).TotalMilliseconds;
            if (fadeOutTime > 0 && fadeOutTime <= int.MaxValue)
            {
                fadeOutTimer.Interval = fadeOutTime;
                fadeOutTimer.Start();
            }
        }
        //var dbg_t = DateTime.Now;
        //MainViewModel.Log($"[Playback Debugging] Cue about to start! {dbg_t:HH:mm:ss.ffff} dt={(dbg_t-MainViewModel.dbg_cueStartTime)}");
        mainViewModel.AudioPlaybackManager.PlaySound(fadeInOutProvider, (x) => Stop());
        fadeInOutProvider.Volume = 0;
        fadeInOutProvider.BeginFade(Volume, Math.Max(FadeIn * 1000, 1000 / (double)fadeInOutProvider.WaveFormat.SampleRate), FadeType);
    }

    public override void Pause()
    {
        base.Pause();
        if (IsRemoteControlling || fadeInOutProvider == null || mainViewModel == null)
            return;
        mainViewModel.AudioPlaybackManager.StopSound(fadeInOutProvider);
        //audioProgressUpdater.Stop();
        fadeOutTimer.Stop();
        OnPropertyChanged(nameof(PlaybackTime));
    }

    public override void Stop()
    {
        base.Stop();
        if (IsRemoteControlling)
            return;
        if (shouldSendRemoteStatus)
            mainViewModel?.OSCManager?.SendRemoteStatus(RemoteNode, qid, State);
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
        synchronizationContext?.Post((x) =>
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

            fadeInOutProvider.BeginFade(0, FadeOut * 1000, FadeType, FadeOut_Completed);
        }, null);
    }

    internal override void UpdateUIStatus()
    {
        OnPropertyChanged(nameof(PlaybackTime));

        if (shouldSendRemoteStatus)
            mainViewModel?.OSCManager?.SendRemoteStatus(thisNodeName ?? string.Empty, qid, State,
                State != CueState.Ready ? (float)PlaybackTime.TotalSeconds : null);
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
        //audioProgressUpdater.Stop();
        fadeOutTimer.Stop();
    }

    private void UnloadAudioFile()
    {
        Stop();
        audioFile?.Dispose();
        audioFile = null;
        OnPropertyChanged(nameof(Duration));
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

            // Hook up the different sample providers.
            loopingAudioStream = new(audioFile, LoopMode == LoopMode.LoopedInfinite, LoopMode != LoopMode.OneShot ? LoopCount : 1);
            // The EQ sample provider isn't compatible with the resampler (if the resampler comes after it in the chain)
            // Hence resampling must happen first.
            EQ.InputSampleProvider = mainViewModel.AudioPlaybackManager.ConvertToMixerFormat(loopingAudioStream);
            fadeInOutProvider = new(EQ.EQSampleProvider!, true);

            PlaybackTime = TimeSpan.Zero;

            OnPropertyChanged(nameof(Duration));

            Task.Run(async () =>
            {
                return await PeakFileWriter.LoadOrGeneratePeakFile(path);
            }).ContinueWith(x =>
            {
                // Make sure this happens on the UI thread...
                synchronizationContext?.Post(x =>
                {
                    waveFormRenderer.PeakFile = (PeakFile?)x;
                }, x.Result);
            });
        }
        catch (Exception ex)
        {
            MainViewModel.Log($"Error while loading audio file ({path}): \n" + ex, MainViewModel.LogLevel.Error);
        }
    }

    private static void VM2M_Path(SoundCueViewModel vm, SoundCue m) => m.path = vm.MainViewModel?.ResolvePath(vm.MainViewModel.ResolvePath(vm.Path), false) ?? vm.Path;
}
