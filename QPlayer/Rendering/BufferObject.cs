using Silk.NET.OpenGL;
using System;

namespace QPlayer.Rendering;

public class BufferObject<T> : IDisposable
    where T : unmanaged
{
    private readonly GL gl;
    private readonly BufferTargetARB type;
    public readonly uint handle;
    private int length = 0;

    /// <summary>
    /// Creates and initiliases a buffer object with StatisDraw usage.
    /// </summary>
    /// <param name="gl"></param>
    /// <param name="data"></param>
    /// <param name="type"></param>
    public BufferObject(GL gl, ReadOnlySpan<T> data, BufferTargetARB type)
    {
        this.gl = gl;
        this.type = type;

        handle = gl.GenBuffer();

        Bind();
        gl.BufferData(type, data, BufferUsageARB.StaticDraw);
        length = data.Length;
        Unbind();
    }

    public void Bind()
    {
        gl.BindBuffer(type, handle);
    }

    public void Unbind()
    {
        gl.BindBuffer(type, 0);
    }

    /// <summary>
    /// Updates the contents of this buffer. If <paramref name="replace"/> is <c>true</c> then 
    /// the entire contents of the buffer is replaced. Note that the size of the buffer can only 
    /// be changed if <paramref name="replace"/> is <c>true</c>.
    /// </summary>
    /// <param name="data">The new data to copy to the buffer.</param>
    /// <param name="replace">Whether the entire buffer should be replaced and resized to fit the data.</param>
    /// <param name="offset">The offset at which to start replacing data.</param>
    public void Update(ReadOnlySpan<T> data, bool replace = false, uint offset = 0)
    {
        // If we're replacing the data in this buffer then we must start at offset 0
        // If we're not replacing the whole buffer, then it cannot be resized.
        if (replace)
            ArgumentOutOfRangeException.ThrowIfNotEqual(offset, 0u);
        else
            ArgumentOutOfRangeException.ThrowIfGreaterThan(data.Length, length);

        Bind();
        if (replace && data.Length != length)
        {
            gl.BufferData(type, data, BufferUsageARB.StaticDraw);
            length = data.Length;
        }
        else
            gl.BufferSubData(type, (nint)offset, data);
        Unbind();
    }

    public void Dispose()
    {
        gl.DeleteBuffer(handle);
    }
}
