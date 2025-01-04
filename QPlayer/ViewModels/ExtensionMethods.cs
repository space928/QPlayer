using ColorPicker.Models;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
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

    /// <summary>
    /// Updates an item at the given index in the list, expanding the list with 
    /// <c>default</c> items if index larger than the size of the list.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="list"></param>
    /// <param name="index"></param>
    /// <param name="value"></param>
    public static void AddOrUpdate<T>(this List<T> list, int index, T value)
    {
        while (index >= list.Count)
            list.Add(default!);

        list[index] = value;
    }

    /// <summary>
    /// Sets the translation component of this matrix.
    /// </summary>
    /// <param name="mat"></param>
    /// <param name="trans"></param>
    /// <returns></returns>
    public static Matrix4x4 SetTranslation(this Matrix4x4 mat, Vector3 trans)
    {
        mat.M41 = trans.X;
        mat.M42 = trans.Y;
        mat.M43 = trans.Z;
        return mat;
    }

    public static Vector3 XZY(this Vector3 value) => new(value.X, value.Z, value.Y);
    public static Vector3 YXZ(this Vector3 value) => new(value.Y, value.X, value.Z);
    public static Vector3 ZYX(this Vector3 value) => new(value.Z, value.Y, value.X);

    public static Vector4 XZYW(this Vector4 value) => new(value.X, value.Z, value.Y, value.W);
    public static Vector4 YXZW(this Vector4 value) => new(value.Y, value.X, value.Z, value.W);
    public static Vector4 ZYXW(this Vector4 value) => new(value.Z, value.Y, value.X, value.W);
    public static Vector4 WZYX(this Vector4 value) => new(value.W, value.Z, value.Y, value.X);
}
