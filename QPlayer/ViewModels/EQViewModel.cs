using CommunityToolkit.Mvvm.ComponentModel;
using NAudio.Wave;
using QPlayer.Audio;
using QPlayer.Models;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public class EQViewModel : ObservableObject, IConvertibleModel<EQSettings, EQViewModel>
{
    [Reactive] public bool Enabled { get; set; }
    [Reactive] public float LowFreq { get; set; } = 200;
    [Reactive] public float LowGain { get; set; }
    [Reactive] public float LowMidFreq { get; set; } = 500;
    [Reactive] public float LowMidGain { get; set; }
    [Reactive] public float HighMidFreq { get; set; } = 2500;
    [Reactive] public float HighMidGain { get; set; }
    [Reactive] public float HighFreq { get; set; } = 8000;
    [Reactive] public float HighGain { get; set; }

    private EQSettings? model;

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
        PropertyChanged += EQViewModel_PropertyChanged;
    }

    private void EQViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        ToModel(e.PropertyName ?? string.Empty);
    }

    private void ConfigureEQ()
    {
        if (eqSampleProvider == null)
            return;

        eqSampleProvider.eq = model;
    }

    public void FromModel(EQSettings model)
    {
        Bind(model);

        Enabled = model.enabled;

        LowFreq = model.band1.freq;
        LowGain = model.band1.gain;

        LowMidFreq = model.band2.freq;
        LowMidGain = model.band2.gain;

        HighMidFreq = model.band3.freq;
        HighMidGain = model.band3.gain;

        HighFreq = model.band4.freq;
        HighGain = model.band4.gain;
    }

    public static EQViewModel FromModel(EQSettings model, MainViewModel mainViewModel)
    {
        var res = new EQViewModel();
        res.FromModel(model);

        return res;
    }

    public void ToModel(EQSettings model)
    {
        Bind(model);

        model.enabled = Enabled;
        model.band1 = new(LowFreq, LowGain, .7f, EQBandShape.LowShelf);
        model.band2 = new(LowMidFreq, LowMidGain, .7f, EQBandShape.Bell);
        model.band3 = new(HighMidFreq, HighMidGain, .7f, EQBandShape.Bell);
        model.band4 = new(HighFreq, HighGain, .7f, EQBandShape.HighShelf);
    }

    public void ToModel(string propertyName)
    {
        if (model == null)
            return;

        switch (propertyName)
        {
            case nameof(Enabled): model.enabled = Enabled; break;
            case nameof(LowFreq): model.band1.freq = LowFreq; break;
            case nameof(LowGain): model.band1.gain = LowGain; break;
            case nameof(LowMidFreq): model.band2.freq = LowMidFreq; break;
            case nameof(LowMidGain): model.band2.gain = LowMidGain; break;
            case nameof(HighMidFreq): model.band3.freq = HighMidFreq; break;
            case nameof(HighMidGain): model.band3.gain = HighMidGain; break;
            case nameof(HighFreq): model.band4.freq = HighFreq; break;
            case nameof(HighGain): model.band4.gain = HighGain; break;
            case nameof(InputSampleProvider): break;
            default: MainViewModel.Log($"Attempted to set undefined EQ parameter '{propertyName}'!", MainViewModel.LogLevel.Warning); break;
        }
    }

    public void Bind(EQSettings model)
    {
        this.model = model;
        ConfigureEQ();
    }
}
