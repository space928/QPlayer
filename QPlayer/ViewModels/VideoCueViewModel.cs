using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Video;
using QPlayer.Views;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Win32;
using static QPlayer.ViewModels.MainViewModel;

namespace QPlayer.ViewModels;

public class VideoCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>, IDisposable
{
    #region Bindable Properties
    [Reactive] public string Path { get; set; } = string.Empty;
    [Reactive] public string Shader { get; set; } = string.Empty;
    [Reactive] public int ZIndex { get; set; }
    [Reactive] public string AlphaPath { get; set; } = string.Empty;
    [Reactive] public AlphaMode AlphaMode { get; set; }
    [Reactive] public TimeSpan StartTime { get; set; }
    [Reactive] public TimeSpan PlaybackDuration { get; set; }
    [Reactive] public override TimeSpan Duration => PlaybackDuration == TimeSpan.Zero ? (videoFile?.Duration ?? TimeSpan.Zero) - StartTime : PlaybackDuration;
    [Reactive]
    public override TimeSpan PlaybackTime
    {
        get => (videoFile?.IsReady ?? false) ? videoFile.CurrentTime - StartTime : TimeSpan.Zero;
        set
        {
            if (videoFile?.IsReady ?? false)
                videoFile.CurrentTime = value + StartTime;
        }
    }
    [Reactive] public float Dimmer { get; set; }
    [Reactive] public float Volume { get; set; }
    [Reactive] public float FadeIn { get; set; }
    [Reactive] public float FadeOut { get; set; }
    [Reactive] public FadeType FadeType { get; set; }

    [Reactive] public float Brightness { get; set; }
    [Reactive] public float Contrast { get; set; }
    [Reactive] public float Gamma { get; set; }

    [Reactive] public float Scale { get; set; }
    [Reactive] public float Rotation { get; set; }
    [Reactive] public float XPos { get; set; }
    [Reactive] public float YPos { get; set; }

    [Reactive] public RelayCommand OpenMediaFileCommand { get; private set; }
    [Reactive] public RelayCommand OpenAlphaMediaFileCommand { get; private set; }
    [Reactive] public RelayCommand OpenShaderFileCommand { get; private set; }

    //[Reactive] public WaveFormRenderer WaveForm => waveFormRenderer;

    [Reactive] public static ObservableCollection<AlphaMode>? AlphaModeVals { get; private set; }
    #endregion

    private readonly Timer playbackProgressUpdater;
    private readonly Timer fadeOutTimer;
    private VideoFile? videoFile;
    private VideoFile? alphaVideoFile;
    private bool shouldSendRemoteStatus;
    private string? thisNodeName;

    public VideoCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        OpenMediaFileCommand = new(OpenMediaFileExecute);
        OpenAlphaMediaFileCommand = new(OpenAlphaMediaFileExecute);
        OpenShaderFileCommand = new(OpenShaderFileExecute);
        playbackProgressUpdater = new Timer(50);
        playbackProgressUpdater.Elapsed += AudioProgressUpdater_Elapsed;
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
                    LoadVideoFile();
                    break;
                case nameof(Volume):
                    break;
                case nameof(StartTime):
                    OnPropertyChanged(nameof(Duration));
                    OnPropertyChanged(nameof(PlaybackTimeString));
                    OnPropertyChanged(nameof(PlaybackTimeStringShort));
                    break;
            }
        };

        AlphaModeVals = new(Enum.GetValues<AlphaMode>());
    }

    public void Dispose()
    {
        UnloadVideoFile();
    }

    internal override void OnFocussed()
    {
        base.OnFocussed();
    }

    public void OpenMediaFileExecute()
    {
        OpenFileDialog openFileDialog = new()
        {
            Multiselect = false,
            Title = "Open Video File",
            CheckFileExists = true,
            FileName = Path,
            Filter = "Supported Videos (*.mp4;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.mov)|*.mp4;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.mov|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() ?? false)
        {
            Path = openFileDialog.FileName;
        }
    }

    public void OpenAlphaMediaFileExecute()
    {
        OpenFileDialog openFileDialog = new()
        {
            Multiselect = false,
            Title = "Open Alpha Video File",
            CheckFileExists = true,
            FileName = AlphaPath,
            Filter = "Supported Videos (*.mp4;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.mov)|*.mp4;*.mkv;*.avi;*.webm;*.flv;*.wmv;*.mov|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() ?? false)
        {
            AlphaPath = openFileDialog.FileName;
        }
    }

    public void OpenShaderFileExecute()
    {
        OpenFileDialog openFileDialog = new()
        {
            Multiselect = false,
            Title = "Open Shader File",
            CheckFileExists = false,
            FileName = Shader,
            Filter = "Supported Shaders (*.glsl;*.ps)|*.glsl;*.ps|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() ?? false)
        {
            Shader = openFileDialog.FileName;
        }
    }

    public override void Go()
    {
        var oldState = State; // We need to capture the old state here since the base function writes to it
        base.Go();
        if (IsRemoteControlled)
            return;
        if (oldState == CueState.Playing || oldState == CueState.PlayingLooped)
            StopVideo();

        Log($"Local video playback is not yet supported!", LogLevel.Warning);

        if (!(videoFile?.IsReady ?? false) || mainViewModel == null)
            return;

        // There are a few edge cases where this can happen (notably when starting a cue while it's in the delay state).
        //if (mainViewModel.AudioPlaybackManager.IsPlaying(fadeInOutProvider))
        //    StopVideo();

        shouldSendRemoteStatus = mainViewModel.ProjectSettings.EnableRemoteControl
            && (string.IsNullOrEmpty(RemoteNode) || RemoteNode == mainViewModel.ProjectSettings.NodeName);
        thisNodeName = mainViewModel.ProjectSettings.NodeName;
        playbackProgressUpdater.Start();
        if (FadeOut > 0)
        {
            double fadeOutTime = (Duration - TimeSpan.FromSeconds(FadeOut) - videoFile.CurrentTime + StartTime).TotalMilliseconds;
            if (fadeOutTime > 0 && fadeOutTime <= int.MaxValue)
            {
                fadeOutTimer.Interval = fadeOutTime;
                fadeOutTimer.Start();
            }
        }
        //var dbg_t = DateTime.Now;
        //MainViewModel.Log($"[Playback Debugging] Cue about to start! {dbg_t:HH:mm:ss.ffff} dt={(dbg_t-MainViewModel.dbg_cueStartTime)}");
        //mainViewModel.AudioPlaybackManager.PlaySound(fadeInOutProvider, (x) => Stop());
        //fadeInOutProvider.Volume = 0;
        //fadeInOutProvider.BeginFade(Volume, Math.Max(FadeIn * 1000, 1000 / (double)fadeInOutProvider.WaveFormat.SampleRate), FadeType);
    }

    public override void Pause()
    {
        base.Pause();
        if (IsRemoteControlled || mainViewModel == null)
            return;
        //mainViewModel.AudioPlaybackManager.StopSound(fadeInOutProvider);
        playbackProgressUpdater.Stop();
        fadeOutTimer.Stop();
        OnPropertyChanged(nameof(PlaybackTime));
        OnPropertyChanged(nameof(PlaybackTimeString));
        OnPropertyChanged(nameof(PlaybackTimeStringShort));
    }

    public override void Stop()
    {
        base.Stop();
        if (IsRemoteControlled)
            return;
        if (shouldSendRemoteStatus)
            mainViewModel?.OSCManager?.SendRemoteStatus(RemoteNode, qid, State);
        StopVideo();
    }

    /// <summary>
    /// Fades out the current playing sound.
    /// </summary>
    /// <param name="duration">The duration in seconds to fade over</param>
    /// <param name="fadeType">The type of fade to use</param>
    public void FadeOutAndStop(float duration, FadeType? fadeType = null)
    {
        //if (fadeInOutProvider == null || mainViewModel == null)
        //    return;
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

        //fadeInOutProvider.BeginFade(0, duration * 1000, fadeType ?? FadeType, FadeOut_Completed);
    }

    /// <summary>
    /// Fades this sound to the given volume in a given amount of time.
    /// </summary>
    /// <param name="volume">The volume to fade to</param>
    /// <param name="duration">The duration in seconds to fade over</param>
    /// <param name="fadeType">The type of fade to use</param>
    public void Fade(float volume, float duration, FadeType? fadeType = null)
    {
        //if (fadeInOutProvider == null || mainViewModel == null)
        //    return;
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

        //fadeInOutProvider.BeginFade(volume, duration * 1000, fadeType ?? FadeType);
    }

    private void FadeOutTimer_Elapsed(object? sender, ElapsedEventArgs e)
    {
        synchronizationContext?.Post((x) =>
        {
            //if (fadeInOutProvider == null || mainViewModel == null)
            //    return;

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

            //fadeInOutProvider.BeginFade(0, FadeOut * 1000, FadeType, FadeOut_Completed);
        }, null);
    }

    private void AudioProgressUpdater_Elapsed(object? sender, ElapsedEventArgs e)
    {
        synchronizationContext?.Post((x) =>
        {
            OnPropertyChanged(nameof(PlaybackTime));
            OnPropertyChanged(nameof(PlaybackTimeString));
            OnPropertyChanged(nameof(PlaybackTimeStringShort));

            // When not using a fadeout, there's nothing to stop the sound early if it's been trimmed.
            // This won't be very accurate, but should work for now...
            if (PlaybackTime >= Duration)
                Stop();

            if (shouldSendRemoteStatus)
                mainViewModel?.OSCManager?.SendRemoteStatus(thisNodeName ?? string.Empty, qid, State,
                    State != CueState.Ready ? (float)PlaybackTime.TotalSeconds : null);
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
    private void StopVideo()
    {
        if (!(videoFile?.IsReady ?? false) || mainViewModel == null)
            return;
        //mainViewModel.AudioPlaybackManager.StopSound(fadeInOutProvider);
        PlaybackTime = TimeSpan.Zero;
        playbackProgressUpdater.Stop();
        fadeOutTimer.Stop();
    }

    public void LoadVideoFile()
    {
        //if (!IsRemoteControlled)
        //    Log($"");

        videoFile = new();
        alphaVideoFile = new();
    }

    public void UnloadVideoFile()
    {
        videoFile = null;
        alphaVideoFile = null;
    }

    #region Model Synchronisation

    public override void ToModel(string propertyName)
    {
        base.ToModel(propertyName);
        if (cueModel is VideoCue vcue)
        {
            switch (propertyName)
            {
                case nameof(Path): vcue.path = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Path), false) ?? Path; break;
                case nameof(Shader): vcue.shader = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Shader), false) ?? Shader; break;
                case nameof(ZIndex): vcue.zIndex = ZIndex; break;
                case nameof(AlphaPath): vcue.alphaPath = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(AlphaPath), false) ?? AlphaPath; break;
                case nameof(AlphaMode): vcue.alphaMode = AlphaMode; break;
                case nameof(StartTime): vcue.startTime = StartTime; break;
                case nameof(PlaybackDuration): vcue.duration = PlaybackDuration; break;
                case nameof(Dimmer): vcue.dimmer = Dimmer; break;
                case nameof(Volume): vcue.volume = Volume; break;
                case nameof(FadeIn): vcue.fadeIn = FadeIn; break;
                case nameof(FadeOut): vcue.fadeOut = FadeOut; break;
                case nameof(FadeType): vcue.fadeType = FadeType; break;
                case nameof(Brightness): vcue.brightness = Brightness; break;
                case nameof(Contrast): vcue.contrast = Contrast; break;
                case nameof(Gamma): vcue.gamma = Gamma; break;
                case nameof(Scale): vcue.scale = Scale; break;
                case nameof(Rotation): vcue.rotation = Rotation; break;
                case nameof(XPos): vcue.offset = new(XPos, vcue.offset.Y); break;
                case nameof(YPos): vcue.offset = new(vcue.offset.X, YPos); break;
            }
        }
    }

    public override void ToModel(Cue cue)
    {
        base.ToModel(cue);
        if (cue is VideoCue vcue)
        {
            vcue.path = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Path), false) ?? Path;
            vcue.shader = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(Shader), false) ?? Shader;
            vcue.zIndex = ZIndex;
            vcue.alphaPath = MainViewModel?.ResolvePath(MainViewModel.ResolvePath(AlphaPath), false) ?? AlphaPath;
            vcue.alphaMode = AlphaMode;
            vcue.startTime = StartTime;
            vcue.duration = PlaybackDuration;
            vcue.dimmer = Dimmer;
            vcue.volume = Volume;
            vcue.fadeIn = FadeIn;
            vcue.fadeOut = FadeOut;
            vcue.fadeType = FadeType;
            vcue.brightness = Brightness;
            vcue.contrast = Contrast;
            vcue.gamma = Gamma;
            vcue.scale = Scale;
            vcue.rotation = Rotation;
            vcue.offset = new(XPos, vcue.offset.Y);
            vcue.offset = new(vcue.offset.X, YPos);
        }
    }

    public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
    {
        VideoCueViewModel vm = new(mainViewModel);
        if (cue is VideoCue vcue)
        {
            vm.Path = vcue.path;
            vm.Shader = vcue.shader;
            vm.ZIndex = vcue.zIndex;
            vm.AlphaPath = vcue.alphaPath ?? string.Empty;
            vm.AlphaMode = vcue.alphaMode;
            vm.StartTime = vcue.startTime;
            vm.PlaybackDuration = vcue.duration;
            vm.Dimmer = vcue.dimmer;
            vm.Volume = vcue.volume;
            vm.FadeIn = vcue.fadeIn;
            vm.FadeOut = vcue.fadeOut;
            vm.FadeType = vcue.fadeType;
            vm.Brightness = vcue.brightness;
            vm.Contrast = vcue.contrast;
            vm.Gamma = vcue.gamma;
            vm.Rotation = vcue.rotation;
            vm.XPos = vcue.offset.X;
            vm.YPos = vcue.offset.Y;
        }
        return vm;
    }
    #endregion
}
