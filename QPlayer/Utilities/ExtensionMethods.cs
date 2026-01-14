using ColorPicker.Models;
using QPlayer.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Color = System.Drawing.Color;

namespace QPlayer.Utilities;

public static partial class ExtensionMethods
{
    public static Color ToColor(this ColorState x)
    {
        return Color.FromArgb(255, (byte)(x.RGB_R * 255), (byte)(x.RGB_G * 255), (byte)(x.RGB_B * 255));
    }

    public static System.Windows.Media.Color ToMediaColor(this ColorState x)
    {
        return System.Windows.Media.Color.FromRgb((byte)(x.RGB_R * 255), (byte)(x.RGB_G * 255), (byte)(x.RGB_B * 255));
    }

    public static System.Windows.Media.Color ToMediaColor(this ColorState x, byte alpha)
    {
        return System.Windows.Media.Color.FromArgb(alpha, (byte)(x.RGB_R * 255), (byte)(x.RGB_G * 255), (byte)(x.RGB_B * 255));
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

    public static int IndexOf<TList, TItem>(this IList<TList> list, Func<TList, TItem> selector, TItem value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            var sel = selector(list[i]);
            if (EqualityComparer<TItem>.Default.Equals(sel, value))
                return i;
        }
        return -1;
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

    /// <summary>
    /// Synchronises a list with this collection. Note that this synchronisation only works in one direction, if the list is 
    /// modified, then the behaviour is undefined.
    /// </summary>
    /// <typeparam name="TCollection"></typeparam>
    /// <typeparam name="TList"></typeparam>
    /// <param name="collection"></param>
    /// <param name="listGetter"></param>
    /// <param name="converter"></param>
    public static void SyncList<TCollection, TList>(this ObservableCollection<TCollection> collection, Func<IList<TList>?> listGetter,
        Func<TCollection, TList> converter)
    {
        collection.CollectionChanged += (o, e) => ObservableCollectionChangedHandler(e, listGetter, converter);
    }

    public static void UnSyncList<TCollection, TList>(this ObservableCollection<TCollection> collection, Func<IList<TList>?> listGetter,
        Func<TCollection, TList> converter)
    {
        collection.CollectionChanged -= (o, e) => ObservableCollectionChangedHandler(e, listGetter, converter);
    }

    private static void ObservableCollectionChangedHandler<TCollection, TList>(NotifyCollectionChangedEventArgs e,
        Func<IList<TList>?> listGetter, Func<TCollection, TList> converter)
    {
        var list = listGetter();
        if (list == null)
            return;

        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                list.Insert(e.NewStartingIndex, converter((TCollection)e.NewItems![0]!));
                break;
            case NotifyCollectionChangedAction.Remove:
                list.RemoveAt(e.OldStartingIndex);
                break;
            case NotifyCollectionChangedAction.Replace:
                list[e.OldStartingIndex] = converter((TCollection)e.NewItems![0]!);
                break;
            case NotifyCollectionChangedAction.Move:
                list.RemoveAt(e.OldStartingIndex);
                list.Insert(e.NewStartingIndex, converter((TCollection)e.NewItems![0]!));
                break;
            case NotifyCollectionChangedAction.Reset:
                list.Clear();
                /*vfModel.corners.RemoveRange(e.OldStartingIndex, )
                vfModel.corners.AddRange(e.NewItems);*/
                break;
        }
    }

    public static void HandleCollectionValueChange<T>(ObservableCollection<T> collection, T obj)
    {
        // This should trigger a Replace collection changed notification
        // TODO: This is a hopelessly stupid way of doing this
        int ind = collection.IndexOf(obj);
        collection[ind] = collection[ind];
    }

    /*private static Func<PointCollection, IList<System.Windows.Point>>? pointCollectionBackingCollectionGetter = null;
    private static Func<PointCollection, IList<System.Windows.Point>> PointCollectionBackingCollectionGetter
    {
        get
        {
            if (pointCollectionBackingCollectionGetter != null)
                return pointCollectionBackingCollectionGetter;

            var fld = typeof(PointCollection).GetField("_collection", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            pointCollectionBackingCollectionGetter = pc => Unsafe.As<IList<System.Windows.Point>>(fld.GetValue(pc)!);
            return pointCollectionBackingCollectionGetter;
        }
    }*/

    /// <summary>
    /// Efficiently adds a collection of points to the collection.
    /// </summary>
    /// <param name="collection"></param>
    /// <param name="points"></param>
    public static void AddRange(this PointCollection collection, IEnumerable<System.Windows.Point> points)
    {
        /*WritePreamble(collection);

        var baseCollection = PointCollectionBackingCollectionGetter(collection);// GetCollection(collection);
        foreach (var point in points)
            baseCollection.Add(point);

        GetSetVersion(collection)++;
        WritePostscript(collection);*/

        foreach (var point in points)
            AddWithoutFiringPublicEvents(collection, point);
        WritePostscript(collection);

        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "AddWithoutFiringPublicEvents")]
        static extern int AddWithoutFiringPublicEvents(PointCollection c, System.Windows.Point value);
        //[UnsafeAccessor(UnsafeAccessorKind.Method, Name = "WritePreamble")]
        //static extern void WritePreamble(Freezable c);
        [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "WritePostscript")]
        static extern void WritePostscript(Freezable c);
        //[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_collection")]
        //extern static ref IList<System.Windows.Point> GetCollection(PointCollection c);
        //[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "_version")]
        //extern static ref int GetSetVersion(PointCollection c);
    }
}
