using System;
using System.Collections;
using System.Collections.Generic;
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

        public readonly bool MoveNext() => first.MoveNext() && second.MoveNext();

        public readonly void Reset()
        {
            first.Reset();
            second.Reset();
        }
    }

    internal struct FastArrayZipIterator(TA[] first, TB[] second) : IEnumerator<(TA first, TB second)>
    {
        private readonly TA[] aArr = first;
        private readonly TB[] bArr = second;
        private readonly int count;
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
    }
}
