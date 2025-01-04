using ImGuiNET;
using QPlayer.ViewModels;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace QPlayer.VideoPlugin;

public partial class GUIController : IDisposable
{
    private readonly ImGuiController controller;
    private readonly GL gl;
    private readonly IInputContext inputContext;
    private readonly IWindow window;
    private readonly MainViewModel vm;
    private readonly SynchronizationContext syncContext;
    private readonly GCHandle imguiIniPathHandle;

    private const int windowSnapRange = 10;

    private ImFontPtr? fontPtr;
#if DEBUG
    private bool imGUIDemoOpen = false;
#endif
    private bool shouldRepositionWindows = true;

    public IInputContext InputContext => inputContext;
    public IWindow Window => window;

    public GUIController(IWindow window, IInputContext inputContext, GL gl, MainViewModel vm)
    {
        this.window = window;
        this.gl = gl;
        this.inputContext = inputContext;
        ConfigureImGUI(window, inputContext, gl, vm, out imguiIniPathHandle, out controller);

        this.vm = vm;
        syncContext = SynchronizationContext.Current ?? new SynchronizationContext();

        if (inputContext.Keyboards.Count > 0)
        {
            inputContext.Keyboards[0].KeyDown += Kb_KeyDown;
        }

        window.FramebufferResize += Window_Resize;

        MainViewModel.Log("QPlayer video window GUI initialised!");
    }

    private void ConfigureImGUI(IWindow window, IInputContext inputContext, GL gl, MainViewModel vm,
        out GCHandle imguiIniPathHandle, out ImGuiController controller)
    {
        string fontPath = "guiFont.ttf";
        string fontPathFull = fontPath;//Program.GetResourcePath(fontPath);

        unsafe
        {
            string imguiIniPath = "imgui.ini";//Path.Combine(PersistantDataManager.appdataPath, "imgui.ini");
            var iniPathHandle = GCHandle.Alloc(Encoding.UTF8.GetBytes(imguiIniPath), GCHandleType.Pinned);
            imguiIniPathHandle = iniPathHandle;

            if (!File.Exists(fontPathFull))
                MainViewModel.Log($"Couldn't find font file '{fontPathFull}'!", MainViewModel.LogLevel.Warning);

            //var fontConfig = new ImGuiFontConfig(fontPathFull, 14);

            controller = new ImGuiController(
                gl,
                window,
                inputContext,
                //fontConfig,
                () =>
                {
                    var io = ImGui.GetIO();
                    io.NativePtr->IniFilename = (byte*)iniPathHandle.AddrOfPinnedObject();

                    try
                    {
                        ImFontPtr font;
                        var fontConfigPtr = ImGuiNative.ImFontConfig_ImFontConfig();
                        fontConfigPtr->RasterizerMultiply = 1;
                        fontConfigPtr->GlyphOffset = new Vector2(0);
                        fontConfigPtr->OversampleH = 3;
                        fontConfigPtr->OversampleV = 2;
                        //fontConfigPtr->FontDataOwnedByAtlas = 0;
                        fontConfigPtr->PixelSnapH = 1;

                        var bytes = File.ReadAllBytes(fontPathFull);
                        var ttfHandle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

                        font = io.Fonts.AddFontFromMemoryTTF(ttfHandle.AddrOfPinnedObject(), 15, 15, fontConfigPtr);
                        io.Fonts.Build();
                        ttfHandle.Free();

                        if (font.NativePtr == null || !font.IsLoaded())
                            MainViewModel.Log("Failed to load font file!", MainViewModel.LogLevel.Warning);

                        RecreateFontDeviceTexture();

                        io.FontGlobalScale = 1.0f;
                    }
                    catch (Exception ex)
                    {
                        MainViewModel.Log($"Failed to load font file!\n{ex.Message}", MainViewModel.LogLevel.Warning);
                    }

                    io.Fonts.Build();

                    ImGui.GetStyle().FrameRounding = 2;
                    ImGui.GetStyle().WindowRounding = 2;
                }
            );

            var fonts = ImGui.GetIO().Fonts;
            ImFontPtr? font = (fonts.Fonts.Size > 0) ? fonts.Fonts[fonts.Fonts.Size - 1] : null;
            if (!(font?.IsLoaded() ?? false))
                MainViewModel.Log("Failed to load font file!", MainViewModel.LogLevel.Warning);
            //ImGui.PushFont(font);
            fontPtr = font;
        }
    }

    private void RecreateFontDeviceTexture()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        io.Fonts.GetTexDataAsRGBA32(out IntPtr out_pixels, out int out_width, out int out_height, out int _);

        gl.GetInteger(GLEnum.TextureBinding2D, out var data);

        uint handle = gl.CreateTexture(TextureTarget.Texture2D);
        gl.BindTexture(TextureTarget.Texture2D, handle);
        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        unsafe
        {
            ref readonly byte bytes = ref Unsafe.AsRef<byte>((void*)out_pixels);
            gl.TexImage2D(GLEnum.Texture2D, 0, InternalFormat.Rgba8, (uint)out_width, (uint)out_height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, in bytes);
        }
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        io.Fonts.SetTexID((nint)handle);
        gl.BindTexture(GLEnum.Texture2D, (uint)data);
    }

    private void Kb_KeyDown(IKeyboard kb, Key key, int arg3)
    {
        if (ImGui.IsAnyItemActive())
            return;

        switch (key)
        {
            default:
                break;
        }
    }

    public void Render(double delta)
    {
        controller.Update((float)delta);
        if (fontPtr != null)
            ImGui.PushFont(fontPtr.Value);

        if (ImGui.BeginMainMenuBar())
        {
            if (ImGui.BeginMenu("File"))
            {
                

                ImGui.EndMenu();
            }
            if (ImGui.BeginMenu("Help"))
            {

                ImGui.EndMenu();
            }

            ImGui.EndMainMenuBar();
        }

#if DEBUG
        if (imGUIDemoOpen)
            ImGui.ShowDemoWindow(ref imGUIDemoOpen);
#endif

        if (fontPtr != null)
            ImGui.PopFont();

        controller.Render();
    }

    public void Dispose()
    {
        controller.Dispose();
        imguiIniPathHandle.Free();
    }

    private void Window_Resize(Silk.NET.Maths.Vector2D<int> size)
    {
        shouldRepositionWindows = true;
    }

    public void SnapWindowToEdge(ref WindowSnapEdge snapEdge)
    {
        var pos = ImGui.GetWindowPos();
        var size = ImGui.GetWindowSize();
        var viewSize = ImGui.GetWindowViewport().WorkSize;
        var workPos = ImGui.GetWindowViewport().WorkPos;
        pos -= workPos;

        var topLeft = Vector2.Abs(pos);
        var bottomRight = Vector2.Abs(pos + size - viewSize);
        var bottomRightNew = viewSize - size;

        if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Left) != 0)
            || (topLeft.X != 0 && topLeft.X <= windowSnapRange))
        {
            // Left snap
            pos.X = 0;
            ImGui.SetWindowPos(pos + workPos);
            snapEdge |= WindowSnapEdge.Left;
        }
        else if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Right) != 0)
            || (bottomRight.X != 0 && bottomRight.X <= windowSnapRange))
        {
            // Right snap
            pos.X = bottomRightNew.X;
            ImGui.SetWindowPos(pos + workPos);
            snapEdge |= WindowSnapEdge.Right;
        }
        else if (topLeft.X != 0 && bottomRight.X != 0)
        {
            // Clear any old left/right snaps if the window isn't on the edge
            snapEdge &= ~(WindowSnapEdge.Left | WindowSnapEdge.Right);
        }

        if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Top) != 0)
            || (topLeft.Y != 0 && topLeft.Y <= windowSnapRange))
        {
            // Top snap
            pos.Y = 0;
            ImGui.SetWindowPos(pos + workPos);
            snapEdge |= WindowSnapEdge.Top;
        }
        else if ((shouldRepositionWindows && (snapEdge & WindowSnapEdge.Bottom) != 0)
            || (bottomRight.Y != 0 && bottomRight.Y <= windowSnapRange))
        {
            // Bottom snap
            pos.Y = bottomRightNew.Y;
            ImGui.SetWindowPos(pos + workPos);
            snapEdge |= WindowSnapEdge.Bottom;
        }
        else if (topLeft.Y != 0 && bottomRight.Y != 0)
        {
            // Clear any old top/bottom snaps if the window isn't on the edge
            snapEdge &= ~(WindowSnapEdge.Top | WindowSnapEdge.Bottom);
        }
    }
}

[Flags]
public enum WindowSnapEdge
{
    None = 0,
    Left = 1 << 0,
    Right = 1 << 1,
    Top = 1 << 2,
    Bottom = 1 << 3,
}
