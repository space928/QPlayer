using Silk.NET.Maths;
using Silk.NET.OpenGL;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.VideoPlugin;

public static partial class ExtensionMethods
{
    public static VectorInt2 ToVectorInt2(this Vector2D<int> vec) => Unsafe.As<Vector2D<int>, VectorInt2>(ref vec);
    public static Vector2D<int> ToVector2D(this VectorInt2 vec) => Unsafe.As<VectorInt2, Vector2D<int>>(ref vec);
    public static VectorInt3 ToVectorInt3(this Vector3D<int> vec) => Unsafe.As<Vector3D<int>, VectorInt3>(ref vec);
    public static Vector3D<int> ToVector3D(this VectorInt3 vec) => Unsafe.As<VectorInt3, Vector3D<int>>(ref vec);

    /*internal static (PixelFormat pxFmt, InternalFormat intFmt, PixelType pxType) ToOpenGLFormat(this ColorComponents components)
    {
        return components switch
        {
            ColorComponents.Grey => (PixelFormat.Red, InternalFormat.R8, PixelType.UnsignedByte),
            ColorComponents.GreyAlpha => (PixelFormat.RG, InternalFormat.RG8, PixelType.UnsignedByte),
            ColorComponents.RedGreenBlue => (PixelFormat.Rgb, InternalFormat.Rgb8, PixelType.UnsignedByte),
            ColorComponents.RedGreenBlueAlpha => (PixelFormat.Rgba, InternalFormat.Rgba8, PixelType.UnsignedByte),
            _ => throw new Exception("Unknown pixel format 'Default'!"),
        };
    }*/
}
