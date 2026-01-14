using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace QPlayer.Utilities;

public class ReactiveCollection<T> : ObservableCollection<T>
    where T : INotifyPropertyChanged
{
    // TODO: This collection does NOT support duplicates correctly
    public delegate void ItemChangedEventHandler(int index, T item, string property);

    public event ItemChangedEventHandler? ItemChanged;

    // A fairly rubbish solution to the problem of needing to know the index of the item which has changed
    private readonly Dictionary<T, int> indexCache = [];

    private void OnItemChanged(object? item, PropertyChangedEventArgs args)
    {
        if (item is T obj && indexCache.TryGetValue(obj, out int index))
            ItemChanged?.Invoke(index, obj, args.PropertyName ?? string.Empty);
    }

    protected override void ClearItems()
    {
        foreach (var item in Items)
            item.PropertyChanged -= OnItemChanged;
        indexCache.Clear();

        base.ClearItems();
    }

    protected override void RemoveItem(int index)
    {
        //base.RemoveItem(index);
        CheckReentrancy();
        T removedItem = this[index];
        removedItem.PropertyChanged -= OnItemChanged;
        indexCache.Remove(removedItem);
        // Expensive...
        for (int i = index + 1; i < Items.Count; i++)
            indexCache[this[i]] = i - 1;

        base.RemoveItem(index);

        OnCountPropertyChanged();
        OnIndexerPropertyChanged();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Remove, removedItem, index));
    }

    protected override void InsertItem(int index, T item)
    {
        item.PropertyChanged += OnItemChanged;
        base.InsertItem(index, item);
        indexCache.Add(item, index);
        // Expensive...
        for (int i = index + 1; i < Items.Count; i++)
            indexCache[this[i]] = i + 1;
    }

    protected override void SetItem(int index, T item)
    {
        CheckReentrancy();
        T originalItem = this[index];
        originalItem.PropertyChanged -= OnItemChanged;
        item.PropertyChanged += OnItemChanged;
        indexCache.Remove(originalItem);
        indexCache.Add(item, index);
        base.SetItem(index, item);

        OnIndexerPropertyChanged();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Replace, originalItem, item, index));
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
        CheckReentrancy();

        T removedItem = this[oldIndex];

        base.RemoveItem(oldIndex);
        base.InsertItem(newIndex, removedItem); // Is this even correct?

        for (int i = oldIndex; i < Math.Min(newIndex + 1, Count); i++)
            indexCache[this[i]] = i;

        OnIndexerPropertyChanged();
        OnCollectionChanged(new(NotifyCollectionChangedAction.Move, removedItem, newIndex, oldIndex));
    }

    /// <summary>
    /// Helper to raise a PropertyChanged event for the Count property
    /// </summary>
    private void OnCountPropertyChanged() => OnPropertyChanged(EventArgsCache.CountPropertyChanged);

    /// <summary>
    /// Helper to raise a PropertyChanged event for the Indexer property
    /// </summary>
    private void OnIndexerPropertyChanged() => OnPropertyChanged(EventArgsCache.IndexerPropertyChanged);

    internal static class EventArgsCache
    {
        internal static readonly PropertyChangedEventArgs CountPropertyChanged = new("Count");
        internal static readonly PropertyChangedEventArgs IndexerPropertyChanged = new("Item[]");
        internal static readonly NotifyCollectionChangedEventArgs ResetCollectionChanged = new(NotifyCollectionChangedAction.Reset);
    }
}
