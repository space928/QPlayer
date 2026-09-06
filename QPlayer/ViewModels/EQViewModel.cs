using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

[Model(typeof(EQSettings))]
public partial class EQViewModel : BindableViewModel<EQSettings>
{
    [Reactive] private bool enabled;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band1)}.{nameof(EQBand.freq)}")] private float lowFreq = 200;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band1)}.{nameof(EQBand.gain)}")] private float lowGain;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band2)}.{nameof(EQBand.freq)}")] private float lowMidFreq = 500;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band2)}.{nameof(EQBand.gain)}")] private float lowMidGain;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band3)}.{nameof(EQBand.freq)}")] private float highMidFreq = 2500;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band3)}.{nameof(EQBand.gain)}")] private float highMidGain;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band4)}.{nameof(EQBand.freq)}")] private float highFreq = 8000;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band4)}.{nameof(EQBand.gain)}")] private float highGain;

    [Reactive] private readonly RelayCommand copyCommand;
    [Reactive] private readonly RelayCommand pasteCommand;

    public ISamplePositionProvider? InputSampleProvider
    {
        get => inputSampleProvider;
        set
        {
            inputSampleProvider = value;
            if (value != null)
            {
                eqSampleProvider = new(value);
                ConfigureEQ();
            }
            else
            {
                eqSampleProvider = null;
            }
        }
    }
    public EQSampleProvider? EQSampleProvider => eqSampleProvider;

    private readonly CueViewModel owner;
    private EQSampleProvider? eqSampleProvider;
    private ISamplePositionProvider? inputSampleProvider;

    private static EQSettings? clipboard;

    public EQViewModel(CueViewModel owner)
    {
        this.owner = owner;
        copyCommand = new(Copy);
        pasteCommand = new(PasteSelected);
    }

    private void Copy()
    {
        var bound = BoundModel;
        if (clipboard == null)
            clipboard = new();
        Bind(clipboard);
        SyncToModel();
        Bind(bound);
        SyncToModel();
    }

    private void PasteSelected()
    {
        int count = owner.MainViewModel.MultiSelection.Count;
        if (count > 1)
            UndoManager.BeginGroupRecording();
        foreach (var cue in owner.MainViewModel.MultiSelection)
        {
            if (cue is SoundCueViewModel sound)
                sound.EQ.Paste();
        }
        if (count > 1)
            UndoManager.EndGroupRecording($"Pasted EQ settings to {count} cues");
    }

    private void Paste()
    {
        if (clipboard == null)
            return;

        using var _ = UndoManager.ScopedGroup($"Pasted EQ settings to {owner.FullQID}");
        var bound = BoundModel;
        Bind(clipboard);
        SyncFromModel();
        Bind(bound);
        SyncToModel();
    }

    private void ConfigureEQ()
    {
        if (eqSampleProvider == null)
            return;

        eqSampleProvider.eq = boundModel;
    }

    public override void SyncToModel()
    {
        base.SyncToModel();
        if (boundModel == null)
            return;

        boundModel.band1.q = 0.7f;
        boundModel.band2.q = 0.7f;
        boundModel.band3.q = 0.7f;
        boundModel.band4.q = 0.7f;

        boundModel.band1.shape = EQBandShape.LowShelf;
        boundModel.band2.shape = EQBandShape.Bell;
        boundModel.band3.shape = EQBandShape.Bell;
        boundModel.band4.shape = EQBandShape.HighShelf;

        ConfigureEQ();
    }

    public override void SyncFromModel()
    {
        base.SyncFromModel();

        ConfigureEQ();
    }
}
