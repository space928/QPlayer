using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;

namespace QPlayer.ViewModels;

public class ObservableArray<T> : ICollection<T>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly T[] array;
    public int Count => array.Length;
    public bool IsReadOnly => false;
    public T[] Array => array;
    public T this[int index]
    {
        get => array[index];
        set
        {
            array[index] = value;
            NotifyChange(index);
        }
    }

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableArray(T[] array)
    {
        this.array = array;
    }

    public void NotifyChange(int index = -1)
    {
        if (index == -1)
            CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Reset));
        else
            CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Replace, array[index], index));
    }

    public void Add(T item) => throw new InvalidOperationException();
    public void Clear() => throw new InvalidOperationException();
    public bool Contains(T item) => array.Contains(item);
    public void CopyTo(T[] array, int arrayIndex) => array.CopyTo(array, arrayIndex);
    public IEnumerator<T> GetEnumerator()
    {
        for (int i = 0; i < array.Length; i++)
            yield return array[i];
    }
    public bool Remove(T item) => throw new InvalidOperationException();
    IEnumerator IEnumerable.GetEnumerator() => array.GetEnumerator();
}
