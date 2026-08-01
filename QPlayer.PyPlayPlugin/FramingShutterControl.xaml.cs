using QPlayer.Views;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace QPlayer.PyPlayPlugin;

/// <summary>
/// Interaction logic for FramingShutterControl.xaml
/// </summary>
public partial class FramingShutterControl : UserControl
{
    bool isDragging = false;
    Point lastPos;

    public FramingShutterControl()
    {
        InitializeComponent();
    }

    private void Rectangle_MouseDown(object sender, MouseButtonEventArgs e)
    {
        isDragging = true;
        LibraryImports.ShowCursor(false);
        lastPos = e.GetPosition(ContainerGrid);
        (sender as FrameworkElement)?.CaptureMouse();
        //Debug.WriteLine($"down left={e.LeftButton} right={e.RightButton} drag={isDragging}");
    }

    private void Rectangle_MouseUp(object sender, MouseButtonEventArgs e)
    {
        //Debug.WriteLine($"up   left={e.LeftButton} right={e.RightButton} drag={isDragging}");
    }

    private void Rectangle_MouseMove(object sender, MouseEventArgs e)
    {
        //Debug.WriteLine($"move left={e.LeftButton} right={e.RightButton} drag={isDragging}");
        if (!isDragging || DataContext is not FramingShutterViewModel shutter)
            return;

        if (e.LeftButton == MouseButtonState.Released && e.RightButton == MouseButtonState.Released)
        {
            (sender as FrameworkElement)?.ReleaseMouseCapture();
            isDragging = false;
            ContainerGrid.Opacity = 0.7;
            // Sometimes the MouseUp call gets eaten, make sure to unhide the mouse here...
            int count;
            do
            {
                count = LibraryImports.ShowCursor(true);
            } while (count < 0);
            while (count > 0)
            {
                count = LibraryImports.ShowCursor(false);
            }
            return;
        }

        var pos = e.GetPosition(ContainerGrid);
        var off = pos - lastPos;
        lastPos = pos;

        var x = off.X / ContainerGrid.ActualWidth;
        var y = off.Y / ContainerGrid.ActualHeight;

        shutter.Rotation = Math.Clamp(shutter.Rotation - (float)(x * 150), -120, 120);
        if (e.RightButton == MouseButtonState.Pressed)
            shutter.Softness = Math.Clamp(shutter.Softness + (float)(y*2), 0, 1);
        else
            shutter.MaskStart = Math.Clamp(shutter.MaskStart - (float)(y*2), 0, 1);
    }

    private void Rectangle_MouseEnter(object sender, MouseEventArgs e)
    {
        ContainerGrid.Opacity = 1.0;
    }

    private void Rectangle_MouseLeave(object sender, MouseEventArgs e)
    {
        // Debug.WriteLine($"leave left={e.LeftButton} right={e.RightButton} drag={isDragging}");
        if (isDragging)
            return;

        ContainerGrid.Opacity = 0.7;
        //LibraryImports.ShowCursor(true);
    }
}
