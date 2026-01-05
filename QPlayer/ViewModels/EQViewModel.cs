using QPlayer.Audio;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

[Model(typeof(EQSettings))]
public class EQViewModel : BindableViewModel<EQSettings>
{
    [Reactive] public bool Enabled { get; set; }
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band1)}.{nameof(EQBand.freq)}")] public float LowFreq { get; set; } = 200;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band1)}.{nameof(EQBand.gain)}")] public float LowGain { get; set; }
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band2)}.{nameof(EQBand.freq)}")] public float LowMidFreq { get; set; } = 500;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band2)}.{nameof(EQBand.gain)}")] public float LowMidGain { get; set; }
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band3)}.{nameof(EQBand.freq)}")] public float HighMidFreq { get; set; } = 2500;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band3)}.{nameof(EQBand.gain)}")] public float HighMidGain { get; set; }
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band4)}.{nameof(EQBand.freq)}")] public float HighFreq { get; set; } = 8000;
    [Reactive, ModelBindsTo($"{nameof(EQSettings.band4)}.{nameof(EQBand.gain)}")] public float HighGain { get; set; }

    [ModelSkip]
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

    private EQSampleProvider? eqSampleProvider;
    private ISamplePositionProvider? inputSampleProvider;

    public EQViewModel()
    {
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
