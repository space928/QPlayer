using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#if DEBUG
using System.Text;
#endif

namespace Mathieson.Dev;

/*
 *   A fast string-keyed dictionary. 
 *   
 * The MIT License (MIT)
 * 
 * Copyright (c) 2024-2025 Thomas Mathieson
 *  
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 * 
 * The above copyright notice and this permission notice shall be included in all
 * copies or substantial portions of the Software.
 * 
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
 * SOFTWARE.
 * 
 */

/*

### BENCHMARKS
We tested add, remove, get, and set methods for the built in Dictionary<string, int> and StringDict<int>.

- Method Descriptions:
intToStr is an array of strings in the form: ["0", "1", "2", "3", "4", ...]

BuiltInDict() / StringDict()
{
    intDict.Clear();
    for (int i = 0; i < TestSize; i++)
        intDict.Add(intToStr[i], i);

    for (int i = TestSize/3; i < TestSize; i++)
        intDict.Remove(intToStr[i]);
}

BuiltInDictAccess() / StringDictAccess()
{
    for (int i = 0; i < TestSize; i++)
        intDict[intToStr[i]]++;
}

BuiltInDictAccessRT() / StringDictAccessRT()
{
    Span<char> key = stackalloc char[16];

    for (int i = 0; i < TestSize; i++)
    {
        i.TryFormat(key, out int written);
        var k = key[..written];
        #if STRING_DICT
        intDict[k]++; // String dict can be indexed by ReadOnlySpan<char> directly
        #else
        intDict[k.ToString()]++;
        #endif
    }
}

- Results:

BenchmarkDotNet v0.14.0, Windows 11 (10.0.22631.3880/23H2/2023Update/SunValley3)
Intel Core i5-8300H CPU 2.30GHz (Coffee Lake), 1 CPU, 8 logical and 4 physical cores
.NET SDK 8.0.303
  [Host]     : .NET 8.0.7 (8.0.724.31311), X64 RyuJIT AVX2
  Job-GYPGUD : .NET 8.0.7 (8.0.724.31311), X64 RyuJIT AVX2

Runtime=.NET 8.0  IterationCount=10  WarmupCount=4  

| Method              | Mean      | Error     | StdDev    | Gen0      | LLCMisses/Op | Allocated |
|-------------------- |----------:|----------:|----------:|----------:|-------------:|----------:|
| BuiltInDict         | 11.204 ms | 1.0543 ms | 0.6973 ms |         - |      855,552 |       1 B | => Adding and removing elements
| StringDict          |  9.075 ms | 0.8741 ms | 0.4572 ms |         - |      654,950 |       1 B |
| BuiltInDictAccess   | 10.228 ms | 0.6638 ms | 0.3950 ms |         - |      546,202 |       6 B | => Incrementing the value of elements (get, increment, set)
| StringDictAccess    |  5.162 ms | 0.5082 ms | 0.3361 ms |         - |      295,219 |       3 B |
| BuiltInDictAccessRT | 17.273 ms | 1.1698 ms | 0.6961 ms | 1718.7500 |      818,176 | 7199932 B | => Incrementing the value of elements accessed by ReadOnlySpan<char> keys (or new string(...) for built in dict)
| StringDictAccessRT  |  5.681 ms | 0.3556 ms | 0.2352 ms |         - |       51,610 |       3 B |

 */

/// <summary>
/// A fast string-keyed dictionary. This is very similar in implementation to the .NET generic dictionary, 
/// but it has a few specific optimisations which can be useful in some cases. Notably, it can by accessed
/// by string key or by <see cref="ReadOnlySpan{T}"/> of <see langword="char"/> which can save unnecessary 
/// string allocations. It also exposes methods to get values by reference which can save performance. 
/// And it uses a slightly faster string hashing algorithm.
/// </summary>
/// <typeparam name="T">The type of values stored in this dictionary.</typeparam>
#if DEBUG
[DebuggerTypeProxy(typeof(StringDict<>.DebugVisualiser))]
#endif
//[DebuggerBrowsable(DebuggerBrowsableState.Collapsed)]
[DebuggerDisplay("Count = {Count}")]
public class StringDict<T> : IDictionary<string, T>, IDictionary, IReadOnlyDictionary<string, T>, ICollection<KeyValuePair<string, T>>
{
    /// <summary>
    /// Stores references to dictionary items, index by key hashcode. Stores a 1-based index into the other arrays.
    /// </summary>
    private int[] buckets;
    /// <summary>
    /// Stores the complete hashcode of an item pointed to by buckets.
    /// </summary>
    private int[] hashcodes;
    /// <summary>
    /// Stores the index of the next item in the same bucket as the current item pointed to by buckets. 1-based index!
    /// </summary>
    private int[] links;
    /// <summary>
    /// Stores the key of an item pointed to by buckets.
    /// </summary>
    private string?[] keys;
    /// <summary>
    /// Stores the value of an item pointed to by buckets.
    /// </summary>
    private T[] values;

    //private int maxListLength;
    //private int capacity;
    private int lastSlot;
    private int freeCount;
    private int freeIndex;
    private int lastFreeIndex;
    private int version;

    /*private int chainsAboveThresh;

    private int NChainResizeThreshold => Count >> NResizeRate;
    private const int NResizeRate = 8;
    private const int SuggestedChainResizeThreshold = 5;
    private const int MaxChainResizeThreshold = 10;*/

    private const int ChainResizeCheckThresh = 5;
    private /*const*/ float targetLoadFactor = 3f;
    public float TargetLoadFactor
    {
        get => targetLoadFactor;
        set => targetLoadFactor = value;
    }

    public StringDict() : this(0) { }

#pragma warning disable CS8618 // Non-nullable fields are initialised by Initialise();
    public StringDict(int capacity)
#pragma warning restore CS8618
    {
        Initialise(capacity);
    }

    public StringDict(ICollection<KeyValuePair<string, T>> keyValuePairs)
    {
        Initialise(keyValuePairs.Count);

        throw new NotImplementedException();
    }

    public StringDict(IEnumerable<KeyValuePair<string, T>> keyValuePairs)
    {
        throw new NotImplementedException();
    }

    public StringDict(IDictionary<string, T> other) : this(other as ICollection<KeyValuePair<string, T>>) { }

#if DEBUG
    internal int DBG_CountLinks()
    {
        int nlinks = 0;
        for (int b = 0; b < buckets.Length; b++)
        {
            int bucket = buckets[b];
            if (bucket == 0)
                continue;
            int i = links[bucket - 1] - 1;
            while (i >= 0)
            {
                nlinks++;
                i = links[i] - 1;
            }
        }
        return nlinks;
    }

    internal int[] DBG_LinksHistogram()
    {
        int[] dst = new int[64];
        for (int b = 0; b < buckets.Length; b++)
        {
            int bucket = buckets[b];
            if (bucket == 0)
                continue;
            int nlinks = 0;
            int i = links[bucket - 1] - 1;
            while (i >= 0)
            {
                nlinks++;
                i = links[i] - 1;
            }
            dst[Math.Min(nlinks, dst.Length - 1)]++;
        }
        return dst;
    }
#endif

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(string key)
    {
        int ind = FindKey(key);
        if (ind == -1)
            throw new KeyNotFoundException();
        return ref values[ind];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(ReadOnlySpan<char> key)
    {
        int ind = FindKey(key);
        if (ind == -1)
            throw new KeyNotFoundException();
        return ref values[ind];
    }

    public ref T this[ReadOnlySpan<char> key] => ref GetRef(key);

    public T this[string key]
    {
        get => GetRef(key);
        set => GetRef(key) = value;
    }

    public object? this[object key]
    {
        get => this[(string)key];
        set => this[(string)key] = (T)value!;
    }

    public int Capacity => buckets.Length;
    public int Count => lastSlot - freeCount;
    internal float LoadFactor => Count / (float)Capacity;
    public bool IsReadOnly => false;
    public bool IsFixedSize => false;
    public bool IsSynchronized => false;
    public object SyncRoot => this;

    ICollection IDictionary.Keys => new KeyCollection(this);
    IEnumerable<string> IReadOnlyDictionary<string, T>.Keys => new KeyCollection(this);
    public ICollection<string> Keys => new KeyCollection(this);
    ICollection IDictionary.Values => new ValueCollection(this);
    IEnumerable<T> IReadOnlyDictionary<string, T>.Values => new ValueCollection(this);
    public ICollection<T> Values => new ValueCollection(this);

#if !NETSTANDARD
    [MemberNotNull(nameof(buckets))]
    [MemberNotNull(nameof(hashcodes))]
    [MemberNotNull(nameof(links))]
    [MemberNotNull(nameof(keys))]
    [MemberNotNull(nameof(values))]
#endif
    private void Initialise(int capacity)
    {
#if !NETSTANDARD
        capacity = Math.Max((int)BitOperations.RoundUpToPowerOf2((uint)capacity) - 1, 0);
#else
        capacity = Math.Max((int)PolyFill.RoundUpToPowerOf2((uint)capacity) - 1, 0);
#endif

        version++;

        buckets = new int[capacity];
        hashcodes = new int[capacity];
        links = new int[capacity];
        keys = new string[capacity];
        values = new T[capacity];
        lastSlot = 0;
        freeCount = 0;
        freeIndex = 0;
        lastFreeIndex = 0;
    }

    private void GrowBuckets(int extraSpace = 1)
    {
#if !NETSTANDARD
        ResizeBuckets(Math.Max((int)BitOperations.RoundUpToPowerOf2((uint)(buckets.Length + 1 + extraSpace)) - 1, 7));
#else
        ResizeBuckets(Math.Max((int)PolyFill.RoundUpToPowerOf2((uint)(buckets.Length + 1 + extraSpace)) - 1, 7));
#endif
    }

    private void ResizeBuckets(int capacity)
    {
        buckets = new int[capacity];

        // Regenerate the buckets
        for (int i = 0; i < lastSlot; i++)
        {
            if (keys[i] != null)
            {
                int hash = hashcodes[i];
                ref var bucket = ref GetBucket(hash);
                links[i] = bucket; // If the bucket already had an item in it, simply move that item after the current one in the list
                bucket = i + 1;
            }
        }

        version++;
        //maxListLength = 0;
        // This is not correct as resizing the buckets might leave some chains above the
        // threshold, but counting chains while resizing is difficult...
        //chainsAboveThresh = 0; 
    }

    private void GrowSlots()
    {
#if !NETSTANDARD
        ResizeSlots(Math.Max((int)BitOperations.RoundUpToPowerOf2((uint)hashcodes.Length + 1), 8));
#else
        ResizeSlots(Math.Max((int)PolyFill.RoundUpToPowerOf2((uint)hashcodes.Length + 1), 8));
#endif
    }

    private void ResizeSlots(int capacity)
    {
        //capacity = Math.Max((int)BitOperations.RoundUpToPowerOf2((uint)capacity), 8);

        var nHashcodes = new int[capacity];
        var nLinks = new int[capacity];
        var nKeys = new string[capacity];
        var nValues = new T[capacity];

        // Copy all the items across
        int toCopy = Math.Min(Math.Min(hashcodes.Length, lastSlot), capacity);
        Array.Copy(hashcodes, nHashcodes, toCopy);
        Array.Copy(links, nLinks, toCopy);
        Array.Copy(keys, nKeys, toCopy);
        Array.Copy(values, nValues, toCopy);

        hashcodes = nHashcodes;
        links = nLinks;
        keys = nKeys;
        values = nValues;

        version++;
    }

    public void EnsureCapacity(int capacity)
    {
        if (capacity > Capacity)
            GrowBuckets(capacity);
        if (capacity > keys.Length)
            ResizeSlots(capacity);
    }

    /// <summary>
    /// Optimises the dictionary for red performance and memory efficiency.
    /// </summary>
    /// <param name="targetLoadFactor">The load factor to target. Smaller values result in faster reads at the 
    /// cost of more memory used. A good value is between 1 and 3.</param>
    /// <param name="compact">Whether to compact all the entries in the slots. Saves a bit of space if many 
    /// items have been removed from the dictionary.</param>
    public void Optimize(float targetLoadFactor, bool compact = true)
    {
        int capacity = (int)(Count / targetLoadFactor);
#if !NETSTANDARD
        int pot1 = (int)BitOperations.RoundUpToPowerOf2((uint)capacity + 1);
#else
        int pot1 = (int)PolyFill.RoundUpToPowerOf2((uint)capacity + 1);
#endif
        int pot2 = pot1 >> 1;
        int closer = Math.Abs(capacity - pot1) > Math.Abs(capacity - pot2) ? pot2 : pot1;
        capacity = Math.Max(closer - 1, 7);
        ResizeBuckets(capacity);
        if (compact)
        {
            int writeDest = 0;
            for (int i = 0; i < hashcodes.Length; i++)
            {
                if (keys[i] != null)
                {
                    // Move the item to writeDest
                    // Update links
                    ref var bucket = ref GetBucket(hashcodes[i]);
                    if (bucket == i + 1)
                        bucket = writeDest + 1;
                    else
                    {
                        // Follow the chain
                        int l = bucket - 1;
                        int lastLink = l;
                        while (l != i && l != -1)
                        {
                            lastLink = l;
                            l = links[l] - 1;
                        }
                        links[lastLink] = writeDest + 1;
                    }

                    // Move the item
                    links[i] = 0;
                    keys[writeDest] = keys[i];
                    keys[i] = null;
                    hashcodes[writeDest] = hashcodes[i];
                    values[writeDest] = values[i];

                    writeDest++;
                }
            }
            ResizeSlots(writeDest);
            freeCount = 0;
            lastSlot = writeDest;
        }
        else
        {
            ResizeSlots(hashcodes.Length);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetKeyHash(string key)
    {
        //return key.GetHashCode();
        return GetKeyHash(key.AsSpan());
    }

    //[MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetKeyHash(ReadOnlySpan<char> key)
    {
        //return string.GetHashCode(key);

        int hash = unchecked((int)2166136261);
        const int prime = 16777619;

        /*var bytes = MemoryMarshal.AsBytes(key);

        for (int i = 0; i < bytes.Length; i++)
        {
            hash ^= bytes[i];
            hash *= prime;
        }*/

        // FNV Hash, a very simple hash, but in my testing performe equally well in quality to the
        // default hash (the load factor and total number of buckets with links are almost identical),
        // in fact FNV was sometimes even slightly better. And it's always slightly faster.
        int len = key.Length;
#if !NETSTANDARD
        var ints = MemoryMarshal.CreateReadOnlySpan(
#else
        var ints = PolyFill.CreateReadOnlySpan(
#endif
            ref Unsafe.As<char, int>(ref MemoryMarshal.GetReference(key)),
            len << 1);//MemoryMarshal.Cast<char, int>(key);
        int lenInt = len >> 1;
        for (int i = 0; i < lenInt; i++)
        {
            hash ^= ints[i];
            hash *= prime;
        }
        if ((len & 1) == 1)
        {
            hash ^= key[len - 1];
            hash *= prime;
        }

        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool KeyMatch(int hash, int ind, in ReadOnlySpan<char> key)
    {
        if (hash != hashcodes[ind])
            return false;

        ref var searchKeyRef = ref MemoryMarshal.GetReference(key);
        string? tableKey = keys[ind];
#if !NETSTANDARD
        ref readonly char tableKeyRef = ref (tableKey == null ? ref Unsafe.NullRef<char>() : ref tableKey.GetPinnableReference());
        if (Unsafe.AreSame(in searchKeyRef, in tableKeyRef))
            return true;
        ReadOnlySpan<char> tableKeySpan = tableKey.AsSpan();
#else
        ReadOnlySpan<char> tableKeySpan = tableKey.AsSpan();
        ref char tableKeyRef = ref MemoryMarshal.GetReference(tableKeySpan);
        if (Unsafe.AreSame(ref searchKeyRef, ref tableKeyRef))
            return true;
#endif

        return key.SequenceEqual(tableKeySpan);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ResizeBucketsIfNeeded(int chainLength)
    {
        /*if (chainLength > SuggestedChainResizeThreshold)
        {
            chainsAboveThresh++;
            if (chainsAboveThresh > NChainResizeThreshold)
                goto Resize;
        }
        else if (chainLength > MaxChainResizeThreshold)
            goto Resize;*/

        if (chainLength > ChainResizeCheckThresh)
        {
            if (LoadFactor > targetLoadFactor)
                goto Resize;
        }

        return false;

    Resize:
        GrowBuckets();
        //Console.WriteLine($"Resize, too may collisions. Add or update. cap={Capacity}");
        return true;
    }

    private ref T InsertNewItem(string key, int hashcode, int lastItem, ref int bucket, out int slot)
    {
        // Find a free slot to put our item in
        if (freeCount > 0)
        {
            slot = freeIndex;
            freeCount--;
            freeIndex = links[slot];
        }
        else
        {
            slot = lastSlot++;
        }

        // Grow the dict if needed and try again
        if (slot >= hashcodes.Length)
        {
            GrowSlots();
            lastSlot--;
            //Console.WriteLine($"Resize, no more slots. Insert new item. cap={hashcodes.Length}");
            //return ref AddOrUpdate(key);
            return ref InsertNewItem(key, hashcode, lastItem, ref bucket, out slot);
        }

        // Insert item
        hashcodes[slot] = hashcode;
        keys[slot] = key;
        //values[slot] = value;

        // Link the item into the dict
        if (bucket == 0)
            bucket = slot + 1;
        else
            links[lastItem] = slot + 1;

        version++;

        return ref values[slot];
    }

    public ref T AddOrUpdate(ReadOnlySpan<char> key)
    {
        if (buckets.Length == 0)
        {
            GrowBuckets();
            GrowSlots();
            //Console.WriteLine($"Resize, empty dict. Add or update. cap={Capacity}");
            return ref AddOrUpdate(key);
        }

        int hashcode = GetKeyHash(key);
        //int i = FindKey(hashcode, out bool collided);
        ref int bucket = ref GetBucket(hashcode);
        int i = bucket - 1;
        int listLen = 0;
        if (bucket != 0)
        {
            // Go through all the items in this bucket to see if any of them have matching hashcodes
            while (!KeyMatch(hashcode, i, key))
            {
                int nextI = links[i] - 1;

                if (nextI == -1)
                    goto Collision;

                i = nextI;
                listLen++;
            }
            // We could check the actual key == other key, but check the full hashcode is *probably* enough for now...
            // Key already exists...
            return ref values[i];

        Collision:
            //maxListLength = Math.Max(maxListLength, listLen);
            if (ResizeBucketsIfNeeded(listLen))
                return ref AddOrUpdate(key);
        }

        return ref InsertNewItem(key.ToString(), hashcode, i, ref bucket, out _);
    }

    public ref T AddOrUpdate(string key)
    {
        if (buckets.Length == 0)
        {
            GrowBuckets();
            GrowSlots();
            //Console.WriteLine($"Resize, empty. Add or update. cap={Capacity}");
            return ref AddOrUpdate(key);
        }

        int hashcode = GetKeyHash(key);
        //int i = FindKey(hashcode, out bool collided);
        ref int bucket = ref GetBucket(hashcode);
        int i = bucket - 1;
        int listLen = 0;
        if (bucket != 0)
        {
            // Go through all the items in this bucket to see if any of them have matching hashcodes
            while (hashcode != hashcodes[i] || key != keys[i])
            {
                int nextI = links[i] - 1;

                if (nextI == -1)
                    goto Collision;

                i = nextI;
                listLen++;
            }
            // We could check the actual key == other key, but check the full hashcode is *probably* enough for now...
            // Key already exists...
            return ref values[i];

        Collision:
            //maxListLength = Math.Max(maxListLength, listLen);
            if (ResizeBucketsIfNeeded(listLen))
                return ref AddOrUpdate(key);
        }

        return ref InsertNewItem(key, hashcode, i, ref bucket, out _);
    }

    /*public bool TryAdd(ReadOnlySpan<char> key, T value)
    {

    }*/

    public bool TryAdd(string key, T value/*, bool update = false*/)
    {
        if (buckets.Length == 0)
        {
            GrowBuckets();
            GrowSlots();
            //Console.WriteLine($"Resize, empty. Try add. cap={Capacity}");
            return TryAdd(key, value);
        }

        int hashcode = GetKeyHash(key);
        ref int bucket = ref GetBucket(hashcode);
        int i = bucket - 1;
        int listLen = 0;
        if (bucket != 0)
        {
            // Go through all the items in this bucket to see if any of them have matching hashcodes
            while (hashcode != hashcodes[i] || key != keys[i])
            {
                int nextI = links[i] - 1;

                if (nextI == -1)
                    goto Collision;

                i = nextI;
                listLen++;
            }
            // We could check the actual key == other key, but check the full hashcode is *probably* enough for now...
            // Key already exists...
            return false;

        Collision:
            //maxListLength = Math.Max(maxListLength, listLen);
            if (ResizeBucketsIfNeeded(listLen))
                return TryAdd(key, value);
        }

        ref var val = ref InsertNewItem(key, hashcode, i, ref bucket, out _);
        val = value;

        return true;
    }

    public void Add(string key, T value)
    {
        if (!TryAdd(key, value))
            throw new ArgumentOutOfRangeException(nameof(key));
    }

    public void Add(KeyValuePair<string, T> item) => Add(item.Key, item.Value);

    [Obsolete("Using the Add(object, object?) signature is not recommended. Did you mean to use Add(string, T)?")]
    public void Add(object key, object? value)
    {
        if (key is not string keyStr)
            throw new ArgumentException(null, nameof(key));
        if (value is not T valueT)
            throw new ArgumentException(null, nameof(value));

        Add(keyStr, valueT);
    }

    public void Clear()
    {
        if (Count == 0)
            return;

        // Clear the keys and values so they can be GC-ed
        Array.Clear(links, 0, lastSlot);
        //Array.Clear(hashcodes, 0, lastSlot);
        Array.Clear(keys, 0, lastSlot);
        if (!typeof(T).IsValueType)
            Array.Clear(values, 0, lastSlot);
        Array.Clear(buckets, 0, buckets.Length);

        lastSlot = 0;
        freeCount = 0;
        version++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(KeyValuePair<string, T> item) => Contains(item.Key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Contains(object key)
    {
        if (key is string str)
            return Contains(str);
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(string key) => FindKey(key) != -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool ContainsKey(ReadOnlySpan<char> key) => FindKey(key) != -1;

    public void CopyTo(KeyValuePair<string, T>[] array, int arrayIndex)
    {
        if (array.Length - arrayIndex < Count)
            throw new ArgumentOutOfRangeException(nameof(array), "Array is too small to contain the dictionary KVPs.");

        int ai = arrayIndex;
        for (int i = 0; i < lastSlot; i++)
        {
            var k = keys[i];
            if (k != null)
                array[ai++] = new(k, values[i]);
        }
    }

    public void CopyTo(Array array, int index)
    {
        if (array.GetType().GetElementType() != typeof(KeyValuePair<string, T>))
            throw new InvalidCastException();

        CopyTo((KeyValuePair<string, T>[])array, index);
    }

    /// <inheritdoc cref="Remove(string)"/>
    public bool Remove(ReadOnlySpan<char> key)
    {
        return Remove(key, out _);
    }

    /// <inheritdoc cref="Remove(string, out T)"/>
    public bool Remove(ReadOnlySpan<char> key, out T item)
    {
        if (buckets.Length == 0)
        {
            item = default!;
            return false;
        }

        int hash = GetKeyHash(key);
        ref var bucket = ref GetBucket(hash);

        // Find the current item
        int ind = bucket - 1;
        int prevInd = -1;
        if (ind != -1)
        {
            // Go through all the items in this bucket to see if any of them have matching hashcodes
            while (!KeyMatch(hash, ind, key))
            {
                prevInd = ind;
                ind = links[ind] - 1;

                if (ind == -1) // Item was not found and there are no more items in the bucket
                {
                    item = default!;
                    return false;
                }
            }
            // Key has been found!
        }

        // Clear the key and value so that the GC can reclaim them.
        keys[ind] = null;
        item = values[ind];
#if !NETSTANDARD
        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
#endif
            values[ind] = default!;

        // Update the bucket to point to the next linked item or null
        ref int link = ref links[ind];
        if (prevInd != -1)
        {
            // Item is in the middle/end of a bucket
            links[prevInd] = link;
        }
        else
        {
            // Item is the first item in the bucket
            bucket = link;
        }
        // Remove the old link
        link = 0;
        // Update free list
        if (freeCount == 0)
        {
            // Create new free list
            freeIndex = lastFreeIndex = ind;
        }
        else
        {
            // Update existing free list
            links[lastFreeIndex] = ind + 1;
            lastFreeIndex = ind;
        }
        freeCount++;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(string key) => Remove(key.AsSpan());

    /// <summary>
    /// Removes an item with the specified key from the dictionary, and returns the removed value.
    /// </summary>
    /// <param name="key">The key of the element to remove.</param>
    /// <param name="item">The value associated with the key which was removed.</param>
    /// <returns><see langword="true"/> if the item was successfully removed from the dictionary.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(string key, out T item) => Remove(key.AsSpan(), out item);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Remove(KeyValuePair<string, T> item) => Remove(item.Key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Remove(object key) => Remove((string)key);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindKey(ReadOnlySpan<char> key, out bool collided)
    {
        int hash = GetKeyHash(key);

        collided = false;
        if (buckets.Length == 0)
            return -1;

        int slot = GetBucket(hash) - 1;

        if (slot != -1)
        {
            // Go through all the items in this bucket to see if any of them have matching hashcodes
            // If the links arrays somehow gets a loop in it (concurrent access?), this will spin forever.
            while (!KeyMatch(hash, slot, key))
            {
                slot = links[slot] - 1;

                if (slot == -1)
                {
                    collided = true;
                    goto NotFound;
                }
            }
            // We could check the actual key == other key, but check the full hashcode is *probably* enough for now...
            // Key has been found!
            return slot;
        }

    NotFound:
        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FastMod(int hash, int mod)
    {
        // https://lemire.me/blog/2016/06/27/a-fast-alternative-to-the-modulo-reduction/
        return unchecked((int)(((ulong)(uint)hash * (ulong)mod) >> 32));
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ref int GetBucket(int hash)
    {
        //return ref buckets[unchecked((uint)hash) % buckets.Length];
        return ref buckets[FastMod(hash, buckets.Length)];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindKey(string key)
    {
        return FindKey(key.AsSpan());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindKey(ReadOnlySpan<char> key)
    {
        return FindKey(key, out bool _);
    }

#if !NETSTANDARD
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out T value)
#else
    public bool TryGetValue(string key, out T value)
#endif
    {
        int ind = FindKey(key);
        if (ind == -1)
        {
            value = default!;
            return false;
        }

        value = values[ind];

        return true;
    }

    /// <inheritdoc cref="TryGetValue(string, out T)"/>
#if !NETSTANDARD
    public bool TryGetValue(ReadOnlySpan<char> key, [MaybeNullWhen(false)] out T value)
#else
    public bool TryGetValue(ReadOnlySpan<char> key, out T value)
#endif
    {
        int ind = FindKey(key);
        if (ind == -1)
        {
            value = default!;
            return false;
        }

        value = values[ind];

        return true;
    }

    /// <summary>
    /// Gets the value associated with the given key. The reference is only valid so long as the dictionary is not mutated.
    /// </summary>
    /// <param name="key">The key to get the value of.</param>
    /// <returns>A reference to the value in the dictionary if it exists, or a null reference.</returns>
    public ref T TryGetValue(ReadOnlySpan<char> key)
    {
        int ind = FindKey(key);
        if (ind == -1)
            return ref Unsafe.NullRef<T>();

        return ref values[ind];
    }

    /// <summary>
    /// Returns an enumerable of all the values with the given keys.
    /// </summary>
    /// <param name="keys">The keys to search for.</param>
    /// <returns>An <see cref="IEnumerable{T}"/> of all the values which match the given keys.</returns>
    public IEnumerable<T> GetValues(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            if (TryGetValue(key, out var value))
                yield return value;
    }

    public IEnumerator<KeyValuePair<string, T>> GetEnumerator() => new Enumerator(this, 0);
    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this, 0);
    IDictionaryEnumerator IDictionary.GetEnumerator() => new Enumerator(this, 0);

    public struct Enumerator : IEnumerator<KeyValuePair<string, T>>, IDictionaryEnumerator
    {
        private readonly StringDict<T> dict;
        private readonly int version;
        private int index;
        private KeyValuePair<string, T> current;
        private readonly EnumerationReturnType getEnumeratorRetType;  // What should Enumerator.Current return?

        public enum EnumerationReturnType
        {
            None,
            DictEntry,
            KeyValuePair
        }

        internal Enumerator(StringDict<T> dictionary, EnumerationReturnType getEnumeratorRetType)
        {
            dict = dictionary;
            version = dictionary.version;
            index = 0;
            this.getEnumeratorRetType = getEnumeratorRetType;
            current = default;
        }

        public bool MoveNext()
        {
            if (version != dict.version)
                throw new InvalidOperationException();

            // Use unsigned comparison since we set index to dictionary.count+1 when the enumeration ends.
            // dictionary.count+1 could be negative if dictionary.count is int.MaxValue
            while ((uint)index < (uint)dict.lastSlot)
            {
                //ref Entry entry = ref dict._entries![index++];
                int i = index++;

                if (dict.keys[i] is string key)
                {
                    current = new KeyValuePair<string, T>(key, dict.values[i]);
                    return true;
                }
            }

            index = dict.lastSlot + 1;
            current = default;
            return false;
        }

        public readonly KeyValuePair<string, T> Current => current;

        public readonly void Dispose() { }

        readonly object? IEnumerator.Current
        {
            get
            {
                if (index == 0 || (index == dict.lastSlot + 1))
                    throw new InvalidOperationException();

                if (getEnumeratorRetType == EnumerationReturnType.DictEntry)
                {
                    return new DictionaryEntry(current.Key, current.Value);
                }

                return new KeyValuePair<string, T>(current.Key, current.Value);
            }
        }

        void IEnumerator.Reset()
        {
            if (version != dict.version)
                throw new InvalidOperationException();

            index = 0;
            current = default;
        }

        readonly DictionaryEntry IDictionaryEnumerator.Entry
        {
            get
            {
                if (index == 0 || (index == dict.lastSlot + 1))
                    throw new InvalidOperationException();

                return new(current.Key, current.Value);
            }
        }

        readonly object IDictionaryEnumerator.Key
        {
            get
            {
                if (index == 0 || (index == dict.lastSlot + 1))
                    throw new InvalidOperationException();

                return current.Key;
            }
        }

        readonly object? IDictionaryEnumerator.Value
        {
            get
            {
                if (index == 0 || (index == dict.lastSlot + 1))
                    throw new InvalidOperationException();

                return current.Value;
            }
        }
    }

    public readonly struct KeyCollection : ICollection<string>, ICollection
    {
        private readonly StringDict<T> dict;
        private readonly int version;

        internal KeyCollection(StringDict<T> dict)
        {
            this.dict = dict;
            version = dict.version;
        }

        public readonly int Count => dict.Count;
        public readonly bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public readonly void Add(string item) => throw new InvalidOperationException();

        public readonly void Clear() => throw new InvalidOperationException();

        public readonly bool Contains(string item)
        {
            throw new NotImplementedException();
        }

        public readonly void CopyTo(string[] array, int arrayIndex)
        {
            for (int i = 0; i < dict.lastSlot; i++)
            {
                var k = dict.keys[i];
                if (k != null)
                    array[arrayIndex++] = k;
            }
        }

        public void CopyTo(Array array, int index) => CopyTo((string[])array, index);

        public readonly bool Remove(string item) => throw new InvalidOperationException();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly IEnumerator<string> GetEnumerator()
        {
            for (int i = 0; i < dict.lastSlot; i++)
            {
                if (version != dict.version)
                    throw new InvalidOperationException("The dictionary has been mutated since iteration began!");

                var k = dict.keys[i];
                if (k != null)
                    yield return k;
            }
        }
    }

    public readonly struct ValueCollection : ICollection<T>, ICollection
    {
        private readonly StringDict<T> dict;
        private readonly int version;

        internal ValueCollection(StringDict<T> dict)
        {
            this.dict = dict;
            version = dict.version;
        }

        public readonly int Count => dict.Count;
        public readonly bool IsReadOnly => true;
        public bool IsSynchronized => false;
        public object SyncRoot => this;

        public readonly void Add(T item) => throw new InvalidOperationException();

        public readonly void Clear() => throw new InvalidOperationException();

        public readonly bool Contains(T item)
        {
            throw new NotImplementedException();
        }

        public readonly void CopyTo(T[] array, int arrayIndex)
        {
            for (int i = 0; i < dict.lastSlot; i++)
            {
                var k = dict.keys[i];
                if (k != null)
                    array[arrayIndex++] = dict.values[i];
            }
        }

        public void CopyTo(Array array, int index) => CopyTo((T[])array, index);

        public readonly bool Remove(T item) => throw new InvalidOperationException();

        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public readonly IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < dict.lastSlot; i++)
            {
                if (version != dict.version)
                    throw new InvalidOperationException("The dictionary has been mutated since iteration began!");

                var k = dict.keys[i];
                if (k != null)
                    yield return dict.values[i];
            }
        }
    }

#if DEBUG
    /// <summary>
    /// A little helper class to begtter visualise the inner workings of the dictionary for debugging. 
    /// Only available in DEBUG builds.
    /// </summary>
    private class DebugVisualiser
    {
        private readonly StringDict<T> dict;

        public string[] Buckets => dict.buckets.Select(x => x > 0 ? $"{x - 1} ({dict.keys[x - 1]}){GetLinks(x)}" : "").ToArray();
        public string[] Links => dict.links.Select(x => x > 0 ? $"{x - 1} => '{dict.keys[x - 1]}'" : "").ToArray();
        public string[] HashCodes => dict.hashcodes.Select(x => x.ToString()).ToArray();
        public string?[] KeysStore => dict.keys;//.Select(x => x).ToArray();
        public T[] ValuesStore => dict.values;//.Select(x => x > 0 ? (x-1).ToString() : "").ToArray();

        public int A_Capacity => dict.Capacity;
        public int A_Count => dict.Count;
        public int LastSlot => dict.lastSlot;
        public int FreeCount => dict.freeCount;
        public int FreeIndex => dict.freeIndex;
        [DebuggerDisplay("{P0_LoadFactor}")]
        public string P0_LoadFactor => $"{dict.Count / (float)dict.Capacity:F3} ({dict.Count}/{dict.Capacity})";
        [DebuggerDisplay("{P1_HashCollisions} / {dict.Count} (items)")]
        public int P1_HashCollisions => dict.DBG_CountLinks();
        public string[] P2_ListLengths => MakeHistogram();

        public ICollection<KeyValuePair<string, T>> A_Items => dict;
        public ICollection<string> A_Keys => dict.Keys;
        public ICollection<T> A_Values => dict.Values;

        public DebugVisualiser(StringDict<T> dict)
        {
            this.dict = dict;
        }

        private string[] MakeHistogram()
        {
            var hist = dict.DBG_LinksHistogram();
            var dst = new string[hist.Length];
            StringBuilder sb = new();
            int maxCount = hist.Skip(1).Take(hist.Length - 2).Max();
            for (int i = 0; i < hist.Length; i++)
            {
                sb.Append($"{hist[i],12} ");
                int val = hist[i] / (maxCount / 16);
                for (int j = 0; j < 16; j++)
                    sb.Append(j < val ? '#' : ' ');
                dst[i] = sb.ToString();
                sb.Clear();
            }
            return dst;
        }

        private string GetLinks(int bucket)
        {
            StringBuilder sb = new();
            int i = dict.links[bucket - 1] - 1;
            while (i >= 0)
            {
                sb.Append(" => ");
                sb.Append(i);
                sb.Append(" (");
                sb.Append(dict.keys[i]);
                sb.Append(')');
                i = dict.links[i] - 1;
            }

            return sb.ToString();
        }
    }
#endif
}
