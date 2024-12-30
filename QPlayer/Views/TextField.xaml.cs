using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using static QPlayer.Views.LibraryImports;

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for TextField.xaml
/// </summary>
public partial class TextField : UserControl
{
    private bool isCapturingMouse;
    private POINT mouseStartPos;
    private double delta;

    public TextField()
    {
        InitializeComponent();
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool ShowSpinner
    {
        get { return (bool)GetValue(ShowSpinnerProperty); }
        set { SetValue(ShowSpinnerProperty, value); OnSpinnerVisibilityChanged(); }
    }

    public bool ReturnValidates
    {
        get { return (bool)GetValue(ReturnValidatesProperty); }
        set { SetValue(ReturnValidatesProperty, value); }
    }

    public bool ClampValue
    {
        get { return (bool)GetValue(ClampValueProperty); }
        set { SetValue(ClampValueProperty, value); }
    }

    public double MinValue
    {
        get { return (double)GetValue(MinValueProperty); }
        set { SetValue(MinValueProperty, value); }
    }

    public double MaxValue
    {
        get { return (double)GetValue(MaxValueProperty); }
        set { SetValue(MaxValueProperty, value); }
    }

    public double SpinRate
    {
        get { return (double)GetValue(SpinRateProperty); }
        set { SetValue(SpinRateProperty, value); }
    }

    public SpinnerType SpinnerType
    {
        get { return (SpinnerType)GetValue(SpinnerTypeProperty); }
        set { SetValue(SpinnerTypeProperty, value); }
    }

    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(TextField), new FrameworkPropertyMetadata
    {
        BindsTwoWayByDefault = true,
        DefaultUpdateSourceTrigger = UpdateSourceTrigger.LostFocus
    });

    public static readonly DependencyProperty ClampValueProperty =
        DependencyProperty.Register("ClampValue", typeof(bool), typeof(TextField), new PropertyMetadata(false));

    public static readonly DependencyProperty MinValueProperty =
        DependencyProperty.Register("MinValue", typeof(double), typeof(TextField), new PropertyMetadata(0d));

    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register("MaxValue", typeof(double), typeof(TextField), new PropertyMetadata(1d));

    public static readonly DependencyProperty SpinRateProperty =
        DependencyProperty.Register("SpinRate", typeof(double), typeof(TextField), new PropertyMetadata(1d));

    public static readonly DependencyProperty ReturnValidatesProperty =
        DependencyProperty.Register("ReturnValidates", typeof(bool), typeof(TextField), new PropertyMetadata(true));

    public static readonly DependencyProperty ShowSpinnerProperty =
        DependencyProperty.Register("ShowSpinner", typeof(bool), typeof(TextField), new PropertyMetadata(false));

    public static readonly DependencyProperty SpinnerTypeProperty =
        DependencyProperty.Register("SpinnerType", typeof(SpinnerType), typeof(TextField), new PropertyMetadata(SpinnerType.Double));

    private void TextBox_KeyUp(object? sender, KeyEventArgs e)
    {
        if (sender is not TextBox tb || !ReturnValidates)
            return;

        if (e.Key == Key.Enter)
        {
            var binding = BindingOperations.GetBindingExpression(tb, TextBox.TextProperty);
            binding?.UpdateSource();
            binding = BindingOperations.GetBindingExpression(this, TextProperty);
            binding?.UpdateSource();
            Keyboard.ClearFocus();
            //tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Down));
        }
    }

    private void OnSpinnerVisibilityChanged()
    {
        Spinner.Visibility = ShowSpinner ? Visibility.Visible : Visibility.Collapsed;
        TextBox.Margin = new Thickness(0, 0, ShowSpinner ? Spinner.ActualWidth : 0, 0);
    }

    private void Spinner_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;

        isCapturingMouse = true;
        e.MouseDevice.Capture(Spinner);
        ShowCursor(false);
        //GetCursorPos(out mouseStartPos);
        var spinnerCentre = Spinner.PointToScreen(new Point(Spinner.ActualWidth / 2, Spinner.ActualHeight / 2));
        mouseStartPos = new((int)spinnerCentre.X, (int)spinnerCentre.Y);
        SetCursorPos(mouseStartPos.x, mouseStartPos.y);
    }

    private void Spinner_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!IsEnabled)
            return;
        if (!isCapturingMouse)
            return;

        isCapturingMouse = false;
        SetCursorPos(mouseStartPos.x, mouseStartPos.y);
        e.MouseDevice.Capture(null);
        ShowCursor(true);
    }

    private void Spinner_MouseLeave(object sender, MouseEventArgs e)
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
    }

    private void Spinner_MouseMove(object sender, MouseEventArgs e)
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

        double spinRate = SpinRate;
        if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
            spinRate *= 0.2;
        else if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
            spinRate *= 5;

        delta -= deltaPos.y * spinRate;

        switch (SpinnerType)
        {
            case SpinnerType.Double:
                {
                    if (double.TryParse(Text, out var value))
                    {
                        value += delta;
                        value = Math.Round(value, (int)Math.Max(0, Math.Ceiling(-Math.Log10(spinRate))));
                        delta = 0;
                        if (ClampValue)
                        {
                            value = Math.Clamp(value, MinValue, MaxValue);
                            if (value == -0 && MinValue == 0) // Weird but whatev
                                value = 0;
                        }

                        Text = value.ToString();
                    }
                    break;
                }
            case SpinnerType.Int:
                {
                    if (int.TryParse(Text, out var value))
                    {
                        int deltaInt = (int)delta;
                        if (deltaInt != 0)
                        {
                            value += deltaInt;
                            delta = 0;
                            if (ClampValue)
                                value = Math.Clamp(value, (int)MinValue, (int)MaxValue);

                            Text = value.ToString();
                        }
                    }
                    break;
                }
            case SpinnerType.TimeSpan:
                {
                    // TODO: Add support for hours param
                    if (TimeSpanStringConverter.ConvertBack(Text, out var value))
                    {
                        var ticks = value.Ticks;
                        long deltaInt = (long)(delta * TimeSpan.TicksPerSecond);
                        if (deltaInt != 0)
                        {
                            ticks += deltaInt;
                            ticks = Math.Max(0, ticks); // For now only support positive time spans
                            delta = 0;
                            if (ClampValue)
                                ticks = Math.Clamp(ticks, (long)(MinValue * TimeSpan.TicksPerSecond), (long)(MaxValue * TimeSpan.TicksPerSecond));

                            Text = TimeSpanStringConverter.Convert(new TimeSpan(ticks));
                        }
                    }
                    break;
                }
        }

        var binding = BindingOperations.GetBindingExpression(this, TextProperty);
        binding?.UpdateSource();
    }

    private void Spinner_Loaded(object sender, RoutedEventArgs e)
    {
        OnSpinnerVisibilityChanged();
    }
}

public enum SpinnerType
{
    Double,
    Int,
    TimeSpan,
}
