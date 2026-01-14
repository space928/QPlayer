using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace QPlayer.SourceGenerator;

/// <summary>
/// An immutable array with structural equality comparison semantics.
/// </summary>
/// <typeparam name="T"></typeparam>
public readonly partial struct EquatableArray<T> : IReadOnlyList<T>, IList<T>, IEquatable<EquatableArray<T>>, IList, IStructuralEquatable
{
    private readonly T[] elements;

    public T this[int index] { get => elements[index]; set => throw new NotSupportedException(); }
    object IList.this[int index] { get => elements[index]!; set => throw new NotSupportedException(); }

    public int Count => elements.Length;
    public int Length => elements.Length;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => true;
    public object SyncRoot => this;

    public EquatableArray(ImmutableArray<T> elements)
    {
        this.elements = ImmutableCollectionsMarshal.AsArray(elements)!;
    }

    public EquatableArray(IEnumerable<T> elements)
    {
        if (elements is T[] arr)
            this.elements = arr;
        else
            this.elements = elements.ToArray();
    }

    public ImmutableArray<T> AsImmutableArray()
    {
        return ImmutableCollectionsMarshal.AsImmutableArray(elements);
    }

    public ref readonly T GetItemRef(int index) => ref elements[index];

    public bool Contains(T item) => elements.Contains(item);
    public bool Contains(object value) => elements.Contains((T)value);

    public void CopyTo(T[] array, int arrayIndex) => elements.CopyTo(array, arrayIndex);
    public void CopyTo(Array array, int index) => elements.CopyTo(array, index);

    public override bool Equals(object obj)
    {
        if (obj is EquatableArray<T> arr)
            return Equals(arr);
        return false;
    }

    public override int GetHashCode()
    {
        HashCode hash = new();
        foreach (var element in elements)
            hash.Add(element);
        return hash.ToHashCode();
    }

    public bool Equals(EquatableArray<T> other)
    {
        return elements.SequenceEqual(other.elements);
    }

    public bool Equals(EquatableArray<T> other, IEqualityComparer<T> comparer)
    {
        return elements.SequenceEqual(other.elements, comparer);
    }

    public bool Equals(object other, IEqualityComparer comparer)
    {
        if (other is EquatableArray<T> arr)
            return Equals(arr, comparer);
        else if (other is IEnumerable<T> enm)
            return elements.SequenceEqual(enm, (IEqualityComparer<T>)comparer);

        return false;
    }

    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < elements.Length; i++)
            yield return elements[i];
    }

    public int GetHashCode(IEqualityComparer comparer)
    {
        HashCode hash = default;
        foreach (var element in elements)
            hash.Add(element);
        return hash.ToHashCode();
    }

    public int IndexOf(T item) => Array.IndexOf(elements, item);
    public int IndexOf(object value) => Array.IndexOf(elements, value);
    public int IndexOf(T item, int index, int count, IEqualityComparer<T>? equalityComparer) => Array.IndexOf(elements, item, index, count);
    public int LastIndexOf(T item, int index, int count, IEqualityComparer<T>? equalityComparer) => Array.LastIndexOf(elements, item, index, count);
    IEnumerator IEnumerable.GetEnumerator() => elements.GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right)
    {
        return !left.Equals(right);
    }

    #region Unsupported Operations

    public void Add(T item) => throw new NotSupportedException();

    public int Add(object value) => throw new NotSupportedException();
    public IImmutableList<T> AddRange(IEnumerable<T> items) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, T item) => throw new NotSupportedException();
    public void Insert(int index, object value) => throw new NotSupportedException();
    public IImmutableList<T> InsertRange(int index, IEnumerable<T> items) => throw new NotSupportedException();
    public bool Remove(T item) => throw new NotSupportedException();
    public void Remove(object value) => throw new NotSupportedException();
    public IImmutableList<T> Remove(T value, IEqualityComparer<T>? equalityComparer) => throw new NotSupportedException();
    public IImmutableList<T> RemoveAll(Predicate<T> match) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
    #endregion
}
