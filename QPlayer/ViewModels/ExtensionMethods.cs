using ColorPicker.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

public static partial class ExtensionMethods
{
    public static Color ToColor(this ColorState x)
    {
        return Color.FromArgb(255, (byte)(x.RGB_R * 255), (byte)(x.RGB_G * 255), (byte)(x.RGB_B * 255));
    }

    public static ColorState ToColorState(this Color x)
    {
        ColorState c = new();/*new()
        {
            A=1,
            RGB_R=x.r/255d,
            RGB_G=x.g/255d,
            RGB_B=x.b/255d
        };*/
        c.SetARGB(1, x.R / 255d, x.G / 255d, x.B / 255d);

        return c;
    }

    /// <summary>
    /// Removes trailing zeros from a decimal.
    /// </summary>
    /// <param name="value"></param>
    /// <remarks>https://stackoverflow.com/a/7983330/10874820</remarks>
    /// <returns></returns>
    public static decimal Normalize(this decimal value)
    {
        return value / 1.000000000000000000000000000000000m;
    }
}
