using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;

namespace QPlayer.Utilities;

/// <summary>
/// A very simple enumerable that implements <see cref="System.Linq.Enumerable.Zip{TFirst, TSecond}(IEnumerable{TFirst}, IEnumerable{TSecond})"/>
/// without any allocations.
/// </summary>
/// <typeparam name="TA"></typeparam>
/// <typeparam name="TB"></typeparam>
/// <param name="first"></param>
/// <param name="second"></param>
internal readonly struct FastZipEnumerable<TA, TB>(IEnumerable<TA> first, IEnumerable<TB> second) : IEnumerable<(TA first, TB second)>
{
    public IEnumerator<(TA first, TB second)> GetEnumerator() => new FastZipIterator(first, second);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal readonly struct FastZipIterator(IEnumerable<TA> first, IEnumerable<TB> second) : IEnumerator<(TA first, TB second)>
    {
        private readonly IEnumerator<TA> first = first.GetEnumerator();
        private readonly IEnumerator<TB> second = second.GetEnumerator();

        public readonly (TA first, TB second) Current => (first.Current, second.Current);

        readonly object IEnumerator.Current => Current;

        public readonly void Dispose()
        {
            first.Dispose();
            second.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool MoveNext() => first.MoveNext() && second.MoveNext();

        public readonly void Reset()
        {
            first.Reset();
            second.Reset();
        }
    }
}

/// <summary>
/// A very simple enumerable that implements <see cref="System.Linq.Enumerable.Zip{TFirst, TSecond}(IEnumerable{TFirst}, IEnumerable{TSecond})"/>
/// without any allocations.
/// </summary>
/// <typeparam name="TA"></typeparam>
/// <typeparam name="TB"></typeparam>
/// <param name="first"></param>
/// <param name="second"></param>
internal readonly struct FastZipList<TA, TB>(IReadOnlyList<TA> first, IReadOnlyList<TB> second) : IReadOnlyList<(TA first, TB second)>, IList<(TA first, TB second)>
{
    public (TA first, TB second) this[int index]
    {
        get => (first[index], second[index]);
        set => throw new InvalidOperationException();
    }

    public int Count => Math.Min(first.Count, second.Count);
    public bool IsReadOnly => true;

    public IEnumerator<(TA first, TB second)> GetEnumerator() => new FastZipIterator(first, second);
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public bool Contains((TA first, TB second) item) => first.Contains(item.first) && second.Contains(item.second);

    public void CopyTo((TA first, TB second)[] array, int arrayIndex)
    {
        int count = Count;
        for (int i = 0; i < count; i++)
            array[arrayIndex] = (first[i], second[i]);
    }

    public void Add((TA first, TB second) item) => throw new InvalidOperationException();
    public void Clear() => throw new InvalidOperationException();
    public int IndexOf((TA first, TB second) item) => throw new InvalidOperationException();
    public void Insert(int index, (TA first, TB second) item) => throw new InvalidOperationException();
    public bool Remove((TA first, TB second) item) => throw new InvalidOperationException();
    public void RemoveAt(int index) => throw new InvalidOperationException();


    internal struct FastZipIterator(IReadOnlyList<TA> first, IReadOnlyList<TB> second) : IEnumerator<(TA first, TB second)>
    {
        private readonly IReadOnlyList<TA> first = first;
        private readonly IReadOnlyList<TB> second = second;
        private readonly int count = Math.Min(first.Count, second.Count);
        private int pos = -1;

        public readonly (TA first, TB second) Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>(first[pos], second[pos]);
        }

        readonly object IEnumerator.Current => Current;

        public readonly void Dispose() { }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MoveNext()
        {
            pos++;
            return pos < count;
        }

        public void Reset()
        {
            pos = -1;
        }
    }

    /*internal struct FastArrayZipIterator(TA[] first, TB[] second) : IEnumerator<(TA first, TB second)>
    {
        private readonly TA[] aArr = first;
        private readonly TB[] bArr = second;
        private readonly int count = Math.Min(first.Length, second.Length);
        private nint pos = -1;

        public readonly (TA first, TB second) Current
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (aArr[pos], bArr[pos]);
        }

        readonly object IEnumerator.Current => Current;

        public readonly void Dispose() { }

        public bool MoveNext()
        {
            if (pos >= count)
                return false;
            pos++;
            return true;
        }

        public void Reset()
        {
            pos = -1;
        }
    }*/
}
