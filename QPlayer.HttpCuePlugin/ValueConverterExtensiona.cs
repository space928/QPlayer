using System;
using System.Globalization;
using System.Windows.Data;
using static QPlayer.HttpCuePlugin.HttpCueModel;

namespace QPlayer.HttpCuePlugin
{
    [ValueConversion(typeof(CueHttpMethod), typeof(int))]
    public class CueHttpMethodModeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (int)(CueHttpMethod)value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return (CueHttpMethod)(int)value;
        }
    }
}
