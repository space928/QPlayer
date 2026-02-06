using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace QPlayer.Audio;

internal static class VectorExtensions
{
    /// <summary>
    /// Multiplies the contents of span a by the scalar b, mutating a with the result.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="a"></param>
    /// <param name="b"></param>
    public static void Multiply<T>(Span<T> a, T b)
        where T : INumber<T>
    {
        int len = a.Length;
        int i = 0;
        if (len < Vector<T>.Count || !Vector.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector<T>.Count;

        var vecB = new Vector<T>(b);
        ref T dst = ref MemoryMarshal.GetReference(a);
        /*
         Inner loop compiles to (x86_64 with AVX2, .NET 8, PGO Tier 1):
               vmulps   ymm2, ymm0, ymmword ptr [r8]
               vmovups  ymmword ptr [r8], ymm2
               add      r8, 32
               add      r9d, 8
               cmp      r11d, r9d
         */
        for (; i < len - remaining; i += Vector<T>.Count)
        {
            //var vecA = new Vector<T>(ref dst);
            var vecA = Vector.LoadUnsafe(ref dst);
            var res = Vector.Multiply(vecA, vecB);
            ref var dstByte = ref Unsafe.As<T, byte>(ref dst);
            Unsafe.WriteUnaligned(ref dstByte, res);
            dst = ref Unsafe.Add(ref dst, Vector<T>.Count);
        }

    CopyRemaining:
        for (; i < len; i++)
            a[i] *= b;
    }

    /// <summary>
    /// Multiplies the pairs of values in a by the values in b, mutating a with the result.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="a"></param>
    /// <param name="b"></param>
    public static void Multiply<T>(Span<T> a, Span<T> b)
        where T : INumber<T>
    {
        int len = a.Length;
        ArgumentOutOfRangeException.ThrowIfNotEqual(len, b.Length);
        int i = 0;
        if (len < Vector<T>.Count || !Vector.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector<T>.Count;

        ref T dst = ref MemoryMarshal.GetReference(a);
        ref T bRef = ref MemoryMarshal.GetReference(b);
        for (; i < len - remaining; i += Vector<T>.Count)
        {
            var vecA = Vector.LoadUnsafe(ref dst);
            var vecB = Vector.LoadUnsafe(ref bRef);

            var res = Vector.Multiply(vecA, vecB);
            ref var dstByte = ref Unsafe.As<T, byte>(ref dst);
            Unsafe.WriteUnaligned(ref dstByte, res);

            dst = ref Unsafe.Add(ref dst, Vector<T>.Count);
            bRef = ref Unsafe.Add(ref bRef, Vector<T>.Count);
        }

    CopyRemaining:
        for (; i < len; i++)
            a[i] *= b[i];
    }

    public static void ApplyPan(Span<float> buffer, float pan)
    {
        int len = buffer.Length;
        int i = 0;
        Vector2 lr = new(1 - Math.Max(0, pan), 1 + Math.Min(0, pan));
        if (len < Vector<float>.Count || !Vector.IsHardwareAccelerated)
            goto CopyRemaining;

        int remaining = len % Vector<float>.Count;

        // Create a vector of LRLRLRLR values by interpretting lr as a double and broadcasting it to the vector.
        var vecB = new Vector<double>(Unsafe.As<Vector2, double>(ref lr)).As<double, float>();
        ref float dst = ref MemoryMarshal.GetReference(buffer);
        for (; i < len - remaining; i += Vector<float>.Count)
        {
            var vecA = Vector.LoadUnsafe(ref dst);
            var res = Vector.Multiply(vecA, vecB);
            ref var dstByte = ref Unsafe.As<float, byte>(ref dst);
            Unsafe.WriteUnaligned(ref dstByte, res);
            dst = ref Unsafe.Add(ref dst, Vector<float>.Count);
        }

    CopyRemaining:
        for (; i < len; i += 2)
        {
            buffer[i] *= lr.X;
            buffer[i+1] *= lr.Y;
        }
    }
}
