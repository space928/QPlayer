using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace QPlayer.Utilities;

/// <summary>
/// A simple iterator that just wraps a single element.
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly struct OneEnumerable<T>(T value) : IEnumerable<T>, IEnumerable, IList<T>, IList, IReadOnlyList<T>, ICollection<T>, ICollection
{
    private readonly T value = value;

    public T this[int index] { get => index == 0 ? value : throw new IndexOutOfRangeException(); set => throw new NotSupportedException(); }
    object? IList.this[int index] { get => index == 0 ? value : throw new IndexOutOfRangeException(); set => throw new NotSupportedException(); }

    public int Count => 1;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => true;
    public object SyncRoot => this;

    public IEnumerator<T> GetEnumerator() => new OneIterator(value);
    IEnumerator IEnumerable.GetEnumerator() => new OneIterator(value);

    public bool Contains(T item) => EqualityComparer<T>.Default.Equals(item, value);

    public bool Contains(object? value) => value?.Equals(this.value) ?? false;

    public void CopyTo(T[] array, int arrayIndex)
    {
        if (arrayIndex < 0 || arrayIndex >= array.Length)
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));

        array[arrayIndex] = value;
    }

    public void CopyTo(Array array, int index)
    {
        if (index < 0 || index >= array.Length)
            throw new ArgumentOutOfRangeException(nameof(index));

        array.SetValue(value, index);
    }

    public int IndexOf(T item) => EqualityComparer<T>.Default.Equals(item, value) ? 0 : -1;
    public int IndexOf(object? value) => (value?.Equals(this.value) ?? false) ? 0 : -1;

    public void Add(T item) => throw new NotSupportedException();
    public int Add(object? value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, T item) => throw new NotSupportedException();
    public void Insert(int index, object? value) => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void Remove(object? value) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();

    internal struct OneIterator(T value) : IEnumerator<T>
    {
        private readonly T value = value;
        private bool done = false;

        public readonly T Current => value;
        readonly object IEnumerator.Current => value!;

        public readonly void Dispose() { }
        public bool MoveNext()
        {
            bool more = !done;
            done = true;
            return more;
        }

        public void Reset() => done = false;
    }
}
