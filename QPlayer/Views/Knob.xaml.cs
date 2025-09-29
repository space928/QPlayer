using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using static QPlayer.Views.LibraryImports;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for Knob.xaml
/// </summary>
public partial class Knob : UserControl, INotifyPropertyChanged
{
    private bool isCapturingMouse;
    private POINT mouseStartPos;
    private double delta;

    public string ValueText => string.Format(ValueFormat, Value);

    public Knob()
    {
        InitializeComponent();
    }

    #region Dependency Properties

    public double MinValue
    {
        get { return (double)GetValue(MinValueProperty); }
        set { SetValue(MinValueProperty, value); }
    }

    // Using a DependencyProperty as the backing store for MinValue.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty MinValueProperty =
        DependencyProperty.Register("MinValue", typeof(double), typeof(Knob), new PropertyMetadata(0d));

    public double MaxValue
    {
        get { return (double)GetValue(MaxValueProperty); }
        set { SetValue(MaxValueProperty, value); }
    }

    public double Rounding
    {
        get { return (double)GetValue(RoundingProperty); }
        set { SetValue(RoundingProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Rounding.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty RoundingProperty =
        DependencyProperty.Register("Rounding", typeof(double), typeof(Knob), new PropertyMetadata(0d));

    // Using a DependencyProperty as the backing store for MaxValue.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register("MaxValue", typeof(double), typeof(Knob), new PropertyMetadata(1d));

    public double Value
    {
        get { return (double)GetValue(ValueProperty); }
        set { SetValue(ValueProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Value.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register("Value", typeof(double), typeof(Knob), new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValueChanged));

    public double Power
    {
        get { return (double)GetValue(PowerProperty); }
        set { SetValue(PowerProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Power.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty PowerProperty =
        DependencyProperty.Register("Power", typeof(double), typeof(Knob), new PropertyMetadata(1d));

    public string Label
    {
        get { return (string)GetValue(LabelProperty); }
        set { SetValue(LabelProperty, value); }
    }

    // Using a DependencyProperty as the backing store for Label.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register("Label", typeof(string), typeof(Knob), new PropertyMetadata(string.Empty));

    public string ValueFormat
    {
        get { return (string)GetValue(ValueFormatProperty); }
        set { SetValue(ValueFormatProperty, value); }
    }

    // Using a DependencyProperty as the backing store for ValueFormat.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty ValueFormatProperty =
        DependencyProperty.Register("ValueFormat", typeof(string), typeof(Knob), new PropertyMetadata("{0:G3}"));

    #endregion

    private void KnobEllipse_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;

        isCapturingMouse = true;
        e.MouseDevice.Capture(KnobEllipse);
        ShowCursor(false);
        //GetCursorPos(out mouseStartPos);
        var spinnerCentre = KnobEllipse.PointToScreen(new Point(KnobEllipse.ActualWidth / 2, KnobEllipse.ActualHeight / 2));
        mouseStartPos = new((int)spinnerCentre.X, (int)spinnerCentre.Y);
        GetCursorPos(out POINT nPos);
        SetCursorPos(mouseStartPos.x, mouseStartPos.y);

        ValueLabel.Visibility = Visibility.Visible;

        if (e.ClickCount >= 2 || (e.ClickCount == 1 && nPos.x == mouseStartPos.x && nPos.y == mouseStartPos.y))
        {
            ValueTextField.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, ValueTextField.TextBox.Focus);
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, ValueTextField.TextBox.SelectAll);
        }
    }

    private void KnobEllipse_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;
        if (!isCapturingMouse)
            return;

        isCapturingMouse = false;
        SetCursorPos(mouseStartPos.x, mouseStartPos.y);
        e.MouseDevice.Capture(null);
        ShowCursor(true);

        ValueLabel.Visibility = Visibility.Hidden;
    }

    private void KnobEllipse_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isCapturingMouse)
            return;
        if (!IsEnabled)
            return;

        GetCursorPos(out POINT nPos);
        SetCursorPos(mouseStartPos.x, mouseStartPos.y);
        POINT deltaPos = new()
        {
            x = nPos.x - mouseStartPos.x,
            y = nPos.y - mouseStartPos.y
        };

        double spinRate = Math.Abs(MaxValue - MinValue) * 0.005f;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            spinRate *= 0.2;
        else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            spinRate *= 5;

        delta -= deltaPos.y * spinRate;

        var min = MinValue;
        var max = MaxValue;
        double val = Value;
        val = UnApplyPower(Math.Clamp(val, min, max));
        val = Math.Round(val + delta, (int)Math.Max(0, Math.Ceiling(-Math.Log10(spinRate))));
        delta = 0;
        val = Math.Clamp(val, min, max);
        if (val == -0 && min == 0) // Weird but whatev
            val = 0;
        UpdateKnobMarker(min, max, val);

        val = ApplyPower(val);
        if (Rounding != 0)
            val = Math.Round(val / Rounding) * Rounding;
        Value = val;

        //var binding = BindingOperations.GetBindingExpression(this, TextProperty);
        //binding?.UpdateSource();
        OnPropertyChanged(nameof(ValueText));
    }

    private void KnobEllipse_MouseLeave(object sender, MouseEventArgs e)
    {
        if (isCapturingMouse)
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
        ValueLabel.Visibility = Visibility.Hidden;
    }

    private void ValueTextField_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        ValueTextField.Visibility = Visibility.Collapsed;
        UpdateKnobMarker(MinValue, MaxValue, UnApplyPower(Value));
        OnPropertyChanged(nameof(ValueText));
    }

    private static void ValueChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        Knob k = (Knob)d;
        var min = k.MinValue;
        var max = k.MaxValue;
        double val = (double)e.NewValue;
        val = k.UnApplyPower(Math.Clamp(val, min, max));
        k.UpdateKnobMarker(min, max, val);
    }

    private void Grid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateKnobMarker();
    }

    private void Grid_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateKnobMarker();
    }

    private void UpdateKnobMarker()
    {
        var min = MinValue;
        var max = MaxValue;
        double val = Value;
        val = UnApplyPower(Math.Clamp(val, min, max));
        UpdateKnobMarker(min, max, val);
    }

    private void UpdateKnobMarker(double min, double max, double val)
    {
        KnobMarkerRot.CenterX = KnobMarker.ActualWidth / 2;
        KnobMarkerRot.CenterY = KnobEllipse.ActualHeight / 2;
        KnobMarkerRot.Angle = Remap(val, min, max, -145, 145);
    }

    private double Remap(double x, double in0, double in1, double out0, double out1)
    {
        return out0 + (out1 - out0) * (x - in0) / (in1 - in0);
    }

    private double ApplyPower(double x)
    {
        if (Power == 1)
            return x;

        double min = MinValue;
        double max = MaxValue;
        if (x >= 0)
        {
            min = Math.Max(min, 0);
            x = (x - min) / (max - min);
            return Math.Pow(x, Power) * (max - min) + min;
        }
        else
        {
            max = Math.Min(max, 0);
            x = 1 - (x - min) / (max - min);
            return (1 - Math.Pow(x, Power)) * (max - min) + min;
        }
    }

    private double UnApplyPower(double x)
    {
        if (Power == 1)
            return x;

        double min = MinValue;
        double max = MaxValue;
        if (x >= 0)
        {
            min = Math.Max(min, 0);
            x = (x - min) / (max - min);
            return Math.Pow(x, 1 / Power) * (max - min) + min;
        }
        else
        {
            max = Math.Min(max, 0);
            x = 1 - (x - min) / (max - min);
            return (1 - Math.Pow(x, 1 / Power)) * (max - min) + min;
        }
    }
}
