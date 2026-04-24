using QPlayer.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Text;

namespace QPlayer.Utilities;

/// <summary>
/// An array-backed list which implements <see cref="INotifyCollectionChanged"/> and registers changes with the <see cref="UndoManager"/>.
/// </summary>
/// <typeparam name="T"></typeparam>
public partial class UndoableObservableCollection<T> : IList<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly List<T> list;
    private static readonly PropertyChangedEventArgs _countChangedEventArgs = new(nameof(Count));
    private static readonly PropertyChangedEventArgs _indexerChangedEventArgs = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs _collectionResetEventArgs = new(NotifyCollectionChangedAction.Reset);

    public int Count => list.Count;
    public bool IsReadOnly => false;

    public T this[int index]
    {
        get => list[index];
        set => SetItem(value, index);
    }

    public UndoableObservableCollection()
    {
        list = [];
    }

    public UndoableObservableCollection(int capacity)
    {
        list = new(capacity);
    }

    public UndoableObservableCollection(IEnumerable<T> enumerable)
    {
        list = new(enumerable);
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged(string? propertyName) => PropertyChanged?.Invoke(this, new(propertyName));
    private void OnPropertyChanged(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);
    private void OnCollectionChanged(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);
    private void OnItemChanged(NotifyCollectionChangedAction action, T changed, int index) => CollectionChanged?.Invoke(this, new(action, changed, index));
    private void OnItemChanged(NotifyCollectionChangedAction action, IEnumerable<T> changed, int index) => CollectionChanged?.Invoke(this, new(action, changed, index));
    private void OnItemChanged(NotifyCollectionChangedAction action, T oldObj, T newObj, int index) => CollectionChanged?.Invoke(this, new(action, newObj, oldObj, index));
    private void OnItemMoved(T obj, int oldInd, int newInd) => CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Move, obj, newInd, oldInd));

    private void SetItem(T upd, int ind)
    {
        var old = list[ind];
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Replace, old, upd, ind);
        list[ind] = upd;
        UndoManager.RegisterAction($"Changed {upd}", () => SetItem(old, ind), () => SetItem(upd, ind));
    }

    public void Move(int fromIndex, int toIndex)
    {
        var removedItem = list[fromIndex];

        list.RemoveAt(fromIndex);
        list.Insert(toIndex, removedItem);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemMoved(removedItem, fromIndex, toIndex);

        UndoManager.RegisterAction($"Moved {removedItem}", () => Move(toIndex, fromIndex), () => Move(fromIndex, toIndex));
    }

    public void Insert(int index, T item)
    {
        list.Insert(index, item);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, item, index);

        UndoManager.RegisterAction($"Added {item}", () => RemoveAt(index), () => Insert(index, item));
    }

    public void RemoveAt(int index)
    {
        var item = list[index];
        list.RemoveAt(index);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Remove, item, index);

        UndoManager.RegisterAction($"Removed {item}", () => Insert(index, item), () => RemoveAt(index));
    }

    public bool Remove(T item)
    {
        int index = list.IndexOf(item);
        if (index == -1)
            return false;

        list.RemoveAt(index);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Remove, item, index);

        UndoManager.RegisterAction($"Removed {item}", () => Insert(index, item), () => RemoveAt(index));
        return true;
    }

    public void RemoveLast(int count)
    {
        int index = list.Count - count;
        var removed = list.Slice(index, count);
        list.RemoveRange(index, count);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Remove, removed, index);

        UndoManager.RegisterAction($"Removed {count} items", () => AddRange(removed), () => RemoveLast(count));
    }

    public void Add(T item)
    {
        int index = list.Count;
        list.Add(item);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, item, index);

        UndoManager.RegisterAction($"Added {item}", () => RemoveAt(index), () => Add(item));
    }

    public void AddRange(IEnumerable<T> items)
    {
        int index = list.Count;
        list.AddRange(items);
        int added = list.Count - index;

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, items, index);

        UndoManager.RegisterAction($"Added {added} items", () => RemoveLast(added), () => AddRange(items));
    }

    public void Clear()
    {
        var oldItems = list.ToArray();
        list.Clear();

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnCollectionChanged(_collectionResetEventArgs);

        UndoManager.RegisterAction($"Cleared collection", () => AddRange(oldItems), () => Clear());
    }

    public int IndexOf(T item) => list.IndexOf(item);
    public bool Contains(T item) => list.Contains(item);
    public void CopyTo(T[] array, int arrayIndex = 0) => list.CopyTo(array, arrayIndex);

    public IEnumerator<T> GetEnumerator() => list.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
