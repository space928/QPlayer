using QPlayer.ViewModels;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Rendering;

// Adapted from https://github.com/dotnet/Silk.NET/blob/main/examples/CSharp/OpenGL%20Tutorials/Tutorial%202.1%20-%20Co-ordinate%20Systems/Shader.cs
public class Shader : IDisposable
{
    private readonly uint handle;
    private readonly GL gl;

    private readonly Dictionary<string, int> uniformLocationCache = [];

    public Shader(GL gl, string vertexPath, string fragmentPath)
    {
        this.gl = gl;

        uint vertex = LoadShader(ShaderType.VertexShader, vertexPath);
        uint fragment = LoadShader(ShaderType.FragmentShader, fragmentPath);
        handle = this.gl.CreateProgram();
        this.gl.AttachShader(handle, vertex);
        this.gl.AttachShader(handle, fragment);
        this.gl.LinkProgram(handle);
        this.gl.GetProgram(handle, GLEnum.LinkStatus, out var status);
        if (status == 0)
        {
            throw new Exception($"Program failed to link with error: {this.gl.GetProgramInfoLog(handle)}");
        }
        this.gl.DetachShader(handle, vertex);
        this.gl.DetachShader(handle, fragment);
        this.gl.DeleteShader(vertex);
        this.gl.DeleteShader(fragment);
    }

    public void Use()
    {
        gl.UseProgram(handle);
    }

    public bool GetLocation(out int location, string name, bool silentFail)
    {
        if (uniformLocationCache.TryGetValue(name, out location))
            return true;

        location = gl.GetUniformLocation(handle, name);
        if (location == -1)
        {
            if (silentFail)
                return false;
            throw new Exception($"{name} uniform not found on shader.");
        }
        uniformLocationCache.Add(name, location);
        return true;
    }

    public void SetUniform(string name, int value, bool silentFail = true)
    {
        if (GetLocation(out int location, name, silentFail))
            gl.Uniform1(location, value);
    }

    public void SetUniform(string name, bool value, bool silentFail = true)
    {
        if (GetLocation(out int location, name, silentFail))
            gl.Uniform1(location, value ? 1f : 0f);
    }

    public void SetUniform(string name, Matrix4x4 value, bool silentFail = true)
    {
        if (GetLocation(out int location, name, silentFail))
            gl.UniformMatrix4(location, 1, false, MemoryMarshal.Cast<Matrix4x4, float>(MemoryMarshal.CreateReadOnlySpan(in value, 1)));
    }

    public void SetUniform(string name, float value, bool silentFail = true)
    {
        if (GetLocation(out int location, name, silentFail))
            gl.Uniform1(location, value);
    }

    public void SetUniform(string name, Vector2 value, bool silentFail = true)
    {
        if (GetLocation(out int location, name, silentFail))
            gl.Uniform2(location, value);
    }

    public void SetUniform(string name, Vector3 value, bool silentFail = true)
    {
        if (GetLocation(out int location, name, silentFail))
            gl.Uniform3(location, value);
    }

    public void SetUniform(string name, Vector4 value, bool silentFail = true)
    {
        if (GetLocation(out int location, name, silentFail))
            gl.Uniform4(location, value);
    }

    public void Dispose()
    {
        gl.DeleteProgram(handle);
    }

    private uint LoadShader(ShaderType type, string path)
    {
        string src;
        try
        {
            src = File.ReadAllText(path);
        }
        catch
        {
            // If we can't find the shader file locally, try loading it from embedded resources...
            using Stream stream = MainViewModel.LoadResourceFile(path);
            using StreamReader reader = new(stream);
            src = reader.ReadToEnd();
        }
        uint handle = gl.CreateShader(type);
        gl.ShaderSource(handle, src);
        gl.CompileShader(handle);
        string infoLog = gl.GetShaderInfoLog(handle);
        if (!string.IsNullOrWhiteSpace(infoLog))
        {
            throw new Exception($"Error compiling shader of type {type}, failed with error {infoLog}");
        }

        return handle;
    }
}
