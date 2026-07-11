using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace QPlayer.Utilities;

public readonly struct FastReverseEnumerable<T>(IEnumerable<T> source) : ICollection<T>, IReadOnlyCollection<T>
{
    /// <summary>
    /// Accessing this member may require enumerating the source.
    /// </summary>
    public int Count => source.Count();
    public bool IsReadOnly => true;

    public IEnumerator<T> GetEnumerator() => new FastReverseIterator<T>(source);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains(T item) => throw new InvalidOperationException();
    public void CopyTo(T[] array, int arrayIndex)
    {
        int count = Count; // This risks enumerating the collection twice...
        if (arrayIndex + count > array.Length)
            throw new ArgumentOutOfRangeException(nameof(array));

        var iter = source.GetEnumerator();
        for (int i = arrayIndex + count - 1; i >= arrayIndex; i--)
        {
            if (!iter.MoveNext())
                break;
            array[i] = iter.Current;
        }
    }

    public void Add(T item) => throw new InvalidOperationException();
    public void Clear() => throw new InvalidOperationException();
    public bool Remove(T item) => throw new InvalidOperationException();
}

public struct FastReverseIterator<T> : IEnumerator<T>
{
    private readonly IReadOnlyList<T>? source;
    private TemporaryList<T> tempList;
    private readonly int len;
    private int pos;

    public readonly T Current => source != null ? source[pos] : tempList[pos];

    readonly object? IEnumerator.Current => Current;

    public readonly int Count => source?.Count ?? 0;

    public FastReverseIterator(IEnumerable<T> source)
    {
        /*if (source is IList<T> list)
        {
            this.source = list;
        }*/
        if (source is IReadOnlyList<T> listRO)
        {
            this.source = listRO;
            len = pos = listRO.Count;
        }
        else
        {
            tempList = new(source);
            len = pos = tempList.Count;
        }
    }

    public void Dispose()
    {
        tempList.Dispose();
    }

    public bool MoveNext()
    {
        pos--;
        return pos >= 0;
    }

    public void Reset()
    {
        pos = len;
    }
}

/// <summary>
/// This is effecitvely a wrapper for a list which reverses the items within it without re-ordering the data.
/// </summary>
/// <typeparam name="T"></typeparam>
/// <param name="src"></param>
public readonly struct FastReverseList<T>(IList<T> src) : IGeneralList<T>, IList
{
    public readonly T this[int index] { get => src[ConvertIndex(index)]; set => src[ConvertIndex(index)] = value; }
    readonly object? IList.this[int index] { get => src[ConvertIndex(index)]; set => src[ConvertIndex(index)] = (T)value!; }
    public readonly int Count => src.Count;
    public readonly bool IsReadOnly => src.IsReadOnly;
    public readonly bool IsFixedSize => true;
    public readonly bool IsSynchronized => false;
    public readonly object SyncRoot => new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly int ConvertIndex(int index) => Count - index - 1;

    public readonly int Add(object? value)
    {
        if (value is T item)
        {
            src.Insert(0, item);
            return Count - 1;
        }
        return -1;
    }
    public readonly void CopyTo(T[] array, int arrayIndex)
    {
        src.CopyTo(array, arrayIndex);
        Array.Reverse(array, arrayIndex, Count);
    }
    public readonly void CopyTo(Array array, int index)
    {
        src.CopyTo((T[])array, index);
        Array.Reverse(array, index, Count);
    }
    public readonly int IndexOf(T item)
    {
        int ind = src.IndexOf(item);
        if (ind < 0)
            return ind;

        return ConvertIndex(ind);
    }

    public readonly void Insert(int index, T item) => src.Insert(ConvertIndex(index), item);
    public readonly void RemoveAt(int index) => src.RemoveAt(ConvertIndex(index));

    public readonly void Add(T item) => src.Insert(0, item);
    public readonly void Clear() => src.Clear();
    public readonly bool Contains(T item) => src.Contains(item);
    public readonly bool Contains(object? value) => value is T item && src.Contains(item);
    public readonly IEnumerator<T> GetEnumerator() => new FastReverseListIterator(src);
    public readonly int IndexOf(object? value) => value is T item ? IndexOf(item) : -1;
    public readonly void Insert(int index, object? value) => Insert(index, (T)value!);
    public readonly bool Remove(T item) => src.Remove(item);
    public readonly void Remove(object? value) => src.Remove((T)value!);
    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct FastReverseListIterator : IEnumerator<T>
    {
        private readonly IList<T> source;
        private readonly int len;
        private int pos;

        public readonly T Current => source[pos];

        readonly object? IEnumerator.Current => Current;

        public readonly int Count => source?.Count ?? 0;

        public FastReverseListIterator(IList<T> source)
        {
            this.source = source;
            len = pos = source.Count;
        }

        public readonly void Dispose() { }

        public bool MoveNext()
        {
            pos--;
            return pos >= 0;
        }

        public void Reset()
        {
            pos = len;
        }
    }
}
