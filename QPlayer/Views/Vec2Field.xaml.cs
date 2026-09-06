using QPlayer.SourceGenerator;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Numerics;
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

namespace QPlayer.Views;

/// <summary>
/// Interaction logic for Vec2Field.xaml
/// </summary>
public partial class Vec2Field : UserControl, INotifyPropertyChanged, INotifyPropertyChanging
{
    public Vec2Field()
    {
        InitializeComponent();
    }

    [Reactive, Readonly]
    private Vector2 localValue;
    
    public event PropertyChangedEventHandler? PropertyChanged;
    public event PropertyChangingEventHandler? PropertyChanging;

    public Vector2 Value
    {
        get { return (Vector2)GetValue(ValueProperty); }
        set { SetValue(ValueProperty, value); }
    }

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(Vector2), typeof(Vec2Field), new FrameworkPropertyMetadata(Vector2.Zero, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, ValueChangedCallback));

    public bool ShowSpinner
    {
        get { return (bool)GetValue(ShowSpinnerProperty); }
        set { SetValue(ShowSpinnerProperty, value); }
    }

    // Using a DependencyProperty as the backing store for ShowSpinner.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty ShowSpinnerProperty =
        DependencyProperty.Register(nameof(ShowSpinner), typeof(bool), typeof(Vec2Field), new PropertyMetadata(true));

    public bool ClampValue
    {
        get { return (bool)GetValue(ClampValueProperty); }
        set { SetValue(ClampValueProperty, value); }
    }

    public static readonly DependencyProperty ClampValueProperty =
        DependencyProperty.Register(nameof(ClampValue), typeof(bool), typeof(Vec2Field), new PropertyMetadata(false));

    public double MinValue
    {
        get { return (double)GetValue(MinValueProperty); }
        set { SetValue(MinValueProperty, value); }
    }

    public static readonly DependencyProperty MinValueProperty =
        DependencyProperty.Register("MinValue", typeof(double), typeof(Vec2Field), new PropertyMetadata(0d));

    public double MaxValue
    {
        get { return (double)GetValue(MaxValueProperty); }
        set { SetValue(MaxValueProperty, value); }
    }
    
    public static readonly DependencyProperty MaxValueProperty =
        DependencyProperty.Register("MaxValue", typeof(double), typeof(Vec2Field), new PropertyMetadata(1d));

    public double SpinRate
    {
        get { return (double)GetValue(SpinRateProperty); }
        set { SetValue(SpinRateProperty, value); }
    }
    
    public static readonly DependencyProperty SpinRateProperty =
        DependencyProperty.Register("SpinRate", typeof(double), typeof(Vec2Field), new PropertyMetadata(1d));


    private static readonly PropertyChangedEventArgs xChangedArgs = new(nameof(X));
    private static readonly PropertyChangedEventArgs yChangedArgs = new(nameof(Y));
    public double X
    {
        get => Math.Round(localValue.X, 6);
        set => Value = new((float)value, localValue.Y);
    }

    public double Y
    {
        get => Math.Round(localValue.Y, 6);
        set => Value = new(localValue.X, (float)value);
    }

    static void ValueChangedCallback(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not Vector2 val || d is not Vec2Field obj)
            return;

        obj.localValue = val;
        obj.OnPropertyChanged(xChangedArgs);
        obj.OnPropertyChanged(yChangedArgs);
    }
}
