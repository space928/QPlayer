using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Views;

public partial class LibraryImports
{
    [StructLayout(LayoutKind.Sequential)]
    public struct POINT(int x, int y)
    {
        public int x = x;
        public int y = y;

        //public POINT() : this(0,0) { }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE(int w, int h)
    {
        public int w = w;
        public int h = h;

        //public POINT() : this(0,0) { }
    }

    [LibraryImport("user32.dll", EntryPoint = "SetCursorPos")]
    public static partial long SetCursorPos(int x, int y);

    [LibraryImport("user32.dll", EntryPoint = "GetCursorPos", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetCursorPos(out POINT lpPoint);

    /// <summary>
    /// Displays or hides the cursor.
    /// </summary>
    /// <param name="bShow">
    /// If bShow is TRUE, the display count is incremented by one. If bShow is FALSE, the display count is decremented by one.
    /// </param>
    /// <returns>The return value specifies the new display counter.</returns>
    [LibraryImport("user32.dll", EntryPoint = "ShowCursor")]
    public static partial int ShowCursor([MarshalAs(UnmanagedType.Bool)] bool bShow);
}
