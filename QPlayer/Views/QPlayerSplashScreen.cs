using QPlayer.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using static QPlayer.Views.LibraryImports;

namespace QPlayer.Views;

partial class QPlayerSplashScreen : IDisposable
{
    private readonly Stream? imgResStream;
    private readonly nint hInstance;
    private readonly Dispatcher dispatcher;
    private nint hWnd;
    private Bitmap? splashImgBmp;
    private Bitmap? gdiBmp;
    private Graphics? hGraphics;
    private Font? statusFont;
    private Brush? statusBrush;
    private nint screenDC;
    private nint wndDC;
    private POINT wndPos;
    private SIZE wndSize;
    private Rectangle statusTextRect;
    private StringFormat? statusTextFormat;

    public QPlayerSplashScreen(string resourceName)
    {
        var resAssembly = Assembly.GetCallingAssembly();
        imgResStream = resAssembly.GetManifestResourceStream(resourceName);
        hInstance = Marshal.GetHINSTANCE(resAssembly.ManifestModule);
        dispatcher = Dispatcher.CurrentDispatcher;
    }

    public void Show(bool autoClose)
    {
        if (imgResStream == null)
            return;

        if (autoClose)
        {
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                (DispatcherOperationCallback)(static arg =>
                {
                    ((QPlayerSplashScreen)arg).Close();
                    return null;
                }),
                this);
        }

        var bmp = gdiBmp = new Bitmap(imgResStream);
        splashImgBmp = new Bitmap(bmp);
        wndSize = new(bmp.Width, bmp.Height);
        var wnd = hWnd = CreateWindow(wndSize.w, wndSize.h, false, out wndPos);

        bmp.ConvertFormat(System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var hBmp = bmp.GetHbitmap(Color.Black);

        UpdateWnd(hBmp);

        var graphics = hGraphics = Graphics.FromImage(bmp);
        statusFont = new Font(FontFamily.GenericSansSerif, 11);
        statusBrush = new SolidBrush(Color.FromArgb(90, Color.White));
        statusTextRect = new(4, wndSize.h - 21, wndSize.w - 10, 14);
        statusTextFormat = new StringFormat(StringFormatFlags.FitBlackBox | StringFormatFlags.LineLimit)
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };
        // statusBGBrush = new TextureBrush(bmp, statusTextRect);
        DrawVersionNumber();

        MainViewModel.LogList.CollectionChanged += LogList_CollectionChanged;
    }

    private void DrawVersionNumber()
    {
        if (statusFont == null || statusTextFormat == null || statusBrush == null || hGraphics == null)
            return;

        var name = Assembly.GetCallingAssembly().GetName();
        var text = $"Version: {name.Version}";
        hGraphics.DrawString(text, statusFont, statusBrush, 26, 266, statusTextFormat);

        var hBmp = gdiBmp!.GetHbitmap(Color.Black);
        UpdateWnd(hBmp);
    }

    private void LogList_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
    {
        if (dispatcher.CheckAccess())
        {
            UpdateStatusText(e.NewItems);
        }
        else
        {
            dispatcher.BeginInvoke(UpdateStatusText, DispatcherPriority.Normal, e.NewItems);
        }

        void UpdateStatusText(object? arg)
        {
            if (hGraphics == null || statusBrush == null || statusFont == null)
                return;

            if (arg is not IList strings || strings.Count == 0)
                return;

            string text = (string)strings[^1]!;
            int nlPos = text.IndexOf('\n');
            nlPos = nlPos < 0 ? text.Length : (nlPos - 1);
            int splitPos = text.AsSpan(0, nlPos).LastIndexOf(']');
            if (text.Length > splitPos + 2 && text[splitPos + 2] == ' ') // Skip indented messages
                return;
            if (splitPos > 0)
                text = text[(splitPos + 1)..nlPos];

            hGraphics.DrawImage(splashImgBmp!, statusTextRect, statusTextRect, GraphicsUnit.Pixel);
            hGraphics.DrawString(text, statusFont, statusBrush, statusTextRect, statusTextFormat);

            var hBmp = gdiBmp!.GetHbitmap(Color.Black);
            UpdateWnd(hBmp);
        }
    }

    public void Close()
    {
        Dispose();
    }

    public void Dispose()
    {
        MainViewModel.LogList.CollectionChanged -= LogList_CollectionChanged;

        if (hWnd != 0)
            DestroyWindow(hWnd);

        // ReleaseDC(hWnd, wndDC);
        // ReleaseDC(hWnd, screenDC);

        gdiBmp?.Dispose();
        hGraphics?.Dispose();
        statusBrush?.Dispose();
        statusFont?.Dispose();
    }

    private unsafe nint CreateWindow(int width, int height, bool topMost, out POINT pos)
    {
        ushort windowClass = 0;
        string className = "QPLayerSplashClass";
        fixed (char* _className = className)
        {
            WNDCLASSEXW wndClass = new()
            {
                cbSize = (uint)sizeof(WNDCLASSEXW),
                style = 3 /*CS_VREDRAW | CS_HREDRAW*/,
                lpszClassName = _className,
                lpfnWndProc = &WndProc,
                hInstance = hInstance
            };

            windowClass = RegisterClassExW(&wndClass);
        }

        if (windowClass == 0)
        {
            int lastWin32Error = Marshal.GetLastWin32Error();
            if (lastWin32Error != 0x582) // Ignore class already exists errors
                throw new Win32Exception(lastWin32Error);
        }

        int screenWidth = GetSystemMetrics(0 /* SM_CXSCREEN */);
        int screenHeight = GetSystemMetrics(1/* SM_CYSCREEN*/ );
        int x = (screenWidth - width) / 2;
        int y = (screenHeight - height) / 2;

        WindowStylesEx windowCreateFlags = WindowStylesEx.WS_EX_WINDOWEDGE
            | WindowStylesEx.WS_EX_TOOLWINDOW
            | WindowStylesEx.WS_EX_LAYERED
            | (topMost ? WindowStylesEx.WS_EX_TOPMOST : 0);

        var wnd = CreateWindowExW(windowCreateFlags, className, "QPlayerSplash", 0x80000000 | 0x10000000 /*WS_POPUP | WS_VISIBLE */, x, y, width, height, 0, 0, hInstance, 0);
        screenDC = GetDC(0);
        wndDC = CreateCompatibleDC(screenDC);

        //SetLayeredWindowAttributes(wnd, 0, 255, 2 /* LWA_ALPHA */);
        pos = new(x, y);

        return wnd;
    }

    private unsafe void UpdateWnd(nint hbmp)
    {
        BLENDFUNCTION blendFunction = new()
        {
            BlendOp = 0 /* AC_SRC_OVER */,
            SourceConstantAlpha = 255,
            AlphaFormat = 1 /* AC_SRC_ALPHA */
        };
        POINT srcPoint = default;

        // var screenDC = GetDC(0);
        // var wndDC = CreateCompatibleDC(screenDC);
        SelectObject(wndDC, hbmp);

        var pos = wndPos;
        var size = wndSize;
        UpdateLayeredWindow(hWnd, screenDC, &pos, &size, wndDC, &srcPoint, 0, &blendFunction, 2 /* ULW_ALPHA */);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe nint WndProc(nint hWnd, uint Msg, nint wParam, nuint lParam)
    {
        return DefWindowProcW(hWnd, Msg, wParam, lParam);
    }

    [LibraryImport("User32", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint CreateWindowExW(WindowStylesEx dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
        int X, int Y, int nWidth, int nHeight, nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("User32")]
    internal static partial nint DefWindowProcW(nint hWnd, uint Msg, nint wParam, nuint lParam);

    [LibraryImport("User32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("User32")]
    internal static unsafe partial ushort RegisterClassExW(WNDCLASSEXW* unnamedParam1);

    [LibraryImport("User32")]
    internal static partial int GetSystemMetrics(int index);

    [LibraryImport("User32")]
    internal static unsafe partial int BeginPaint(nint hWnd, PAINTSTRUCT* lpPaint);

    [LibraryImport("User32")]
    internal static partial nint GetDC(nint hWnd);

    [LibraryImport("User32")]
    internal static partial nint ReleaseDC(nint hWnd, nint hdc);

    [LibraryImport("Gdi32")]
    internal static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport("User32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool UpdateLayeredWindow(nint hWnd, nint hdcDst, POINT* pptDst,
        SIZE* psize, nint hdcSrc, POINT* pptSrc, int crKey, BLENDFUNCTION* pblend, int dwFlags);

    [LibraryImport("User32")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool SetLayeredWindowAttributes(nint hwnd, int crKey, byte bAlpha, int dwFlags);

    [LibraryImport("Gdi32")]
    internal static partial nint SelectObject(nint hdc, nint obj);

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public delegate* unmanaged[Stdcall]<nint, uint, nint, nuint, nint> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public char* lpszMenuName;
        public char* lpszClassName;
        public nint hIconSm;
    }

    [Flags]
    public enum WindowStylesEx : uint
    {
        WS_EX_ACCEPTFILES = 0x00000010,
        WS_EX_APPWINDOW = 0x00040000,
        WS_EX_CLIENTEDGE = 0x00000200,
        WS_EX_COMPOSITED = 0x02000000,
        WS_EX_CONTEXTHELP = 0x00000400,
        WS_EX_CONTROLPARENT = 0x00010000,
        WS_EX_DLGMODALFRAME = 0x00000001,
        WS_EX_LAYERED = 0x00080000,
        WS_EX_LAYOUTRTL = 0x00400000,
        WS_EX_LEFT = 0x00000000,
        WS_EX_LEFTSCROLLBAR = 0x00004000,
        WS_EX_LTRREADING = 0x00000000,
        WS_EX_MDICHILD = 0x00000040,
        WS_EX_NOACTIVATE = 0x08000000,
        WS_EX_NOINHERITLAYOUT = 0x00100000,
        WS_EX_NOPARENTNOTIFY = 0x00000004,
        WS_EX_OVERLAPPEDWINDOW = WS_EX_WINDOWEDGE | WS_EX_CLIENTEDGE,
        WS_EX_PALETTEWINDOW = WS_EX_WINDOWEDGE | WS_EX_TOOLWINDOW | WS_EX_TOPMOST,
        WS_EX_RIGHT = 0x00001000,
        WS_EX_RIGHTSCROLLBAR = 0x00000000,
        WS_EX_RTLREADING = 0x00002000,
        WS_EX_STATICEDGE = 0x00020000,
        WS_EX_TOOLWINDOW = 0x00000080,
        WS_EX_TOPMOST = 0x00000008,
        WS_EX_TRANSPARENT = 0x00000020,
        WS_EX_WINDOWEDGE = 0x00000100
    }

    [StructLayout(LayoutKind.Sequential)]
    internal unsafe struct PAINTSTRUCT
    {
        public nint hdc;
        public int fErase;
        public Rect rcPaint;
        public int fRestore;
        public int fIncUpdate;
        public fixed byte rgbReserved[32];
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    internal struct BLENDFUNCTION
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }
}
