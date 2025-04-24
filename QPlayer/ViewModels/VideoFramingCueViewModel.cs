using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.Utilities;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using System.Timers;
using System.Windows.Media;

namespace QPlayer.ViewModels;

public class VideoFramingCueViewModel : CueViewModel, IConvertibleModel<Cue, CueViewModel>
{
    [Reactive, ReactiveDependency(nameof(FadeTime))]
    public override TimeSpan Duration => TimeSpan.FromSeconds(FadeTime);
    //[Reactive] public decimal Target { get; set; }
    //[Reactive] public ObservableCollection<Vector2ViewModel> Corners { get; private set; } = [];
    [Reactive] public CornersViewModel Corners { get; private set; } = new();
    [Reactive] public ObservableCollection<FramingShutterViewModel> Framing { get; private set; } = [];
    [Reactive] public float FadeTime { get; set; }
    [Reactive] public FadeType FadeType { get; set; }

    [Reactive] public RelayCommand AddFramingShutterCommand { get; private set; }
    [Reactive] public RelayCommand<FramingShutterViewModel> RemoveFramingShutterCommand { get; private set; }

    private readonly Timer playbackProgressUpdater;
    private DateTime startTime;

    public VideoFramingCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
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
        Corners.PropertyChanged += (o, e) =>
        {
            if (cueModel is VideoFramingCue vfCue)
                Corners.ToModel(vfCue.corners);
        };
        Framing.SyncList(() => (cueModel as VideoFramingCue)?.framing, FramingShutterViewModel.ToModel);

        AddFramingShutterCommand = new(() => {
            FramingShutterViewModel shutter = new();
            int ind = Framing.Count;
            shutter.PropertyChanged += (s, e) => HandleCollectionValueChange(Framing, shutter);
            Framing.Add(shutter);
        });
        RemoveFramingShutterCommand = new(shutter =>
        {
            if (shutter == null)
                return;
            /*int ind = Framing.IndexOf(shutter);
            if (ind == -1)
                return;*/
            shutter.PropertyChanged -= (s, e) => HandleCollectionValueChange(Framing, shutter);
            //Framing.RemoveAt(ind);
            Framing.Remove(shutter);
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
        if (cueModel is VideoFramingCue vfcue)
        {
            switch (propertyName)
            {
                //case nameof(Target): scue.soundQid = Target; break;
                case nameof(Corners): /*vfcue.corners = Corners;*/ break;
                case nameof(Framing): /*vfcue.framing = Framing;*/ break;
                case nameof(FadeTime): vfcue.fadeTime = FadeTime; break;
                case nameof(FadeType): vfcue.fadeType = FadeType; break;
            }
        }
    }

    public override void ToModel(Cue cue)
    {
        base.ToModel(cue);
        if (cue is VideoFramingCue vfcue)
        {
            Corners.ToModel(vfcue.corners);
            vfcue.framing = Framing.Select(FramingShutterViewModel.ToModel).ToList();
            vfcue.fadeTime = FadeTime;
            vfcue.fadeType = FadeType;
        }
    }

    public static new CueViewModel FromModel(Cue cue, MainViewModel mainViewModel)
    {
        VideoFramingCueViewModel vm = new(mainViewModel);
        if (cue is VideoFramingCue vfcue)
        {
            //vm.Target = vfcue.soundQid;
            vm.Corners.FromModel(vfcue.corners);
            vm.Framing.Clear();
            for (int i = 0; i < vfcue.framing.Count; i++)
            {
                FramingShutterViewModel framing = new(vfcue.framing[i]);
                framing.PropertyChanged += (s, e) => HandleCollectionValueChange(vm.Framing, framing);
                vm.Framing.Add(framing);
            }
            vm.FadeTime = vfcue.fadeTime;
            vm.FadeType = vfcue.fadeType;
        }
        return vm;
    }

    private static void HandleCollectionValueChange<T>(ObservableCollection<T> collection, T obj)
    {
        // This should trigger a Replace collection changed notification
        // TODO: This is a hopelessly stupid way of doing this
        int ind = collection.IndexOf(obj);
        collection[ind] = collection[ind];
    }
}

public class FramingShutterViewModel : ObservableObject
{
    [Reactive] public float Rotation { get; set; }
    [Reactive] public float MaskStart { get; set; }
    [Reactive] public float Softness { get; set; }

    public FramingShutterViewModel() { }

    public FramingShutterViewModel(FramingShutter model) : this(model.rotation, model.maskStart, model.softness) { }

    public FramingShutterViewModel(float rotation, float maskStart, float softness)
    {
        Rotation = rotation;
        MaskStart = maskStart;
        Softness = softness;
    }

    public static FramingShutter ToModel(FramingShutterViewModel vm) => new()
    {
        rotation = vm.Rotation,
        maskStart = vm.MaskStart,
        softness = vm.Softness,
    };
}

public class Vector2ViewModel : ObservableObject
{
    [Reactive] public float X { get; set; }
    [Reactive] public float Y { get; set; }

    public Vector2ViewModel() { }

    public Vector2ViewModel(Vector2 model) : this(model.X, model.Y) { }

    public Vector2ViewModel(float x, float y)
    {
        X = x;
        Y = y;
    }

    public static Vector2 ToModel(Vector2ViewModel vm) => new()
    {
        X = vm.X,
        Y = vm.Y,
    };
}

public class CornersViewModel : ObservableObject
{
    [Reactive] public float TL_X { get; set; } = 0;
    [Reactive] public float TL_Y { get; set; } = 0;
    [Reactive] public float TR_X { get; set; } = 1;
    [Reactive] public float TR_Y { get; set; } = 0;
    [Reactive] public float BL_X { get; set; } = 0;
    [Reactive] public float BL_Y { get; set; } = 1;
    [Reactive] public float BR_X { get; set; } = 1;
    [Reactive] public float BR_Y { get; set; } = 1;

    public CornersViewModel() { }

    public CornersViewModel(IList<Vector2> model)
    {
        if (model.Count < 4)
            return;

        TL_X = model[0].X;
        TL_Y = model[0].Y;
        TR_X = model[1].X;
        TR_Y = model[1].Y;
        BL_X = model[2].X;
        BL_Y = model[2].Y;
        BR_X = model[3].X;
        BR_Y = model[3].Y;
    }

    public void FromModel(IList<Vector2> model)
    {
        if (model.Count < 4)
            return;

        TL_X = model[0].X;
        TL_Y = model[0].Y;
        TR_X = model[1].X;
        TR_Y = model[1].Y;
        BL_X = model[2].X;
        BL_Y = model[2].Y;
        BR_X = model[3].X;
        BR_Y = model[3].Y;
    }

    public void ToModel(IList<Vector2> model)
    {
        while (model.Count < 4)
            model.Add(default);

        model[0] = new(TL_X, TL_Y);
        model[1] = new(TR_X, TR_Y);
        model[2] = new(BL_X, BL_Y);
        model[3] = new(BR_X, BR_Y);
    }
}
