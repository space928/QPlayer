using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Text;

namespace QPlayer.Utilities;

public class ObservableSet<T> : ISet<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private static readonly PropertyChangedEventArgs countChangedArgs = new(nameof(Count));
    private readonly HashSet<T> hashSet;

    public int Count => hashSet.Count;
    public bool IsReadOnly => false;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableSet() : this(0) { }

    public ObservableSet(int capacity)
    {
        hashSet = new(capacity);
    }

    public ObservableSet(IEnumerable<T> collection)
    {
        hashSet = new(collection);
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, T changed)
    {
        CollectionChanged?.Invoke(this, new(action, changed));
        switch (action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Remove:
                OnCountChanged();
                break;
        }
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action)
    {
        CollectionChanged?.Invoke(this, new(action));
        switch (action)
        {
            case NotifyCollectionChangedAction.Reset:
                OnCountChanged();
                break;
        }
    }

    private void OnCountChanged()
    {
        PropertyChanged?.Invoke(this, countChangedArgs);
    }

    public bool Add(T item)
    {
        if (!hashSet.Add(item))
            return false;

        OnCollectionChanged(NotifyCollectionChangedAction.Add, item);
        return true;
    }

    void ICollection<T>.Add(T item) => Add(item);

    public void Clear()
    {
        hashSet.Clear();
        OnCollectionChanged(NotifyCollectionChangedAction.Reset);
    }

    public bool Remove(T item)
    {
        var res = hashSet.Remove(item);
        if (res)
            OnCollectionChanged(NotifyCollectionChangedAction.Remove, item);
        return res;
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        hashSet.ExceptWith(other);
        OnCollectionChanged(NotifyCollectionChangedAction.Reset);
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        hashSet.IntersectWith(other);
        OnCollectionChanged(NotifyCollectionChangedAction.Reset);
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        hashSet.SymmetricExceptWith(other);
        OnCollectionChanged(NotifyCollectionChangedAction.Reset);
    }

    public void UnionWith(IEnumerable<T> other)
    {
        hashSet.UnionWith(other);
        OnCollectionChanged(NotifyCollectionChangedAction.Reset);
    }

    public bool Contains(T item) => hashSet.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => hashSet.CopyTo(array, arrayIndex);
    public bool IsProperSubsetOf(IEnumerable<T> other) => hashSet.IsProperSubsetOf(other);
    public bool IsProperSupersetOf(IEnumerable<T> other) => hashSet.IsProperSupersetOf(other);
    public bool IsSubsetOf(IEnumerable<T> other) => hashSet.IsSubsetOf(other);
    public bool IsSupersetOf(IEnumerable<T> other) => hashSet.IsSupersetOf(other);
    public bool Overlaps(IEnumerable<T> other) => hashSet.Overlaps(other);
    public bool SetEquals(IEnumerable<T> other) => hashSet.SetEquals(other);

    public IEnumerator<T> GetEnumerator() => hashSet.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>
/// An observable hashset with some specialisations that make it optimal for storing a selection. 
/// </summary>
/// <typeparam name="T"></typeparam>
public class ObservableSelectionSet<T> : ISet<T>, INotifyCollectionChanged, INotifyPropertyChanged
    where T : class
{
    private static readonly PropertyChangedEventArgs countChangedArgs = new(nameof(Count));
    private readonly HashSet<T> hashSet;
    /// <summary>
    /// Store the first item in the set separately as an optimisation for our particular use case, where the set often only has one item in it.
    /// </summary>
    private T? firstItem;

    public int Count => firstItem == null ? hashSet.Count : 1;
    public bool IsReadOnly => false;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableSelectionSet() : this(0) { }

    public ObservableSelectionSet(int capacity)
    {
        hashSet = new(capacity);
    }

    public ObservableSelectionSet(IEnumerable<T> collection)
    {
        hashSet = new(collection);
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, T changed)
    {
        CollectionChanged?.Invoke(this, new(action, changed));
        switch (action)
        {
            case NotifyCollectionChangedAction.Add:
            case NotifyCollectionChangedAction.Remove:
                OnCountChanged();
                break;
        }
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, T oldItem, T newItem)
    {
        CollectionChanged?.Invoke(this, new(action, newItem, oldItem));
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action)
    {
        CollectionChanged?.Invoke(this, new(action));
        switch (action)
        {
            case NotifyCollectionChangedAction.Reset:
                OnCountChanged();
                break;
        }
    }

    private void OnCountChanged()
    {
        PropertyChanged?.Invoke(this, countChangedArgs);
    }

    public bool Add(T item)
    {
        if (hashSet.Count == 0)
        {
            if (firstItem == null)
            {
                firstItem = item;
                goto Success;
            }
            else
            {
                hashSet.Add(firstItem);
                firstItem = null;
            }
        }

        if (!hashSet.Add(item))
            return false;

    Success:
        OnCollectionChanged(NotifyCollectionChangedAction.Add, item);
        return true;
    }

    void ICollection<T>.Add(T item) => Add(item);

    /// <summary>
    /// Replaces the content of this set with the single item given.
    /// </summary>
    /// <param name="item"></param>
    /// <exception cref="InvalidOperationException"></exception>
    public void Replace(T item)
    {
        var old = firstItem;
        firstItem = item;
        if (hashSet.Count > 0)
        {
            hashSet.Clear();
            OnCollectionChanged(NotifyCollectionChangedAction.Reset);
        }
        else if (old != null)
        {
            OnCollectionChanged(NotifyCollectionChangedAction.Replace, old, item);
        }
        else
        {
            OnCollectionChanged(NotifyCollectionChangedAction.Add, item);
        }
    }

    public void Clear()
    {
        firstItem = null;
        hashSet.Clear();
        OnCollectionChanged(NotifyCollectionChangedAction.Reset);
    }

    public bool Remove(T item)
    {
        if (firstItem != null && ReferenceEquals(firstItem, item))
        {
            firstItem = null;
            goto Success;
        }

        if (!hashSet.Remove(item))
            return false;

    Success:
        OnCollectionChanged(NotifyCollectionChangedAction.Remove, item);
        return true;
    }

    [Obsolete] public void ExceptWith(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public void IntersectWith(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public void SymmetricExceptWith(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public void UnionWith(IEnumerable<T> other) => throw new NotImplementedException();
    public bool Contains(T item) => ReferenceEquals(item, firstItem) || hashSet.Contains(item);
    public void CopyTo(T[] array, int arrayIndex)
    {
        if (firstItem != null)
            array[arrayIndex] = firstItem;
        else
            hashSet.CopyTo(array, arrayIndex);
    }

    [Obsolete] public bool IsProperSubsetOf(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public bool IsProperSupersetOf(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public bool IsSubsetOf(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public bool IsSupersetOf(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public bool Overlaps(IEnumerable<T> other) => throw new NotImplementedException();
    [Obsolete] public bool SetEquals(IEnumerable<T> other) => throw new NotImplementedException();

    public IEnumerator<T> GetEnumerator()
    {
        if (Count == 0)
            yield break;
        else if (Count == 1)
            yield return firstItem!;
        else
            foreach (var val in hashSet)
                yield return val;
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
