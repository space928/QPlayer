using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows;

namespace QPlayer.Utilities;

[ValueConversion(typeof(TimeSpan[]), typeof(double))]
public class ElapsedTimeConverter : IMultiValueConverter
{
    public object Convert(object[] value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value[0] == DependencyProperty.UnsetValue || value[1] == DependencyProperty.UnsetValue)
            return 0d;
        if (((TimeSpan)value[1]).TotalSeconds == 0)
            return 0d;
        return ((TimeSpan)value[0]).TotalSeconds / ((TimeSpan)value[1]).TotalSeconds * 100;
    }

    public object[] ConvertBack(object value, Type[] targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

[ValueConversion(typeof(float), typeof(GridLength))]
public class FloatGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return new GridLength((float)value);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        GridLength gridLength = (GridLength)value;
        return (float)gridLength.Value;
    }
}

[ValueConversion(typeof(TimeSpan), typeof(string))]
public class TimeSpanStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return Convert((TimeSpan)value, parameter is string useHours && useHours == "True");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string timeSpan = (string)value;

        if (ConvertBack(timeSpan, out TimeSpan ret, parameter is string useHours && useHours == "True"))
            return ret;

        return DependencyProperty.UnsetValue;
    }

    public static string Convert(TimeSpan value, bool useHours = false)
    {
        if (useHours)
            return value.ToString(@"hh\:mm\:ss\.ff");
        else
            return value.ToString(@"mm\:ss\.ff");
    }

    public static bool ConvertBack(string value, out TimeSpan result, bool useHours = false)
    {
        if (useHours)
            return TimeSpan.TryParse(value, out result);
        else
            return TimeSpan.TryParse($"00:{value}", out result);
    }
}

[ValueConversion(typeof(double), typeof(bool))]
public class GreaterThanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return (double)value > double.Parse((string)parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }
}

[ValueConversion(typeof(double), typeof(double))]
public class MultiplyByConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return (double)value * double.Parse((string)parameter);
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }
}
