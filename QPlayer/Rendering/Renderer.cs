using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Buffers;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using System.Diagnostics;
using static QPlayer.Rendering.Material;
using System.Drawing;

namespace QPlayer.Rendering;

internal class Renderer
{
    private readonly GL gl;
    private readonly IWindow window;
    private readonly Shader? mainShader;
    private readonly ErrorCallback onErrorMessageCB;
    private readonly Program program;

    private int lastRenderedVerts = 0;
    private int lastRenderedTris = 0;
    private int lastDrawCalls = 0;

    public delegate void ErrorCallback(string message);

    public SceneGraph scene;

    public int RenderedVerts => lastRenderedVerts;
    public int RenderedTris => lastRenderedTris;
    public int DrawCalls => lastDrawCalls;

    public Renderer(GL gl, IWindow window, ErrorCallback onErrorMessageCB, Program program)
    {
        this.gl = gl;
        this.onErrorMessageCB = onErrorMessageCB;
        this.program = program;
        scene = new SceneGraph();

        this.window = window;

        try
        {
            mainShader = new(gl, "main_vert.glsl", "main_frag.glsl");

            gl.Enable(EnableCap.DepthTest);
            gl.DepthFunc(DepthFunction.Lequal);
            gl.Enable(EnableCap.CullFace);
            gl.CullFace(TriangleFace.Back);
            gl.Enable(EnableCap.Multisample);
            gl.Enable(EnableCap.Blend);
            gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
            gl.BlendFunc(BlendingFactor.One, BlendingFactor.Zero);
            gl.Enable(EnableCap.PolygonOffsetFill);
            gl.Enable(EnableCap.PolygonOffsetLine);
        }
        catch (Exception ex)
        {
            onErrorMessageCB($"Error while initialising the renderer!\n{ex}");
        }

        /*Span<Light.ShaderData> lightData = stackalloc Light.ShaderData[Math.Min(scene.Lights.Count, Program.Settings.MaxLights)];
        lightData = Light.CopyLightData(scene.Lights.Take(Program.Settings.MaxLights), lightData);
        lightBuffer = new BufferObject<Light.ShaderData>(gl, lightData, BufferTargetARB.ShaderStorageBuffer);
        lightBuffer.Bind();
        gl.BindBufferBase(BufferTargetARB.ShaderStorageBuffer, SHADER_LIGHTS_SSBO_BINDING, lightBuffer.handle);
        lightBuffer.Unbind();*/
    }

    public void Render(double delta)
    {
        gl.ClearColor(Color.Black);
        gl.Clear((uint)ClearBufferMask.ColorBufferBit);
        var size = window.FramebufferSize;
        Vector2 depthPlanes = new(0.1f, 1000.0f);
        /*var proj = Matrix4x4.CreatePerspectiveFieldOfView(scene.camera.fieldOfView * (MathF.PI / 180), (float)size.X / size.Y, depthPlanes.X, depthPlanes.Y);
        scene.camera.OnRender(delta);
        var view = scene.camera.View;*/
        var proj = Matrix4x4.CreatePerspectiveFieldOfView(90 * (MathF.PI / 180), (float)size.X / size.Y, depthPlanes.X, depthPlanes.Y);
        var view = Matrix4x4.Identity;//scene.camera.View;

        VertexArrayObject<float, uint>.UnbindAny(gl);

        int renderedTris = 0, renderedVerts = 0, drawCalls = 0;

        gl.PolygonMode(TriangleFace.FrontAndBack, PolygonMode.Fill);
        gl.Enable(EnableCap.CullFace);

        //gl.Clear(ClearBufferMask.DepthBufferBit);
        //RenderMeshes(RenderQueue.PreTransparent, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
        //RenderMeshes(RenderQueue.Transparent, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
        //RenderMeshes(RenderQueue.PreOpaque, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
        RenderMeshes(RenderQueue.Opaque, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);
        //RenderMeshes(RenderQueue.PostOpaque, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);

        //RenderMeshes(RenderQueue.Transparent, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls);

        lastRenderedVerts = renderedVerts;
        lastRenderedTris = renderedTris;
        lastDrawCalls = drawCalls;
    }

    private void RenderMeshes(RenderQueue renderQueue, Matrix4x4 proj, Matrix4x4 view, ref int renderedTris, ref int renderedVerts, ref int drawCalls)
    {
        Material? lastMat = null;
        Shader? lastShader = null;
        (BlendingFactor src, BlendingFactor dst) lastBlend = (BlendingFactor.One, BlendingFactor.Zero);
        (DepthMode mode, float offset) lastDepthMode = (DepthMode.Opaque, 0);
        gl.DepthFunc(DepthFunction.Lequal);
        gl.DepthMask(true);
        gl.BlendFunc(lastBlend.src, lastBlend.dst);
        gl.Clear(ClearBufferMask.DepthBufferBit);

        foreach (SceneObject o in scene.SceneObjects)
        {
            RenderMesh(o, renderQueue, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls,
                ref lastMat, ref lastShader, ref lastBlend, ref lastDepthMode);
        }
        // TODO: We could progressively sort the list of renderers to reduce the number of shader/material changes?
        //       a bit like dynamic batching?
    }

    private void RenderMesh(SceneObject o, RenderQueue renderQueue, Matrix4x4 proj, Matrix4x4 view, ref int renderedTris,
        ref int renderedVerts, ref int drawCalls, ref Material? lastMat, ref Shader? lastShader,
        ref (BlendingFactor src, BlendingFactor dst) lastBlend, ref (DepthMode mode, float offset) lastDepthMode)
    {
        if (o.children != null)
            foreach (var child in o.children)
                RenderMesh(child, renderQueue, proj, view, ref renderedTris, ref renderedVerts, ref drawCalls,
                    ref lastMat, ref lastShader, ref lastBlend, ref lastDepthMode);

        if (o is not Mesh m)
            return;

        if (!m.visible)
            return;

        if (renderQueue == RenderQueue.Opaque)
        {
            renderedVerts += m.VertCount;
            renderedTris += m.TriCount;
        }

        var model = m.transform.Matrix;
        foreach (var sm in m.submeshes)
        {
            // Only update shader parameters if needed
            if (lastMat != sm.mat)
            {
                bool shaderChanged = lastShader != sm.mat.shader;
                sm.mat.Bind(shaderChanged, ref lastBlend, ref lastDepthMode.mode);
                lastMat = sm.mat;
                lastShader = sm.mat.shader;

                if (shaderChanged)
                {
                    // Whenever the shader is changed we should need to reset the view and projection matrices
                    // The current light (forward rendering) also needs to be set whenever it's changed
                    lastShader.SetUniform("uView", view);
                    lastShader.SetUniform("uProj", proj);
                    lastShader.SetUniform("uTime", (float)window.Time);
                    //lastShader.SetUniform("uViewDir", scene.camera.direction);
                    //lastShader.SetUniform("uViewPos", scene.camera.transform.Pos);
                }
            }

            sm.mat.shader.SetUniform("uModel", model);

            sm.vao.Bind();
            gl.DrawElements(PrimitiveType.Triangles, sm.vertCount, DrawElementsType.UnsignedInt, (ReadOnlySpan<uint>)null);

            drawCalls++;
        }
    }

    /*public Mesh CreateMeshFromO3D(O3DFile o3d, string path, MeshCommand? meshCommand, string? cfgPath)
    {
        if (mainShader == null)
            throw new Exception("Main shader was not initialised, object cannot be created!");
        return new O3DMesh(o3d, path, gl, mainShader, scene, meshCommand, cfgPath);
    }*/

    private void DBG_LogBoundResources()
    {
        Debug.WriteLine("### Currently bound OpenGL Resources ###");
        int handle;

        gl.GetInteger(GetPName.ActiveTexture, out handle);
        Debug.WriteLine($"\tActiveTexture = {handle}");

        gl.GetInteger(GetPName.BlendEquation, out handle);
        Debug.WriteLine($"\tBlendEquation = {handle}");

        gl.GetInteger(GetPName.ElementArrayBufferBinding, out handle);
        Debug.WriteLine($"\tElementArrayBufferBinding = {handle}");

        gl.GetInteger(GetPName.ArrayBufferBinding, out handle);
        Debug.WriteLine($"\tArrayBufferBinding = {handle}");

        gl.GetInteger(GetPName.VertexArrayBinding, out handle);
        Debug.WriteLine($"\tVertexArrayBinding = {handle}");

        gl.GetInteger(GetPName.UniformBufferBinding, out handle);
        Debug.WriteLine($"\tUniformBufferBinding = {handle}");

        gl.GetInteger(GetPName.ProgramPipelineBinding, out handle);
        Debug.WriteLine($"\tProgramPipelineBinding = {handle}");
    }
}
