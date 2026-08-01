using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using QPlayer.Audio;
using QPlayer.SourceGenerator;
using QPlayer.Utilities;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.PyPlayPlugin;

[Model(typeof(PyVideoCue))]
//[GenerateView]
[View(typeof(PyVideoCueView))]
[DisplayName("Video Cue")]
[Icon("IconPyVideoCue", typeof(Icons))]
public partial class PyVideoCueViewModel : CueViewModel, IMediaCue
{
    [Reactive] private string path = string.Empty;
    [Reactive] private string shader = string.Empty;
    // Timing
    [Reactive] public TimeSpan startTime;
    [Reactive, ModelBindsTo(nameof(PyVideoCue.duration))] public TimeSpan playbackDuration;
    [Reactive] public bool stompsOthers;
    [Reactive] public float fadeIn = 1;
    [Reactive] public float fadeOut = 1;
    [Reactive] public FadeType fadeType = FadeType.SCurve;
    // Placement
    [Reactive] public float scale = 1;
    [Reactive] public float rotation = 0;
    [Reactive] public Vector2 offset = Vector2.Zero;
    // Blending
    [Reactive] public float dimmer = 1;
    [Reactive] public float volume = 1;
    [Reactive] private int zIndex;
    [Reactive] private string? alphaPath;
    [Reactive] public AlphaMode alphaMode = AlphaMode.Video;
    // Image
    [Reactive] public float brightness = 1;
    [Reactive] public float contrast = 1;
    [Reactive] public float gamma = 1;
    // Control
    [Reactive] public UndoableObservableCollection<ShaderParameterViewModel, ShaderParameter> shaderParameters = [];
    /// <summary>
    /// DMX address as a single integer (1..508). 0 means unset.
    /// </summary>
    [Reactive] public int dmxAddress = 0;

    [Reactive, Readonly, ModelSkip] private RelayCommand addShaderParameterCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand<ShaderParameterViewModel> deleteShaderParameterCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand openMediaFileCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand openShaderFileCommand;
    [Reactive, Readonly, ModelSkip] private RelayCommand openAlphaFileCommand;
    [Reactive, ModelSkip] private readonly ObservableArray<AlphaMode> alphaModeVals;

    public override string NamePreview => string.IsNullOrEmpty(Name) ? $"Video {fileNameShort}" : Name;

    private string fileNameShort = "NO MEDIA";

    public PyVideoCueViewModel(MainViewModel mainViewModel) : base(mainViewModel)
    {
        AddShaderParameterCommand = new(() => shaderParameters.Add(new()));
        DeleteShaderParameterCommand = new(item =>
        {
            if (item == null)
                return;
            shaderParameters.Remove(item);
        });
        OpenAlphaFileCommand = new(OpenAlphaFileExecute);
        OpenMediaFileCommand = new(OpenMediaFileExecute);
        OpenShaderFileCommand = new(OpenShaderFileExecute);
        alphaModeVals = new(Enum.GetValues<AlphaMode>());

        PropertyChanged += (o, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(Path):
                    try
                    {
                        fileNameShort = System.IO.Path.GetFileNameWithoutExtension(path);
                    }
                    finally
                    {
                        fileNameShort ??= "NO MEDIA";
                    }
                    OnPropertyChanged(nameof(NamePreview));
                    break;
            }
        };
    }

    public Task<bool> LoadMediaFiles()
    {
        return Task.FromResult(true);
        //throw new NotImplementedException();
    }

    public void UnloadMediaFiles()
    {
        //throw new NotImplementedException();
    }

    public void OpenMediaFileExecute()
    {
        OpenFileDialog openFileDialog = new()
        {
            Multiselect = false,
            Title = "Open Media File",
            CheckFileExists = true,
            FileName = Path,
            Filter = "Supported Media (*.mp4;*.mkv;*.wmv;*.webm;*.png;*.jpg;*.jpeg;*.bmp;*.exr;*.hdr;*.webp)|*.mp4;*.mkv;*.wmv;*.webm;*.png;*.jpg;*.jpeg;*.bmp;*.exr;*.hdr;*.webp|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() ?? false)
        {
            Path = openFileDialog.FileName;
        }
    }

    public void OpenShaderFileExecute()
    {
        OpenFileDialog openFileDialog = new()
        {
            Multiselect = false,
            Title = "Open Shader File",
            CheckFileExists = true,
            FileName = Shader,
            Filter = "Supported Shaders (*.glsl;*.frag;*.vert)|*.glsl;*.frag;*.vert|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() ?? false)
        {
            Shader = openFileDialog.FileName;
        }
    }

    public void OpenAlphaFileExecute()
    {
        OpenFileDialog openFileDialog = new()
        {
            Multiselect = false,
            Title = "Open Alpha File",
            CheckFileExists = true,
            FileName = AlphaPath,
            Filter = "Supported Media (*.mp4;*.mkv;*.wmv;*.webm;*.png;*.jpg;*.jpeg;*.bmp;*.exr;*.hdr;*.webp)|*.mp4;*.mkv;*.wmv;*.webm;*.png;*.jpg;*.jpeg;*.bmp;*.exr;*.hdr;*.webp|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() ?? false)
        {
            AlphaPath = openFileDialog.FileName;
        }
    }
}

public partial class ShaderParameterViewModel : BindableViewModel<ShaderParameter>
{
    [Reactive] private string name = string.Empty;
    [Reactive] private float value;
}
