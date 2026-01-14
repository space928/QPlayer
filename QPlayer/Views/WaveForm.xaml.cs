using CommunityToolkit.Mvvm.Input;
using QPlayer.ViewModels;
using QPlayer.SourceGenerator;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using static QPlayer.Views.LibraryImports;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for WaveForm.xaml
/// </summary>
public partial class WaveForm : UserControl, INotifyPropertyChanged
{
    private const float zoomSpeed = 0.67f;
    private const float panSpeed = 0.5f;

    private PropertyChangedEventHandler? onWaveFormChagedHandler = null;
    private bool isWaveformCapturingMouse = false;
    private bool isTimeinCapturing = false;
    private bool isTimeoutCapturing = false;
    private POINT mouseStartPos;
    private Window? window;
    private static WaveFormWindow? waveFormWindow;
    private double timeOutMarkerPos = 0;
    private double timeInMarkerPos = 0;
    private double timeHandleMouseOffset = 0;
    private TimeSpan timeOutStart = TimeSpan.Zero;

    [Reactive("Enabled")] private bool Enabled_Template => waveFormWindow == null || waveFormWindow == window || waveFormWindow.DataContext != SoundCue;
    public Visibility WaveFormVisible => Enabled ? Visibility.Visible : Visibility.Hidden;
    public Visibility InvWaveFormVisible => Enabled ? Visibility.Hidden : Visibility.Visible;
    public Visibility WaveFormLoading => (WaveFormRenderer?.PeakFile != null && Enabled) ? Visibility.Hidden : Visibility.Visible;
    public RelayCommand PopupCommand { get; private set; }
    public double TimeStampFontSize => NavBarHeight / 2;
    public Thickness PlaybackMarkerPos
    {
        get
        {
            if (SoundCue == null || WaveFormRenderer == null || WaveFormRenderer.ViewSpan == TimeSpan.Zero)
                return new(-10, 0, 0, 0);
            return new((SoundCue.SamplePlaybackTime - WaveFormRenderer.ViewStart/* + SoundCue.StartTime*/) / WaveFormRenderer.ViewSpan * Graph.ActualWidth - PlaybackMarker.Width, 0, 0, 0);
        }
    }
    public Thickness TimeInMarkerPos => new(timeInMarkerPos,0,0,0);
    public Thickness TimeOutMarkerPos => new(timeOutMarkerPos,0,0,0);

    #region Dependency Properties

    public SoundCueViewModel SoundCue
    {
        get { return (SoundCueViewModel)GetValue(SoundCueProperty); }
        set { SetValue(SoundCueProperty, value); }
    }

    public static readonly DependencyProperty SoundCueProperty =
        DependencyProperty.Register("SoundCue", typeof(SoundCueViewModel), typeof(WaveForm), new PropertyMetadata(SoundCueUpdated));

    public double NavBarHeight
    {
        get { return (double)GetValue(NavBarHeightProperty); }
        set { SetValue(NavBarHeightProperty, value); OnPropertyChanged(nameof(TimeStampFontSize)); }
    }

    public static readonly DependencyProperty NavBarHeightProperty =
        DependencyProperty.Register("NavBarHeight", typeof(double), typeof(WaveForm), new PropertyMetadata(20d));

    public WaveFormRenderer WaveFormRenderer
    {
        get { return (WaveFormRenderer)GetValue(WaveFormRendererProperty); }
        set { SetValue(WaveFormRendererProperty, value); }
    }

    public static readonly DependencyProperty WaveFormRendererProperty =
        DependencyProperty.Register("WaveFormRenderer", typeof(WaveFormRenderer), typeof(WaveForm), new PropertyMetadata(WaveFormRendererUpdated));

    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangedEventHandler? PropertyChanging;

    #endregion

    public WaveForm()
    {
        InitializeComponent();

        PopupCommand = new(OpenPopup);
        UpdateTimePositions();
    }

    private static void SoundCueUpdated(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        // The property change notfification chain to make this all work is jank-saurus-rex
        // Basically, a SoundCueViewModel owns a WaveFormRenderer (which is also a vm);
        // in the view we create a WaveForm usercontrol, the WaveForm usercontrol uses it's code-behind (this) as it's vm
        // as such it defines 2 important dep properties: SoundCue and WaveFormRenderer (the important VMs which store
        // everything we care about). Changes in in any of the 3 VMs (WaveForm, WaveFormRenderer, and SoundCue) are important
        // to the view. Traditional data-bindings aren't flexible enough for all the data transformation we're doing
        // (ValueConverters are inefficient and a mess, and very error prone) so we have to link up all these dependencies
        // manually through property change notifications. And of course MS didn't really intend us to do this, so it's a pain.
        // Maybe there's some neat solution to this I'm not seeing, but for now we have a tangled mess of dependencies.
        PropertyChangedEventHandler f = (o, a) => SoundCue_PropertyChanged((WaveForm)sender, a);
        if (e.OldValue != null)
            ((SoundCueViewModel)e.OldValue).PropertyChanged -= f;
        if(e.NewValue != null)
            ((SoundCueViewModel)e.NewValue).PropertyChanged += f;
    }

    private static void WaveFormRendererUpdated(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        // Use a lambda to capture the dependency object (which is just this) sending the message, otherwise
        // we won't know who to route notifications to.
        // Did I mention that this is janky........
        WaveForm waveForm = (WaveForm)sender;
        if (e.OldValue != null)
            ((WaveFormRenderer)e.OldValue).PropertyChanged -= waveForm.onWaveFormChagedHandler;
        waveForm.onWaveFormChagedHandler = (o, a) => WaveFormRenderer_PropertyChanged(waveForm, a);
        if (e.NewValue != null)
            ((WaveFormRenderer)e.NewValue).PropertyChanged += waveForm.onWaveFormChagedHandler;

        waveForm.onWaveFormChagedHandler?.Invoke(waveForm, new PropertyChangedEventArgs(WaveFormRenderer.OnVMUpdate));
    }

    private static void SoundCue_PropertyChanged(WaveForm sender, PropertyChangedEventArgs e)
    {
        if (sender is not WaveForm vm)
            return;
        switch (e.PropertyName)
        {
            case (nameof(vm.SoundCue.PlaybackTime)):
                vm.OnPropertyChanged(nameof(PlaybackMarkerPos));
                break;
            case (nameof(vm.SoundCue.StartTime)):
            case (nameof(vm.SoundCue.PlaybackDuration)):
            case (nameof(vm.SoundCue.Duration)):
                vm.UpdateTimePositions();
                break;
        }
    }

    private static void WaveFormRenderer_PropertyChanged(WaveForm sender, PropertyChangedEventArgs e)
    {
        if (sender is not WaveForm vm)
            return;
        switch (e.PropertyName)
        {
            case nameof(vm.WaveFormRenderer.ViewStart):
            case nameof(vm.WaveFormRenderer.ViewEnd):
            case nameof(vm.WaveFormRenderer.WaveFormDrawing):
                vm.OnPropertyChanged(nameof(PlaybackMarkerPos));
                vm.UpdateTimePositions();
                break;
            case nameof(vm.WaveFormRenderer.PeakFile):
                vm.WaveForm_SizeChanged(vm, null);
                vm.OnPropertyChanged(nameof(WaveFormLoading));
                break;
            case WaveFormRenderer.OnVMUpdate:
                vm.WaveForm_SizeChanged(sender, null);
                vm.OnPropertyChanged(nameof(WaveFormLoading));
                //vm.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, () => vm.WaveForm_SizeChanged(sender, null));
                break;
        }
    }

    private void WaveForm_SizeChanged(object sender, SizeChangedEventArgs? e)
    {
        if (!Enabled)
            return;
        if (WaveFormRenderer == null)
            return;

        WaveFormRenderer.Size = (Graph.ActualWidth, Graph.ActualHeight);
        OnPropertyChanged(nameof(PlaybackMarkerPos));
        UpdateTimePositions();
    }

    private void WaveFormZoom_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!Enabled)
            return;

        isWaveformCapturingMouse = true;
        e.MouseDevice.Capture(NavBar);
        ShowCursor(false);
        GetCursorPos(out mouseStartPos);
        mouseStartPos.y -= 40;

        // There are still plenty of cases where these don't get propagated up to the renderer correctly, so we'll set it here too.
        WaveFormRenderer.Size = (Graph.ActualWidth, Graph.ActualHeight);
    }

    private void WaveFormZoom_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if(!Enabled) 
            return;
        if(!isWaveformCapturingMouse) 
            return;

        isWaveformCapturingMouse = false;
        SetCursorPos(mouseStartPos.x, mouseStartPos.y + 40);
        e.MouseDevice.Capture(null);
        ShowCursor(true);
        NavBarScale.ScaleX = 1;
        NavBarTranslate.X = 0;
    }

    private void WaveFormZoom_MouseLeave(object sender, MouseEventArgs e)
    {
        if (isWaveformCapturingMouse)
        {
            //SetCursorPos(mouseStartPos.x, mouseStartPos.y);
            return;
        }

        // Sometimes the MouseUp call gets eaten, make sure to unhide the mouse here...
        int count;
        do 
        { 
            count = ShowCursor(true);
        } while (count < 0);
        while (count > 0)
        {
            count = ShowCursor(false);
        }
    }

    private void WaveFormZoom_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isWaveformCapturingMouse)
            return;
        if (!Enabled)
            return;

        GetCursorPos(out POINT nPos);
        SetCursorPos(mouseStartPos.x, mouseStartPos.y);
        POINT delta = new()
        {
            x = nPos.x - mouseStartPos.x,
            y = nPos.y - mouseStartPos.y
        };

        if (WaveFormRenderer == null)
            return;

        var wf = WaveFormRenderer;
        var (start, end) = wf.ViewBounds;
        var viewDelta = end - start;
        if (viewDelta.TotalSeconds < 0.05)
        {
            // Prevent the user from zooming in too far...
            viewDelta = TimeSpan.FromSeconds(0.05);
            delta.y = Math.Max(delta.y, 0); 
        }
        var width = Math.Max(RenderSize.Width, 10);
        var zoom = delta.y * zoomSpeed / width;
        var pan = delta.x * panSpeed / width;
        wf.ViewBounds = (start - viewDelta * (pan + zoom), end - viewDelta * (pan - zoom));

        NavBarScale.ScaleX -= delta.y * zoomSpeed / width;
        NavBarTranslate.X += delta.x * panSpeed / width;
    }

    private void Graph_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (Graph.ActualWidth <= 0)
            return;

        var pos = e.GetPosition(this);
        var x = pos.X / Graph.ActualWidth;
        var t = x * WaveFormRenderer.ViewSpan + WaveFormRenderer.ViewStart;

        var scue = WaveFormRenderer.SoundCueViewModel;
        //if (scue.State == CueState.Playing)
        //scue.Preload(t);
        scue.Go();
        scue.PlaybackTime = t - scue.StartTime;
    }

    private void NavBar_Loaded(object sender, RoutedEventArgs e)
    {
        window = Window.GetWindow(this);
        if (window == null)
            return;

        WaveForm_SizeChanged(sender, null);
    }

    private void NavBar_Unloaded(object sender, RoutedEventArgs e)
    {
        if (window == null)
            return;
    }

    private void OpenPopup()
    {
        if(window == null)
            return;
        if (window is WaveFormWindow)
            return;
        if (WaveFormRenderer == null)
            return;
        if (waveFormWindow != null)
        {
            waveFormWindow.DataContext = SoundCue;
            waveFormWindow.WindowState = WindowState.Normal;
            waveFormWindow.Activate();
            return;
        }

        waveFormWindow = new(SoundCue.MainViewModel, window.InputBindings);
        waveFormWindow.Owner = window;
        waveFormWindow.DataContext = SoundCue;
        waveFormWindow.Closed += (s, e) =>
        {
            waveFormWindow = null;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveFormVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InvWaveFormVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            if (WaveFormRenderer != null)
                WaveFormRenderer.Size = (Graph.ActualWidth, Graph.ActualHeight);
        };
        window.Closed += (s, e) =>
        {
            waveFormWindow?.Close();
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveFormVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InvWaveFormVisible)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
        waveFormWindow.Show();
    }

    #region Time In/Out Markers
    private void TimeInMarker_MouseDown(object sender, MouseButtonEventArgs e)
    {
        isTimeinCapturing = true;
        timeHandleMouseOffset = e.GetPosition(TimeInMarker).X;
        if(SoundCue != null)
            timeOutStart = SoundCue.SampleDuration + SoundCue.StartTime;
        e.MouseDevice.Capture(TimeInMarker, CaptureMode.Element);
    }

    private void TimeInMarker_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!isTimeinCapturing)
            return;

        isTimeinCapturing = false;
        e.MouseDevice.Capture(null);
    }

    private void TimeInMarker_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isTimeinCapturing)
            return;
        if(SoundCue == null || WaveFormRenderer == null) 
            return;

        var relGraph = e.GetPosition(Graph);
        relGraph.X -= timeHandleMouseOffset;
        var width = Graph.ActualWidth;
        var time = (relGraph.X / width) * WaveFormRenderer.ViewSpan + WaveFormRenderer.ViewStart;
        if (time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        SoundCue.StartTime = time;
        var duration = timeOutStart - time;
        if(duration < TimeSpan.Zero)
            duration = TimeSpan.Zero;
        SoundCue.PlaybackDuration = duration;
        if(SoundCue.State == CueState.Ready)
            SoundCue.PlaybackTime = TimeSpan.Zero;
    }

    private void TimeOutMarker_MouseDown(object sender, MouseButtonEventArgs e)
    {
        isTimeoutCapturing = true;
        timeHandleMouseOffset = e.GetPosition(TimeOutMarker).X;
        e.MouseDevice.Capture(TimeOutMarker, CaptureMode.Element);
    }

    private void TimeOutMarker_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!isTimeoutCapturing)
            return;

        isTimeoutCapturing = false;
        e.MouseDevice.Capture(null);
    }

    private void TimeOutMarker_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isTimeoutCapturing)
            return;
        if (SoundCue == null || WaveFormRenderer == null)
            return;

        var relGraph = e.GetPosition(Graph);
        relGraph.X += TimeOutMarker.ActualWidth - timeHandleMouseOffset;
        var width = Graph.ActualWidth;
        var time = (relGraph.X / width) * WaveFormRenderer.ViewSpan + WaveFormRenderer.ViewStart - SoundCue.StartTime;
        if(time < TimeSpan.Zero)
            time = TimeSpan.Zero;
        SoundCue.PlaybackDuration = time;
    }

    private void UpdateTimePositions()
    {
        if (SoundCue == null || WaveFormRenderer == null || WaveFormRenderer.ViewSpan == TimeSpan.Zero)
        {
            timeInMarkerPos = 0;
            timeOutMarkerPos = 0;
            OnPropertyChanged(nameof(TimeInMarkerPos));
            OnPropertyChanged(nameof(TimeOutMarkerPos));
            return;
        }

        var start = WaveFormRenderer.ViewStart;
        var span = WaveFormRenderer.ViewSpan;
        double left = (SoundCue.StartTime - start) / span;
        double right = (SoundCue.SampleDuration + SoundCue.StartTime - start) / span;
        var markerWidth = TimeInMarker.ActualWidth;
        var graphWidth = Graph.ActualWidth;
        timeInMarkerPos = left * graphWidth;
        timeOutMarkerPos = right * graphWidth - markerWidth;

        OnPropertyChanged(nameof(TimeInMarkerPos));
        OnPropertyChanged(nameof(TimeOutMarkerPos));
    }
    #endregion
}
