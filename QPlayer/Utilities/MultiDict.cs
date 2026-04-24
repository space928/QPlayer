using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

namespace QPlayer.Utilities;

/// <summary>
/// A dictionary which can store multiple values at a given key.
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public class MultiDict<TKey, TValue> : IDictionary<TKey, MultiDict<TKey, TValue>.ValueCollection>
    where TKey : notnull, IEquatable<TKey>
    where TValue : class
{
    private readonly Dictionary<TKey, ValueCollection> dict;

    public ValueCollection this[TKey key]
    {
        get => dict[key];
        set => dict[key] = value;
    }

    public ICollection<TKey> Keys => dict.Keys;
    public ICollection<ValueCollection> Values => dict.Values;
    public int Count => dict.Count;
    public bool IsReadOnly => false;

    public MultiDict()
    {
        dict = [];
    }

    public MultiDict(int capacity)
    {
        dict = new(capacity);
    }

    public MultiDict(IEnumerable<KeyValuePair<TKey, ValueCollection>> items)
    {
        dict = new(items);
    }

    public void Add(TKey key, TValue value)
    {
        if (dict.TryAdd(key, new(value)))
            return;

        ref var entry = ref Accessors<TKey, ValueCollection>.FindValue(dict, key);
        entry.Add(value);
    }
    public void Add(TKey key, ICollection<TValue> value) => dict.Add(key, new(value));
    public void Add(TKey key, ValueCollection value) => dict.Add(key, value);
    public void Add(KeyValuePair<TKey, ICollection<TValue>> item) => dict.Add(item.Key, new(item.Value));
    public void Add(KeyValuePair<TKey, ValueCollection> item) => dict.Add(item.Key, item.Value);
    public bool TryAdd(TKey key, ValueCollection value) => dict.TryAdd(key, value);
    public bool TryAdd(TKey key, TValue value)
    {
        Add(key, value);
        return true;
    }

    public void Clear() => dict.Clear();

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue? value)
    {
        if (dict.TryGetValue(key, out var items))
        {
            value = items;
            return true;
        }
        value = default;
        return false;
    }
    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out ValueCollection value) => dict.TryGetValue(key, out value);
    public bool Contains(KeyValuePair<TKey, ValueCollection> item) => dict.Contains(item);
    public bool ContainsKey(TKey key) => dict.ContainsKey(key);

    public bool Remove(TKey key, TValue value)
    {
        ref var entry = ref Accessors<TKey, ValueCollection>.FindValue(dict, key);
        if (Unsafe.IsNullRef(ref entry))
            return false;

        if (entry.Count > 1)
            return entry.Remove(value);
        // Annoyingly in this case we pay the cost of finding the bucket twice...
        return dict.Remove(key);
    }
    public bool Remove(TKey key) => dict.Remove(key);
    public bool Remove(KeyValuePair<TKey, ValueCollection> item) => dict.Remove(item.Key);

    /// <summary>
    /// Updates the key of the given item in the dictionary.
    /// </summary>
    /// <param name="oldKey">The original key of the item.</param>
    /// <param name="newKey">The new key of the item.</param>
    /// <param name="value">The value to be updated.</param>
    /// <returns><see langword="true"/> if the value with the given key was found and updated.</returns>
    public bool UpdateKey(TKey oldKey, TKey newKey, TValue value)
    {
        ref var entry = ref Accessors<TKey, ValueCollection>.FindValue(dict, oldKey);
        if (Unsafe.IsNullRef(ref entry))
            return false;

        if (!entry.Remove(value))
            return false;

        Add(newKey, value);
        return true;
    }

    public IEnumerator<KeyValuePair<TKey, ValueCollection>> GetEnumerator() => dict.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => dict.GetEnumerator();

    public void CopyTo(KeyValuePair<TKey, ValueCollection>[] array, int arrayIndex)
    {
        ((ICollection<KeyValuePair<TKey, ValueCollection>>)dict).CopyTo(array, arrayIndex);
    }

    public struct ValueCollection : ICollection<TValue>, IEquatable<ValueCollection>
    {
        private TValue? first;
        private List<TValue>? values;

        public readonly int Count => first == null ? (values?.Count ?? 0) : 1;
        public readonly bool IsReadOnly => false;
        public readonly bool IsUnique => Count == 1;

        public ValueCollection(ValueCollection other)
        {
            this.first = other.first;
            this.values = other.values;
        }

        public ValueCollection(TValue val)
        {
            this.first = val;
            this.values = null;
        }

        public ValueCollection(List<TValue> values)
        {
            this.values = values;
        }

        public ValueCollection(IEnumerable<TValue> values)
        {
            foreach (var v in values)
                Add(v);
        }

        public void Replace(TValue item)
        {
            values = null;
            first = item;
        }

        public void Add(TValue item)
        {
            if (values != null)
            {
                values.Add(item);
                return;
            }

            if (first == null)
            {
                first = item;
                return;
            }
            else
            {
                values = [first, item];
                first = default!;
            }
        }

        public void Clear()
        {
            values?.Clear();
            first = default!;
        }

        public readonly bool Contains(TValue item) => values?.Contains(item) ?? item.Equals(first);

        public readonly void CopyTo(TValue[] array, int arrayIndex)
        {
            switch (Count)
            {
                case 0: return;
                case 1: array[arrayIndex] = first!; return;
                default: values!.CopyTo(array, arrayIndex); return;
            }
        }


        public bool Remove(TValue item)
        {
            switch (Count)
            {
                case 0:
                    return false;
                case 1:
                    if (item.Equals(first))
                    {
                        first = default!;
                        return true;
                    }
                    return false;
                default:
                    if (values!.Remove(item))
                    {
                        if (values!.Count == 1)
                        {
                            first = values[0];
                            values = null;
                        }
                        return true;
                    }
                    return false;
            }
        }

        public readonly IEnumerator<TValue> GetEnumerator()
        {
            switch (Count)
            {
                case 0:
                    break;
                case 1:
                    yield return first!;
                    break;
                default:
                    foreach (var val in values!)
                        yield return val;
                    break;
            }
            yield break;
        }

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly bool Equals(ValueCollection other)
        {
            return ReferenceEquals(other.first, first) && ReferenceEquals(other.values, values);
        }

        public readonly override bool Equals([NotNullWhen(true)] object? obj)
        {
            if (obj is ValueCollection other)
                return Equals(other);
            return false;
        }

        public readonly override int GetHashCode()
        {
            return Count switch
            {
                0 => 0,
                1 => first!.GetHashCode(),
                _ => values!.GetHashCode(),
            };
        }

        public override readonly string? ToString()
        {
            return Count switch
            {
                0 => string.Empty,
                1 => first!.ToString(),
                _ => values!.ToString(),
            };
        }

        public static bool operator ==(ValueCollection left, ValueCollection right) => left.Equals(right);

        public static bool operator !=(ValueCollection left, ValueCollection right) => !(left == right);

        public static implicit operator TValue?(ValueCollection item) => item.Count switch
        {
            0 => null,
            1 => item.first,
            _ => item.values![0],
        };
    }
}

internal static partial class Accessors<TKey_, TValue_>
        where TKey_ : notnull
{
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "FindValue")]
    extern public static ref TValue_ FindValue(Dictionary<TKey_, TValue_> dict, TKey_ key);
}
