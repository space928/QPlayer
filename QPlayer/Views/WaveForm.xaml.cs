using CommunityToolkit.Mvvm.Input;
using QPlayer.ViewModels;
using ReactiveUI.Fody.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;

namespace QPlayer.Views
{
    /// <summary>
    /// Interaction logic for WaveForm.xaml
    /// </summary>
    public partial class WaveForm : UserControl, INotifyPropertyChanged
    {
        private const float zoomSpeed = 0.67f;
        private const float panSpeed = 0.5f;

        private bool isWaveformCapturingMouse = false;
        private POINT mouseStartPos;
        private Window? window;
        private WaveFormWindow? waveFormWindow;
        private bool enabled = true;

        [Reactive] public bool Enabled => enabled;
        [Reactive] public Visibility WaveFormVisible => enabled ? Visibility.Visible : Visibility.Hidden;
        [Reactive] public Visibility InvWaveFormVisible => enabled ? Visibility.Hidden : Visibility.Visible;
        [Reactive] public RelayCommand PopupCommand { get; private set; }
        [Reactive, ReactiveDependency(nameof(NavBarHeight))] public double TimeStampFontSize => NavBarHeight / 2;

        public double NavBarHeight
        {
            get { return (double)GetValue(NavBarHeightProperty); }
            set { SetValue(NavBarHeightProperty, value); }
        }

        public static readonly DependencyProperty NavBarHeightProperty =
            DependencyProperty.Register("NavBarHeight", typeof(double), typeof(WaveForm), new PropertyMetadata(20d));

        public WaveFormRenderer WaveFormRenderer
        {
            get { return (WaveFormRenderer)GetValue(WaveFormRendererProperty); }
            set { SetValue(WaveFormRendererProperty, value); }
        }

        // Using a DependencyProperty as the backing store for WaveFormRenderer.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty WaveFormRendererProperty =
            DependencyProperty.Register("WaveFormRenderer", typeof(WaveFormRenderer), typeof(WaveForm), new PropertyMetadata(null));

        public WaveForm()
        {
            InitializeComponent();

            PopupCommand = new(OpenPopup);
        }

        private void WaveForm_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!enabled)
                return;
            if (WaveFormRenderer == null)
                return;

            WaveFormRenderer.Width = ((Rectangle)sender).ActualWidth;
            WaveFormRenderer.Height = ((Rectangle)sender).ActualHeight;
        }

        private void WaveFormZoom_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!enabled)
                return;

            isWaveformCapturingMouse = true;
            ShowCursor(false);
            GetCursorPos(out mouseStartPos);
            mouseStartPos.y -= 40;

            // There are still plenty of cases where these don't get propagated up to the renderer correctly, so we'll set it here too.
            WaveFormRenderer.Width = Graph.ActualWidth;
            WaveFormRenderer.Height = Graph.ActualHeight;
        }

        private void WaveFormZoom_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if(!enabled) 
                return;
            if(!isWaveformCapturingMouse) 
                return;

            isWaveformCapturingMouse = false;
            SetCursorPos(mouseStartPos.x, mouseStartPos.y + 40);
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
            if (!enabled)
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
            var start = wf.ViewStart;
            var end = wf.ViewEnd;
            var viewDelta = end - start;
            if (viewDelta.TotalSeconds < 0.05)
            {
                // Prevent the user from zooming in too far...
                viewDelta = TimeSpan.FromSeconds(0.05);
                delta.y = Math.Max(delta.y, 0); 
            }
            var width = Math.Max(RenderSize.Width, 10);
            var zoom = viewDelta * delta.y * zoomSpeed / width;
            var pan = viewDelta * delta.x * panSpeed / width;
            wf.ViewStart = start - pan - zoom;
            wf.ViewEnd = end - pan + zoom;

            NavBarScale.ScaleX -= delta.y * zoomSpeed / width;
            NavBarTranslate.X += delta.x * panSpeed / width;
        }

        private void NavBar_Loaded(object sender, RoutedEventArgs e)
        {
            window = Window.GetWindow(this);
            if (window == null)
                return;

            window.MouseMove += WaveFormZoom_MouseMove;
            window.MouseUp += WaveFormZoom_MouseUp;

            WaveFormRenderer.Width = Graph.ActualWidth;
            WaveFormRenderer.Height = Graph.ActualHeight;
        }

        private void NavBar_Unloaded(object sender, RoutedEventArgs e)
        {
            if (window == null)
                return;

            window.MouseUp -= WaveFormZoom_MouseUp;
            window.MouseMove -= WaveFormZoom_MouseMove;

            waveFormWindow?.Close();
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
                waveFormWindow.DataContext = WaveFormRenderer;
                waveFormWindow.Activate();
                return;
            }

            waveFormWindow = new();
            waveFormWindow.DataContext = WaveFormRenderer;
            waveFormWindow.Closed += (s, e) =>
            {
                enabled = true;
                waveFormWindow = null;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveFormVisible)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InvWaveFormVisible)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
                if (WaveFormRenderer != null)
                {
                    WaveFormRenderer.Width = Graph.ActualWidth;
                    WaveFormRenderer.Height = Graph.ActualHeight;
                }
            };
            enabled = false;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(WaveFormVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(InvWaveFormVisible)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            waveFormWindow.Show();
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT
        {
            public int x;
            public int y;
        }

        [DllImport("user32.dll")]
        private static extern long SetCursorPos(int x, int y);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool GetCursorPos(out POINT lpPoint);

        /// <summary>
        /// Displays or hides the cursor.
        /// </summary>
        /// <param name="bShow">
        /// If bShow is TRUE, the display count is incremented by one. If bShow is FALSE, the display count is decremented by one.
        /// </param>
        /// <returns>The return value specifies the new display counter.</returns>
        [DllImport("user32.dll")]
        public static extern int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);
    }
}
