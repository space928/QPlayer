using CommunityToolkit.Mvvm.Input;
using QPlayer.Audio;
using QPlayer.Models;
using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.PyPlayPlugin;

[Model(typeof(ShaderParamsCue))]
//[GenerateView]
[View(typeof(ShaderParamsCueView))]
[DisplayName("Shader Parameters Cue")]
[Icon("IconPyShaderCue", typeof(Icons))]
public partial class ShaderParamsCueViewModel : CueViewModel
{
    [Reactive, ModelCustomBinding(nameof(VM2M_TargetQID), nameof(M2VM_TargetQID))] private string targetQid = string.Empty;
    [Reactive] private UndoableObservableCollection<ShaderParameterViewModel, ShaderParameter> shaderParameters = [];
    [Reactive] private float fadeTime = 0;
    [Reactive] private FadeType fadeType = FadeType.SCurve;
    [Reactive] private bool postProcessing = false;

    public override string NamePreview => string.IsNullOrEmpty(Name) ? (postProcessing ? "Change Post Processing Parameters" : $"Change Shader Parameters of Q{targetQid}") : Name;

    [Reactive, Readonly, ModelSkip] private RelayCommand addShaderParameterCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand<ShaderParameterViewModel> deleteShaderParameterCommand;

    public ShaderParamsCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        AddShaderParameterCommand = new(() => shaderParameters.Add(new()));
        DeleteShaderParameterCommand = new(item =>
        {
            if (item == null)
                return;
            shaderParameters.Remove(item);
        });

        PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(TargetQid):
                case nameof(PostProcessing):
                    OnPropertyChanged(nameof(NamePreview));
                    break;
            }
        };
    }

    private static void M2VM_TargetQID(ShaderParamsCueViewModel vm, ShaderParamsCue m) 
    {
        vm.PostProcessing = m.targetQid == "post";
        vm.TargetQid = vm.postProcessing ? string.Empty : m.targetQid;
    }
    private static void VM2M_TargetQID(ShaderParamsCueViewModel vm, ShaderParamsCue m)
    {
        m.targetQid = vm.postProcessing ? "post" : vm.targetQid;
    }
}
