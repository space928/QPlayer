using QPlayer.VideoPlugin.Rendering;
using QPlayer.ViewModels;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QPlayer.VideoPlugin;

public class VideoWindow
{
    private IWindow? window;
    private IInputContext? inputContext;
    private IKeyboard? mainKeyboard;
    private IMouse? mainMouse;
    private GL? gl;
    private GUIController? guiController;
    private readonly CancellationTokenSource isClosingTokenSource;
    private readonly CancellationToken isClosingToken;

    public string Title
    {
        get => window?.Title ?? string.Empty; 
        set => window?.Title = value;
    }

    //public override IDrawContext? DrawContext => drawContext;

    public VectorInt2 Size
    {
        get => window?.Size.ToVectorInt2() ?? VectorInt2.Zero; 
        set => window?.Size = value.ToVector2D();
    }
    public VectorInt2 Position
    {
        get
        {
            if (window == null)
                return VectorInt2.Zero;
            return window.Position.ToVectorInt2();
        }
        set => window?.Position = value.ToVector2D();
    }

    public VideoWindow(string title = "QPlayer Video Window")
    {
        isClosingTokenSource = new();
        isClosingToken = isClosingTokenSource.Token;
    }

    private void CreateWindow(string title)
    {
        var wndOptions = new WindowOptions(ViewOptions.Default);
        wndOptions.Samples = 4;
        wndOptions.Title = title;
        wndOptions.IsEventDriven = false;
        wndOptions.VSync = true;
        wndOptions.API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.Default, new(3, 3));
        wndOptions.IsEventDriven = true;

        window = Window.Create(wndOptions);

        window.Load += () =>
        {
            gl = window.CreateOpenGL();

#if DEBUG
            gl.DebugMessageControl(DebugSource.DontCare, DebugType.DontCare, DebugSeverity.DebugSeverityLow, null, true);
            nint tmp = 0;
            gl.DebugMessageCallback<nint>((GLEnum source, GLEnum type, int id, GLEnum severity, int length, nint message, nint userParam) =>
            {
                if ((uint)severity == (uint)DebugSeverity.DebugSeverityNotification)
                    return;
#if NETSTANDARD
                var msg = string.Empty;
                unsafe
                {
                    if (message != 0 && length > 0)
                        msg = Encoding.UTF8.GetString((byte*)message, length);
                }
#else
                string msg = Marshal.PtrToStringUTF8(message, length);
#endif
                MainViewModel.Log($"[GL_Debug] [{(DebugSeverity)severity}] [{(DebugType)type}] {msg}");
                // Debug.WriteLine($"[GL_Debug] [{(DebugSeverity)severity}] [{(DebugType)type}] {msg}");

                //if ((uint)severity == (uint)DebugSeverity.DebugSeverityHigh)
                //    Debugger.Break();
            }, ref tmp);
            gl.Enable(EnableCap.DebugOutput);
#endif

            inputContext = window.CreateInput();
            mainKeyboard = inputContext.Keyboards.Count > 0 ? inputContext.Keyboards[0] : null;
            mainMouse = inputContext.Mice.Count > 0 ? inputContext.Mice[0] : null;

            guiController = new(window, inputContext, gl, );
        };

        // The closing function
        window.Closing += () =>
        {
            isClosingTokenSource.Cancel();
            guiController?.Dispose();
            gl?.Dispose();
            inputContext?.Dispose();
        };

        // Handle resizes
        window.FramebufferResize += size =>
        {
            if (gl == null)
                return;

            gl.Viewport(size);
        };

        // The render function
        window.Render += delta =>
        {
            if (guiController == null || renderer == null || selectionManager == null || fileLoader == null)
                return;

            fps = ((float)(1 / delta) * .1f + fps * .9f);

            renderer.Render(delta);
            guiController.Render(delta);
            selectionManager.Update();
            fileLoader.OnFrame();
            O3DTexture.LoadTextureResults();
        };

        window.Initialize();
        window.DoEvents();
    }

    private void RunUIThread(string title)
    {
        CreateWindow(title);
        //nativeWnd?.Run();
        nativeWnd?.Initialize();
        nativeWnd?.DoEvents();
        while ((!nativeWnd?.IsClosing) ?? true)
        {
#if DEBUG_LATENCY
            LogLatency(DateTime.UtcNow.Ticks, "UI frame start");
            if (Interlocked.CompareExchange(ref redrawRequested, 0, 1) == 1)
                nativeWnd?.DoRender();
            LogLatency(DateTime.UtcNow.Ticks, "UI events start");
            nativeWnd?.DoEvents();
            LogLatency(DateTime.UtcNow.Ticks, "UI end");
#else
            // Don't redraw the frame unless the UI engine requests it
            // This is very important in reducing rendering latency,
            // since otherwise, we need to wait for the previous frames
            // to finish drawing before a frame with updated data can be
            // drawn.
            if (Interlocked.CompareExchange(ref redrawRequested, 0, 1) == 1)
                nativeWnd?.DoRender();
            // Wait for events, this can be cancelled if a redraw is requested
            nativeWnd?.DoEvents();
#endif
        }
    }
}
