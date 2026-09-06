using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using static QPlayer.Views.LibraryImports;

namespace QPlayer.Views;

public partial class FileThumbnail : Image
{
    private double defaultHeight = -1;
    public string FilePath
    {
        get { return (string)GetValue(FilePathProperty); }
        set { SetValue(FilePathProperty, value); }
    }

    // Using a DependencyProperty as the backing store for FilePath.  This enables animation, styling, binding, etc...
    public static readonly DependencyProperty FilePathProperty =
        DependencyProperty.Register(nameof(FilePath), typeof(string), typeof(FileThumbnail), new PropertyMetadata(string.Empty, FilePathChangedHandler));

    public FileThumbnail() : base()
    {
        MouseDown += (o, e) =>
        {
            if (defaultHeight == -1)
                defaultHeight = MaxHeight;

            if (MaxHeight == defaultHeight)
                MaxHeight = defaultHeight * 2;
            else
                MaxHeight = defaultHeight;
        };
    }

    private static void FilePathChangedHandler(DependencyObject obj, DependencyPropertyChangedEventArgs e)
    {
        if (obj is not FileThumbnail control)
            return;

        string path = control.FilePath;
        Task.Run(() => LoadThumbnail(path)).ContinueWith(res =>
        {
            control.Dispatcher.Invoke(() =>
            {
                if (res.IsFaulted || res.Result == 0)
                {
                    control.Source = null;
                    return;
                }
                var bmp = res.Result;
                control.Source = Imaging.CreateBitmapSourceFromHBitmap(bmp, 0, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                Imports.DeleteObject(bmp);

                control.InvalidateMeasure();
                control.InvalidateVisual();
            });
        });
        
    }

    private static nint LoadThumbnail(string path)
    {
        if (Imports.SHCreateItemFromParsingName(path, 0, Imports.IShellItemGuid, out var shellItem) != 0)
            return 0;

        var factory = (IShellItemImageFactory)shellItem;
        factory.GetImage(new(512, 512), Imports.SIIGBF.SIIGBF_THUMBNAILONLY | Imports.SIIGBF.SIIGBF_BIGGERSIZEOK, out var hbmp);
        if (hbmp == 0)
            return 0;

        return hbmp;//System.Drawing.Image.FromHbitmap(hbmp);
    }


    internal static partial class Imports
    {
        public static readonly Guid IShellItemGuid = Guid.Parse("43826D1E-E718-42EE-BC55-A1E261C37BFE");

        [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
        public static partial int SHCreateItemFromParsingName(
            [MarshalAs(UnmanagedType.LPWStr)] string pszPath, 
            nint pbc,
            in Guid riid, 
            out IShellItem ppv);

        [LibraryImport("gdi32.dll", EntryPoint = "DeleteObject")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static partial bool DeleteObject(nint hObject);

        [Flags]
        public enum SIIGBF
        {
            SIIGBF_RESIZETOFIT = 0x00,
            SIIGBF_BIGGERSIZEOK = 0x01,
            SIIGBF_MEMORYONLY = 0x02,
            SIIGBF_ICONONLY = 0x04,
            SIIGBF_THUMBNAILONLY = 0x08,
            SIIGBF_INCACHEONLY = 0x10,
        }
    }

    [GeneratedComInterface]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IShellItem
    {

    }

    [GeneratedComInterface]
    [Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal partial interface IShellItemImageFactory
    {
        void GetImage(SIZE size, Imports.SIIGBF flags, out IntPtr phbm);
    }
}
