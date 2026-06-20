using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
