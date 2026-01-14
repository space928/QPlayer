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
