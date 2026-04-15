using System;
using System.Collections;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace QPlayer.Utilities;

/// <summary>
/// A simple double-ended queue implementation. This implementation is not thread-safe.
/// </summary>
/// <typeparam name="T"></typeparam>
internal class Deque<T> : ICollection<T>
{
    private T[] array;
    /// <summary>
    /// Points to the element at the start of the queue.
    /// </summary>
    private int head;
    /// <summary>
    /// Points to the free slot at the end of the queue.
    /// </summary>
    private int tail;
    private int count;
    private int version;

    public Deque()
    {
        array = [];
    }

    public Deque(int capacity)
    {
        array = new T[BitOperations.RoundUpToPowerOf2((uint)capacity)];
    }

    public Deque(IEnumerable<T> source)
    {
        if (source is ICollection<T> col)
        {
            array = new T[BitOperations.RoundUpToPowerOf2((uint)col.Count)];
            col.CopyTo(array, 0);
        }
        else
        {
            array = [];
            foreach (var item in source)
                Add(item);
        }
    }

    public int Count => count;
    public int Capacity => array.Length;
    public bool IsReadOnly => false;

    /// <summary>
    /// Increments the given value, wrapping on the array bounds.
    /// </summary>
    /// <param name="ptr"></param>
    /// <returns>The original value of <paramref name="ptr"/></returns>
    private int Inc(ref int ptr)
    {
        var val = ptr;
        var orig = val;
        val++;
        if (val >= array.Length)
            val = 0;
        ptr = val;
        return orig;
    }

    /// <summary>
    /// Decrements the given value, wrapping on the array bounds.
    /// </summary>
    /// <param name="ptr"></param>
    /// <returns>The new value of <paramref name="ptr"/></returns>
    private int Dec(ref int ptr)
    {
        var val = ptr;
        val--;
        if (val < 0)
            val = array.Length - 1;
        ptr = val;
        return val;
    }

    public void PushStart(T item)
    {
        if (count == array.Length)
            Grow();

        array[Dec(ref head)] = item;

        count++;
        version++;
    }

    public void PushEnd(T item)
    {
        if (count == array.Length)
            Grow();

        array[Inc(ref tail)] = item;

        count++;
        version++;
    }

    public bool TryPopStart(out T item)
    {
        if (count == 0)
        {
            item = default!;
            return false;
        }

        int ind = Inc(ref head);
        item = array[ind];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            array[ind] = default!;

        count--;
        version++;

        return true;
    }

    public bool TryPopEnd(out T item)
    {
        if (count == 0)
        {
            item = default!;
            return false;
        }

        int ind = Dec(ref tail);
        item = array[ind];
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            array[ind] = default!;

        count--;
        version++;

        return true;
    }

    public T PopStart()
    {
        if (TryPopStart(out T item))
            return item;
        throw new InvalidOperationException("Deque is empty");
    }

    public T PopEnd()
    {
        if (TryPopEnd(out T item))
            return item;
        throw new InvalidOperationException("Deque is empty");
    }

    public bool TryPeekStart(out T item)
    {
        if (count == 0)
        {
            item = default!;
            return false;
        }

        item = array[head];

        return true;
    }

    public bool TryPeekEnd(out T item)
    {
        if (count == 0)
        {
            item = default!;
            return false;
        }

        int ind = tail;
        Dec(ref ind);
        item = array[ind];

        return true;
    }

    public ref T MutablePeekEnd()
    {
        if (count == 0)
            return ref Unsafe.NullRef<T>();

        int ind = tail;
        Dec(ref ind);
        return ref array[ind];
    }

    private void Grow()
    {
        var old = array;
        var arr = old.Length == 0 ? new T[8] : new T[old.Length << 1];

        if (count > 0)
        {
            int _head = head;
            int _tail = tail;
            if (head < tail)
                old.AsSpan(_head, _tail - _head).CopyTo(arr);
            else
            {
                var first = old.AsSpan(_head);
                first.CopyTo(arr);
                old.AsSpan(0, _tail).CopyTo(arr.AsSpan(first.Length));
            }

            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
                Array.Clear(old);
        }

        array = arr;
        head = 0;
        tail = count;
        version++;
    }

    public void Add(T item) => PushEnd(item);

    public void Clear()
    {
        if (count > 0)
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                if (head < tail)
                    Array.Clear(array, head, count);
                else
                {
                    Array.Clear(array, head, array.Length - head);
                    Array.Clear(array, 0, tail);
                }
            }

            count = 0;
        }

        head = 0;
        tail = 0;
        version++;
    }

    public bool Contains(T item)
    {
        if (count <= 0)
            return false;


        int _head = head;
        int _tail = tail;
        var arr = array;
        if (head < tail)
            return arr.AsSpan(_head, _tail - _head).Contains(item);
        else
        {
            if (arr.AsSpan(_head).Contains(item))
                return true;
            return arr.AsSpan(0, _tail).Contains(item);
        }
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        if (count <= 0)
            return;

        var src = this.array;
        var dst = array.AsSpan(arrayIndex);
        int _head = head;
        int _tail = tail;
        if (head < tail)
            src.AsSpan(_head, _tail - _head).CopyTo(dst);
        else
        {
            var first = src.AsSpan(_head);
            first.CopyTo(dst);
            src.AsSpan(0, _tail).CopyTo(dst[first.Length..]);
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        int _head = head;
        int _tail = tail;
        int _version = version;
        var arr = array;
        if (head < tail)
        {
            var span = arr.AsSegment(_head, _tail - _head);
            foreach (var item in span)
            {
                if (version != _version)
                    throw new InvalidOperationException("Collection changed while enumerating!");
                yield return item;
            }
        }
        else
        {
            var span = arr.AsSegment(_head);
            foreach (var item in span)
            {
                if (version != _version)
                    throw new InvalidOperationException("Collection changed while enumerating!");
                yield return item;
            }

            span = arr.AsSegment(0, _tail);
            foreach (var item in span)
            {
                if (version != _version)
                    throw new InvalidOperationException("Collection changed while enumerating!");
                yield return item;
            }
        }
    }

    public bool Remove(T item) => throw new InvalidOperationException();

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
