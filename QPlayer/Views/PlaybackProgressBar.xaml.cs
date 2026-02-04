using QPlayer.SourceGenerator;
using QPlayer.ViewModels;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for PlaybackProgressBar.xaml
/// </summary>
public partial class PlaybackProgressBar : UserControl, INotifyPropertyChanged, INotifyPropertyChanging
{
    public string PlaybackTimeString => cueVM == null ? string.Empty : cueVM.State switch
    {
        CueState.Delay => $"WAIT {cueVM.Delay:mm\\:ss\\.ff}",
        CueState.Playing or CueState.PlayingLooped or CueState.Paused => $"{cueVM.PlaybackTime:mm\\:ss} / {cueVM.Duration:mm\\:ss}",
        _ => $"{cueVM.Duration:mm\\:ss\\.ff}",
    };

    public string PlaybackTimeStringShort => cueVM == null ? string.Empty : cueVM.State switch
    {
        CueState.Delay => $"WAIT",
        CueState.Playing or CueState.PlayingLooped or CueState.Paused => $"-{cueVM.PlaybackTime - cueVM.Duration:mm\\:ss}",
        _ => $"{cueVM.Duration:mm\\:ss}",
    };

    [Reactive("Progress")]
    private double Progress_Template
    {
        get
        {
            if (cueVM == null)
                return 0;
            var pt = cueVM.PlaybackTime;
            if (pt == TimeSpan.Zero)
                return 0;
            return pt.Ticks / (double)cueVM.Duration.Ticks * 100;
        }
    }

    private CueViewModel? cueVM;
    private bool showShortString = false;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;

    public PlaybackProgressBar()
    {
        InitializeComponent();

        BindVM();
        DataContextChanged += (o, e) => { UnBindVM(); BindVM(); };
        ProgressBarGrid.SizeChanged += ProgressBarGrid_SizeChanged;
    }

    private void UnBindVM()
    {
        if (cueVM != null)
            cueVM.PropertyChanged -= CueVM_PropertyChanged;
    }

    private void BindVM()
    {
        cueVM = (CueViewModel)DataContext;
        if (cueVM != null)
            cueVM.PropertyChanged += CueVM_PropertyChanged;
    }

    private void CueVM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(CueViewModel.State):
            case nameof(CueViewModel.Duration):
            case nameof(CueViewModel.PlaybackTime):
            case nameof(CueViewModel.Delay):
                OnPropertyChanged(nameof(Progress));
                OnPropertyChanged(nameof(PlaybackTimeString));
                OnPropertyChanged(nameof(PlaybackTimeStringShort));
                // Debug.WriteLine($"Changed: {e.PropertyName} --> pt = {cueVM?.PlaybackTime}");
                break;
        }
    }

    private void ProgressBarGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        bool shouldShowShort = e.NewSize.Width <= 80;
        if (shouldShowShort != showShortString)
        {
            if (shouldShowShort)
                PlaybackTimeLabel.SetBinding(ContentProperty, "PlaybackTimeStringShort");
            else
                PlaybackTimeLabel.SetBinding(ContentProperty, "PlaybackTimeString");
        }
        showShortString = shouldShowShort;
    }
}
