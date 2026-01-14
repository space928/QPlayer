using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace QPlayer.SourceGenerator;

/// <summary>
/// An immutable dictionary with value equality semantics.
/// </summary>
/// <typeparam name="TKey"></typeparam>
/// <typeparam name="TValue"></typeparam>
public readonly struct EquatableDictionary<TKey, TValue> : IDictionary, IReadOnlyDictionary<TKey, TValue>, IDictionary<TKey, TValue>,
    IEquatable<EquatableDictionary<TKey, TValue>>, IStructuralEquatable
    where TKey : notnull
{
    private readonly FrozenDictionary<TKey, TValue> dict;

    public TValue this[TKey key] { get => dict[key]; set => throw new NotSupportedException(); }
    public object this[object key] { get => dict[(TKey)key]!; set => throw new NotSupportedException(); }

    public ICollection<TKey> Keys => dict.Keys;
    public ICollection<TValue> Values => dict.Values;
    public int Count => dict.Count;
    public bool IsReadOnly => true;
    public bool IsFixedSize => true;
    public bool IsSynchronized => true;
    public object SyncRoot => this;
    ICollection IDictionary.Keys => dict.Keys;
    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => dict.Keys;
    ICollection IDictionary.Values => dict.Values;
    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => dict.Values;

    public EquatableDictionary() 
    {
        dict = FrozenDictionary<TKey, TValue>.Empty;
    }

    public EquatableDictionary(IEnumerable<KeyValuePair<TKey, TValue>> keyValues)
    {
        var tmp = new Dictionary<TKey, TValue>();
        foreach (var kvp in keyValues)
            tmp.TryAdd(kvp.Key, kvp.Value);
        dict = tmp.ToFrozenDictionary();
    }

    public EquatableDictionary(IDictionary<TKey, TValue> keyValues)
    {
        dict = keyValues.ToFrozenDictionary();
    }

    public bool Equals(EquatableDictionary<TKey, TValue> other)
    {
        return dict.SequenceEqual(other.dict);
        //dict.Keys.SequenceEqual(other.dict.Keys);
        //dict.Values.SequenceEqual(other.dict.Values);
    }

    public bool Equals(EquatableDictionary<TKey, TValue> other, IEqualityComparer comparer)
    {
        return dict.SequenceEqual(other.dict, (IEqualityComparer<KeyValuePair<TKey, TValue>>)comparer);
        //dict.Keys.SequenceEqual(other.dict.Keys);
        //dict.Values.SequenceEqual(other.dict.Values);
    }

    public bool Equals(object other, IEqualityComparer comparer)
    {
        if (other is EquatableDictionary<TKey, TValue> d)
            return Equals(d, comparer);
        else if (other is IEnumerable<KeyValuePair<TKey, TValue>> enm)
            return dict.SequenceEqual(enm, (IEqualityComparer<KeyValuePair<TKey, TValue>>)comparer);

        return false;
    }

    public int GetHashCode(IEqualityComparer comparer)
    {
        HashCode hash = default;
        foreach (var element in dict)
        {
            hash.Add(element.Key);
            hash.Add(element.Value);
        }
        return hash.ToHashCode();
    }

    public override bool Equals(object obj)
    {
        if (obj is EquatableDictionary<TKey, TValue> d)
            return Equals(d);
        else if (obj is IEnumerable<KeyValuePair<TKey, TValue>> enm)
            return dict.SequenceEqual(enm);

        return false;
    }

    public override int GetHashCode() => GetHashCode(EqualityComparer<KeyValuePair<TKey, TValue>>.Default);

    public static bool operator ==(EquatableDictionary<TKey, TValue> left, EquatableDictionary<TKey, TValue> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(EquatableDictionary<TKey, TValue> left, EquatableDictionary<TKey, TValue> right)
    {
        return !left.Equals(right);
    }

    public bool Contains(KeyValuePair<TKey, TValue> item)
    {
        if (dict.TryGetValue(item.Key, out var value))
            return (value == null && item.Value == null) || (value?.Equals(item.Value) ?? false);
        return false;
    }

    public bool Contains(object key) => dict.ContainsKey((TKey)key);
    public bool ContainsKey(TKey key) => dict.ContainsKey(key);
    public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) => dict.CopyTo(array, arrayIndex);
    public void CopyTo(Array array, int index) => dict.CopyTo((KeyValuePair<TKey, TValue>[])array, index);
    public bool TryGetValue(TKey key, out TValue value) => dict.TryGetValue(key, out value!);

    IEnumerator IEnumerable.GetEnumerator() => dict.GetEnumerator();
    IDictionaryEnumerator IDictionary.GetEnumerator() => new DictEnumerator(dict.GetEnumerator());
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => dict.GetEnumerator();

    public void Add(TKey key, TValue value) => throw new NotSupportedException();
    public void Add(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();
    public void Add(object key, object value) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public bool Remove(TKey key) => throw new NotSupportedException();
    public bool Remove(KeyValuePair<TKey, TValue> item) => throw new NotSupportedException();
    public void Remove(object key) => throw new NotSupportedException();

    private struct DictEnumerator(IEnumerator<KeyValuePair<TKey, TValue>> enumerator) : IDictionaryEnumerator
    {
        public readonly DictionaryEntry Entry
        {
            get
            {
                var curr = enumerator.Current;
                return new(curr.Key, curr.Value);
            }
        }

        public readonly object Key => enumerator.Current.Key!;
        public readonly object Value => enumerator.Current.Value!;
        public readonly object Current => enumerator.Current;

        public readonly bool MoveNext() => enumerator.MoveNext();
        public readonly void Reset() => enumerator.Reset();
    }
}
