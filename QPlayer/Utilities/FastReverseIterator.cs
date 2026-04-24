using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace QPlayer.Utilities;

public readonly struct FastReverseEnumerable<T>(IEnumerable<T> source) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => new FastReverseIterator<T>(source);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

public struct FastReverseIterator<T> : IEnumerator<T>
{
    private readonly IList<T>? source;
    private TemporaryList<T> tempList;
    private readonly int len;
    private int pos;

    public readonly T Current => source != null ? source[pos] : tempList[pos];

    readonly object? IEnumerator.Current => Current;

    public FastReverseIterator(IEnumerable<T> source)
    {
        if (source is IList<T> list)
        {
            this.source = list;
            len = pos = list.Count;
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
