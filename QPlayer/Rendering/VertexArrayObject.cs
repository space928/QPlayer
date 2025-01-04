using System;
using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace QPlayer.Rendering;

public class VertexArrayObject<TVert, TInd> : IDisposable
    where TVert : unmanaged
    where TInd : unmanaged
{
    private readonly GL gl;
    private readonly uint handle;
    private readonly uint vboHandle;
    private readonly BufferObject<TVert> vbo;
    private readonly BufferObject<TInd>? ibo;

    private static uint currentVAO = 0;

    public BufferObject<TVert> VBO => vbo;
    public BufferObject<TInd>? IBO => ibo;

    public VertexArrayObject(GL gl, BufferObject<TVert> vbo, BufferObject<TInd>? ibo)
    {
        this.gl = gl;
        this.vbo = vbo;
        this.ibo = ibo;
        handle = gl.GenVertexArray();
        vboHandle = vbo.handle;
        Bind();
        vbo.Bind();
        ibo?.Bind();
        Unbind();
        vbo.Unbind();
        ibo?.Unbind();
    }

    public void VertexAttributePointer(uint index, int count, VertexAttribType type, uint vertexSize, uint offset)
    {
        if (currentVAO != handle)
            throw new Exception($"Attempted to configure vertex attributes on a VAO which isn't bound! (handle={handle}; current={currentVAO})");

        gl.EnableVertexAttribArray(index);
        gl.VertexAttribFormat(index, count, type, false, (uint)(offset * Marshal.SizeOf<TVert>()));
        gl.VertexAttribBinding(index, 0);
        gl.BindVertexBuffer(0, vboHandle, 0, (uint)(vertexSize * Marshal.SizeOf<TVert>()));
        //bindingIndex++;
        /*gl.VertexAttribPointer(index, count, type, false, vertexSize * (uint)sizeof(TVert), (void*)(offSet * sizeof(TVert)));*/
    }

    public void Bind()
    {
        if(currentVAO != handle)
            gl.BindVertexArray(handle);
        currentVAO = handle;
    }

    public void Unbind()
    {
        if(currentVAO != handle)
            throw new Exception($"VAO with handle {handle} is not currently bound and as such can't be unbound! (Current VAO = {currentVAO})");
        gl.BindVertexArray(0);
        currentVAO = 0;
    }

    public static void UnbindAny(GL gl)
    {
        gl.BindVertexArray(0);
        currentVAO = 0;
    }

    public void Dispose()
    {
        gl.DeleteVertexArray(handle);
        vbo.Dispose();
        ibo?.Dispose();
    }
}
