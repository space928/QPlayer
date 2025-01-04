using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Rendering;

public class Texture2D : IDisposable
{
    public bool IsValid => isValid;
    public PixelFormat Format => pixelFormat;

    protected readonly uint handle;
    protected readonly GL gl;
    protected PixelFormat pixelFormat;
    protected InternalFormat internalFormat;
    protected PixelType pixelType;
    protected bool isValid = true;

    private static uint currentTexture = 0;
    private static Texture2D? missingTexture;

    public Texture2D(GL gl)
    {
        this.gl = gl;
        handle = gl.CreateTexture(TextureTarget.Texture2D);
    }

    private static Texture2D CreateMissingTexture(GL gl)
    {
        var tex = new Texture2D(gl);
        tex.Bind();

        uint size = 32;
        uint halfSize = size / 2;
        uint[] data = new uint[size*size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                data[y * size + x] = ((x >= halfSize) ^ (y >= halfSize)) ? 0xff000000 : 0xffff00ff;
            }
        }

        gl.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        gl.TexImage2D<byte>(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, size, size, 0, PixelFormat.Rgba, PixelType.UnsignedByte, MemoryMarshal.AsBytes<uint>(data));
        gl.GenerateMipmap(TextureTarget.Texture2D);
        gl.TextureParameter(tex.handle, TextureParameterName.TextureMinFilter, (int) TextureMinFilter.LinearMipmapLinear);
        gl.TextureParameter(tex.handle, TextureParameterName.TextureMagFilter, (int) TextureMagFilter.Linear);
        gl.TextureParameter(tex.handle, TextureParameterName.TextureMaxAnisotropy, 8);
        tex.Unbind();

        return tex;
    }

    public void Bind()
    {
        if (!isValid)
        {
            missingTexture ??= CreateMissingTexture(gl);
            missingTexture.Bind();
            return;
        }

        if (currentTexture != handle)
            gl.BindTexture(TextureTarget.Texture2D, handle);
        currentTexture = handle;
    }

    public void Bind(uint unit)
    {
        if (!isValid)
        {
            missingTexture ??= CreateMissingTexture(gl);
            missingTexture.Bind(unit);
            return;
        }

        gl.BindTextureUnit(unit, handle);
    }

    public static void BindTextures(uint startUnit, ReadOnlySpan<Texture2D> textures)
    {
        if (textures.Length < 1)
            return;

        Span<uint> handles = stackalloc uint[textures.Length];
        for (int i = 0; i < textures.Length; i++)
        {
            var tex = textures[i];
            var handle = tex.handle;
            if (!tex.isValid)
                handle = (missingTexture ??= CreateMissingTexture(tex.gl)).handle;
            handles[i] = handle;
        }

        var gl = textures[0].gl;
        gl.BindTextures(startUnit, handles);
    }

    public void Unbind()
    {
        if (missingTexture != null && currentTexture == missingTexture.handle && this != missingTexture)
        {
            missingTexture.Unbind();
            return;
        }
        if (currentTexture != handle)
            throw new Exception($"Can't unbind texture {handle} as it isn't bound! (current = {currentTexture})");
        gl.BindTexture(TextureTarget.Texture2D, 0);
        currentTexture = 0;
    }

    public void Dispose()
    {
        gl.DeleteTexture(handle);
        isValid = false;
    }
}
