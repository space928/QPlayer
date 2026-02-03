using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace QPlayer.Utilities;

/// <summary>
/// A list backed by arrays from the array pool.
/// </summary>
/// <typeparam name="T"></typeparam>
#if NET8_0_OR_GREATER
[CollectionBuilder(typeof(TemporaryListBuilder), nameof(TemporaryListBuilder.Create))]
#endif
public struct TemporaryList<T> : IList<T>, IDisposable
{
    private ArrayPool<T>? arrayPool;
    private T[]? items;
    private int count;
    public int version;

    public readonly T this[int index]
    {
        get
        {
            BoundsCheck(index);
            return items![index];
        }
        set
        {
            BoundsCheck(index);
            items![index] = value;
        }
    }
    public readonly int Count => count;
    public readonly bool IsReadOnly => true;

#if NETSTANDARD
#pragma warning disable CS8618 // Non-nulllable field must contain a non-null value when exiting the constructor.
#endif
    public TemporaryList(int capacity = 0, ArrayPool<T>? arrayPool = null)
    {
        Initialise(capacity, arrayPool);
    }
#if NETSTANDARD
#pragma warning restore CS8618
#endif

    public TemporaryList(ReadOnlySpan<T> items) : this(items.Length, null)
    {
        items.CopyTo(this.items);
    }

    public TemporaryList(IEnumerable<T> items) : this(0, null)
    {
        switch (items)
        {
            case T[] array:
                {
                    EnsureCapacity(array.Length);
                    Array.Copy(array, this.items!, array.Length);
                    break;
                }
            case ICollection<T> collection:
                {
                    EnsureCapacity(collection.Count);
                    foreach (var item in collection)
                        Add(item);
                    break;
                }
            default:
                {
                    foreach (var item in items)
                        Add(item);
                    break;
                }
        }
    }

#if !NETSTANDARD
    [MemberNotNull(nameof(arrayPool), nameof(items))]
#endif
    private void Initialise(int capacity = 0, ArrayPool<T>? arrayPool = null)
    {
        this.arrayPool = arrayPool ?? ArrayPool<T>.Shared;
        items = this.arrayPool.Rent(capacity);
    }

#if !NETSTANDARD
    [MemberNotNull(nameof(items))]
#endif
    public void EnsureCapacity(int capacity)
    {
        if (items == null)
            Initialise(capacity, null);

        if (capacity <= items!.Length)
            return;

#if !NETSTANDARD
        capacity = (int)BitOperations.RoundUpToPowerOf2((uint)capacity);
#else
        capacity = (int)PolyFill.RoundUpToPowerOf2((uint)capacity);
#endif
        var newArr = arrayPool!.Rent(capacity); //new T[capacity];
        Array.Copy(items, newArr, count);
        var old = items;
        items = newArr;

#if !NETSTANDARD
        arrayPool.Return(old, RuntimeHelpers.IsReferenceOrContainsReferences<T>());
#else
        arrayPool.Return(old, true);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private readonly void BoundsCheck(int index)
    {
        if (index < 0 || index >= count)
            throw new ArgumentOutOfRangeException(nameof(index));
    }

    /// <summary>
    /// Gets a span of the elements in this temporary list, valid as long as the list count isn't changed.
    /// </summary>
    /// <returns></returns>
    public readonly Span<T> AsSpan() => items == null ? [] : items.AsSpan(0, count);

    public void Add(T item)
    {
        EnsureCapacity(count + 1);
        items![count++] = item;
        version++;
    }

    /// <summary>
    /// Adds each element from the enumerable to the list efficiently.
    /// </summary>
    /// <param name="items"></param>
    public void AddRange(IEnumerable<T> items)
    {
        switch (items)
        {
            case T[] array:
                EnsureCapacity(count + array.Length);
                Array.Copy(array, 0, this.items, count, array.Length);
                count += array.Length;
                version++;
                break;
            case ICollection<T> collection:
                EnsureCapacity(collection.Count);
                foreach (var item in collection)
                    Add(item);
                break;
            default:
                foreach (var item in items)
                    Add(item);
                break;
        }
    }

    public void Clear()
    {
        count = 0;
        if (items != null)
        {
#if !NETSTANDARD
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
#endif
            {
                // Clear any reference types so that the GC can reclaim them
                Array.Clear(items, 0, count);
            }
        }
        version++;
    }

    public readonly bool Contains(T item) => Array.IndexOf(items!, item, 0, count) != -1;

    public readonly void CopyTo(T[] array, int arrayIndex) => Array.Copy(items ?? [], 0, array, arrayIndex, count);

    public void Dispose()
    {
        version++;
        count = 0;
        if (items != null)
        {
#if !NETSTANDARD
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
#endif
            {
                // Clear any reference types so that the GC can reclaim them
                Array.Clear(items, 0, count);
            }
            arrayPool?.Return(items, false);
            items = null;
        }
    }

    public readonly IEnumerator<T> GetEnumerator()
    {
        int startVersion = version;
        for (int i = 0; i < count; i++)
        {
            if (version != startVersion)
                throw new InvalidOperationException("List has been mutated since iteration began!");
            yield return items![i];
        }
    }

    public readonly int IndexOf(T item) => Array.IndexOf(items!, item, 0, count);

    public void Insert(int index, T item)
    {
        if (index < 0 || index > count)
            throw new ArgumentOutOfRangeException(nameof(index));
        if (index == count)
        {
            Add(item);
            return;
        }
        EnsureCapacity(count + 1);
        Array.Copy(items, index, items, index + 1, count - index);
        items![index] = item;
        count++;
        version++;
    }

    public bool Remove(T item)
    {
        int ind = IndexOf(item);
        if (ind == -1)
            return false;
        RemoveAt(ind);
        return true;
    }

    public void RemoveAt(int index)
    {
        if (index == count - 1)
        {
            count--;
            version++;
            return;
        }

        Array.Copy(items!, index + 1, items!, index, count - index - 1);

        count--;
        version++;
    }

    readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// Factory class used by the collection builder to correctly initialise a <see cref="TemporaryList{T}"/>.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class TemporaryListBuilder
{
    /// <summary>
    /// Factory method used by the collection builder to correctly initialise a <see cref="TemporaryList{T}"/>.
    /// </summary>
    public static TemporaryList<T> Create<T>(ReadOnlySpan<T> values) => new(values);
}
