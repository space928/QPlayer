using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Utilities;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace QPlayer.ViewModels;

public class ShaderParamsCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
{
    [Reactive, ReactiveDependency(nameof(FadeTime))]
    public override TimeSpan Duration => TimeSpan.FromSeconds(FadeTime);
    [Reactive] public decimal Target { get; set; }
    [Reactive] public ObservableCollection<ShaderParameterViewModel> ShaderParameters { get; private set; } = [];
    [Reactive] public float FadeTime { get; set; }
    [Reactive] public FadeType FadeType { get; set; }

    [Reactive] public RelayCommand AddShaderParameterCommand { get; private set; }
    [Reactive] public RelayCommand<ShaderParameterViewModel> RemoveShaderParameterCommand { get; private set; }

    private readonly Timer playbackProgressUpdater;
    private DateTime startTime;

    public ShaderParamsCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
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

        // Propagate changes back to the model
        ShaderParameters.SyncList(() => (cueModel as ShaderParamsCue)?.uniforms, ShaderParameterViewModel.ToModel);

        AddShaderParameterCommand = new(() => {
            ShaderParameterViewModel param = new();
            param.PropertyChanged += (s, e) => ExtensionMethods.HandleCollectionValueChange(ShaderParameters, param);
            ShaderParameters.Add(param);
        });
        RemoveShaderParameterCommand = new(param =>
        {
            if (param == null)
                return;
            param.PropertyChanged -= (s, e) => ExtensionMethods.HandleCollectionValueChange(ShaderParameters, param);
            ShaderParameters.Remove(param);
        });
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
        /*var cue = mainViewModel?.Cues.FirstOrDefault(x => x.QID == Target);
        if (cue != null)
        {
            if (cue is SoundCueViewModel soundCue)
                soundCue.Fade(Volume, FadeTime, FadeType);
            else
                Stop();
        }
        else
        {
            Stop();
        }*/
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
        if (cueModel is ShaderParamsCue spcue)
        {
            switch (propertyName)
            {
                case nameof(Target): spcue.qid = Target; break;
                case nameof(ShaderParameters): /*vfcue.corners = Corners;*/ break;
                case nameof(FadeTime): spcue.fadeTime = FadeTime; break;
                case nameof(FadeType): spcue.fadeType = FadeType; break;
            }
        }
    }

    public override void ToModel(Cue cue)
    {
        base.ToModel(cue);
        if (cue is ShaderParamsCue spcue)
        {
            spcue.targetQid = Target;
            spcue.uniforms = ShaderParameters.Select(ShaderParameterViewModel.ToModel).ToList();
            spcue.fadeTime = FadeTime;
            spcue.fadeType = FadeType;
        }
    }

    public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
    {
        ShaderParamsCueViewModel vm = new(mainViewModel);
        if (cue is ShaderParamsCue spcue)
        {
            vm.Target = spcue.targetQid;
            vm.ShaderParameters.Clear();
            for (int i = 0; i < spcue.uniforms.Count; i++)
            {
                ShaderParameterViewModel param = new(spcue.uniforms[i]);
                param.PropertyChanged += (s, e) => ExtensionMethods.HandleCollectionValueChange(vm.ShaderParameters, param);
                vm.ShaderParameters.Add(param);
            }
            vm.FadeTime = spcue.fadeTime;
            vm.FadeType = spcue.fadeType;
        }
        return vm;
    }
}
