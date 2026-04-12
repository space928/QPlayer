using System;
using System.Buffers;
using System.Collections.Generic;
using System.Text;
using System.Threading;

namespace QPlayer.Utilities;

/// <summary>
/// An array pool which only stores arrays of a fixed size.
/// </summary>
/// <typeparam name="T"></typeparam>
public class FixedArrayPool<T> : ArrayPool<T>
{
    private SpinLock spinLock;
    private int head;
    private T[]?[] arrays;
    private readonly int arraySize;
    private readonly int maxCount;

    public FixedArrayPool(int arraySize, int initialNumber, int maxCount)
    {
        this.arraySize = arraySize;
        this.maxCount = maxCount;
        arrays = new T[initialNumber][];
        for (int i = 0; i < initialNumber; i++)
            arrays[i] = new T[arraySize];
    }

    public override T[] Rent(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minimumLength, arraySize);

        if (minimumLength <= 0)
            return [];

        T[]? result = null;
        bool lockTaken = false;
        try
        {
            spinLock.Enter(ref lockTaken);
            if (head >= 0)
                result = arrays[head--];
        }
        finally
        {
            if (lockTaken)
                spinLock.Exit();
        }

        if (result == null)
            return new T[arraySize];
        return result;
    }

    public override void Return(T[]? array, bool clearArray = false)
    {
        // Don't accept any bad arrays
        if (array == null || array.Length != arraySize)
            return;

        if (clearArray)
            Array.Clear(array, 0, array.Length);

        bool lockTaken = false;
        try
        {
            spinLock.Enter(ref lockTaken);

            // Don't accept any excess arrays
            if (head >= maxCount)
                return;

            head++;
            if (head >= arrays.Length)
                Grow();

            arrays[head] = array;
        }
        finally
        {
            if (lockTaken)
                spinLock.Exit();
        }
    }

    private void Grow()
    {
        var old = arrays;
        arrays = new T[old.Length << 1][];
        old.CopyTo(arrays);
    }
}
