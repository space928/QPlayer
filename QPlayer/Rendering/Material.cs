using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using QPlayer.ViewModels;
using Silk.NET.OpenGL;

namespace QPlayer.Rendering;

public class Material
{
    public List<MaterialParameter> materialParameters = [];
    public BlendMode blendMode;
    public DepthMode depthMode;

    public RenderQueue MatRenderQueue => blendMode == BlendMode.Opaque || blendMode == BlendMode.Cutout ? RenderQueue.Opaque : RenderQueue.Transparent;

    public Shader shader;
    private readonly GL gl;
    private readonly List<Texture2D> boundTextures = [];

    public Material(GL gl, Shader shader)
    {
        this.shader = shader;
        this.gl = gl;
    }

    private void BindTextures()
    {
        int bindInd = 0;
        foreach (var matParam in materialParameters)
        {
            if (matParam is MaterialTextureParameter texParam)
            {
                if (texParam.value != null && !string.IsNullOrEmpty(texParam.UniformName))
                {
                    boundTextures.AddOrUpdate(bindInd, texParam.value);
                    shader.SetUniform(texParam.UniformName, bindInd);
                    bindInd++;
                }
            }
        }

        Texture2D.BindTextures(0, CollectionsMarshal.AsSpan(boundTextures)[..bindInd]);
    }

    public void Bind(bool needsToBindShader, ref (BlendingFactor src, BlendingFactor dst) lastBlendFunc, ref DepthMode lastDepthMode)
    {
        if (needsToBindShader)
            shader.Use();

        BindTextures();

        foreach (var matParam in materialParameters)
        {
            if (string.IsNullOrEmpty(matParam.UniformName))
                continue;

            switch (matParam)
            {
                case MaterialUniformParameter<bool> boolParam:
                    shader.SetUniform(boolParam.UniformName!, boolParam.value);
                    break;
                case MaterialUniformParameter<float> floatParam:
                    shader.SetUniform(floatParam.UniformName!, floatParam.value);
                    break;
                case MaterialUniformParameter<int> intParam:
                    shader.SetUniform(intParam.UniformName!, intParam.value);
                    break;
                case MaterialUniformParameter<uint> uintParam:
                    shader.SetUniform(uintParam.UniformName!, uintParam.value);
                    break;
                case MaterialUniformParameter<Vector2> vec2Param:
                    shader.SetUniform(vec2Param.UniformName!, vec2Param.value);
                    break;
                case MaterialUniformParameter<Vector3> vec3Param:
                    shader.SetUniform(vec3Param.UniformName!, vec3Param.value);
                    break;
                case MaterialUniformParameter<Vector4> vec4Param:
                    shader.SetUniform(vec4Param.UniformName!, vec4Param.value);
                    break;
                case MaterialUniformParameter<Matrix4x4> mat4Param:
                    shader.SetUniform(mat4Param.UniformName!, mat4Param.value);
                    break;
                default:
                    throw new NotImplementedException($"Material parameter of type {matParam.GetType().GenericTypeArguments[0].Name} is not yet supported!");
            }
        }

        (BlendingFactor src, BlendingFactor dst) blendFunc = blendMode switch
        {
            BlendMode.Opaque => (BlendingFactor.One, BlendingFactor.Zero),
            BlendMode.Cutout => (BlendingFactor.One, BlendingFactor.Zero),
            BlendMode.AlphaBlend => (BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha),
            BlendMode.AlphaPreMul => (BlendingFactor.One, BlendingFactor.OneMinusSrcAlpha),
            BlendMode.Add => (BlendingFactor.One, BlendingFactor.One),
            _ => throw new NotImplementedException(),
        };
        if (blendFunc != lastBlendFunc)
        {
            gl.BlendFunc(blendFunc.src, blendFunc.dst);
            lastBlendFunc = blendFunc;
        }

        var _depthMode = depthMode;
        if ((_depthMode & DepthMode.ZTest) != (lastDepthMode & DepthMode.ZTest))
        {
            //if ((_depthMode & DepthMode.ZTest) != 0)
            //    gl.DepthFunc(DepthFunction.Lequal);
            //else
            //    gl.DepthFunc(DepthFunction.Always);
        }
        if ((_depthMode & DepthMode.ZWrite) != (lastDepthMode & DepthMode.ZWrite))
        {
            if ((_depthMode & DepthMode.ZWrite) != 0)
                gl.DepthMask(true);
            else
                gl.DepthMask(false);
        }
        lastDepthMode = _depthMode;
    }

    public enum BlendMode
    {
        Opaque,
        Cutout,
        AlphaBlend,
        AlphaPreMul,
        Add
    }

    public enum RenderQueue
    {
        PreTransparent,
        Transparent,
        PreOpaque,
        Opaque,
        PostOpaque
    }

    [Flags]
    public enum DepthMode
    {
        None,
        ZTest = 1 << 0,
        ZWrite = 1 << 1,

        Opaque = ZTest | ZWrite,
    }

    public abstract class MaterialParameter
    {
        public virtual object? Value { get; set; }
        public string? UniformName { get; set; }
    }

    public class MaterialUniformParameter<T> : MaterialParameter
        where T : unmanaged
    {
        public override object? Value
        {
            get => value;
            set
            {
                ArgumentNullException.ThrowIfNull(value);
                this.value = (T)value;
            }
        }

        public T value;
    }

    public class MaterialTextureParameter : MaterialParameter
    {
        public override object? Value
        {
            get => value;
            set => this.value = (Texture2D?)value;
        }

        public Texture2D? value;
    }
}
