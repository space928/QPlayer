using QPlayer.ViewModels;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace QPlayer.Utilities;

/// <summary>
/// An array-backed list of <see cref="BindableViewModel{Model}"/> which implements <see cref="INotifyCollectionChanged"/> and registers changes with the <see cref="UndoManager"/>.
/// </summary>
/// <typeparam name="TViewModel">The type of item stored in this list. Must implement <see cref="BindableViewModel{Model}"/>.</typeparam>
/// <typeparam name="TModel">The type of items stored in the bound list. (<see cref="BindableViewModel{Model}.SyncToModel"/>)</typeparam>
public partial class UndoableObservableCollection<TViewModel, TModel> : BindableViewModel<List<TModel>>, IList<TViewModel>, INotifyCollectionChanged, INotifyPropertyChanged
    where TModel : class, new()
    where TViewModel : BindableViewModel<TModel>, new()
{
    private readonly List<TViewModel> list;
    private static readonly PropertyChangedEventArgs _countChangedEventArgs = new(nameof(Count));
    private static readonly PropertyChangedEventArgs _indexerChangedEventArgs = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs _collectionResetEventArgs = new(NotifyCollectionChangedAction.Reset);

    public int Count => list.Count;
    public bool IsReadOnly => false;

    public TViewModel this[int index]
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

    /*public UndoableObservableCollection(IEnumerable<TViewModel> enumerable)
    {
        list = new(enumerable);
    }*/

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    private void OnCollectionChanged() => CollectionChanged?.Invoke(this, _collectionResetEventArgs);
    private void OnCollectionChanged(NotifyCollectionChangedEventArgs args) => CollectionChanged?.Invoke(this, args);
    private void OnItemChanged(NotifyCollectionChangedAction action, TViewModel changed, int index) => CollectionChanged?.Invoke(this, new(action, changed, index));
    private void OnItemChanged(NotifyCollectionChangedAction action, IEnumerable<TViewModel> changed, int index) => CollectionChanged?.Invoke(this, new(action, changed, index));
    private void OnItemChanged(NotifyCollectionChangedAction action, TViewModel oldObj, TViewModel newObj, int index) => CollectionChanged?.Invoke(this, new(action, newObj, oldObj, index));
    private void OnItemMoved(TViewModel obj, int oldInd, int newInd) => CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Move, obj, newInd, oldInd));

    private void SetItem(TViewModel upd, int ind)
    {
        (var old, list[ind]) = (list[ind], upd);

        old.Bind(null);
        if (BoundModel != null)
        {
            upd.Bind(BoundModel[ind]);
            upd.SyncToModel();
        }

        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Replace, old, upd, ind);
        UndoManager.RegisterAction($"Changed {upd}", () => SetItem(old, ind), () => SetItem(upd, ind));
    }

    public void Move(int fromIndex, int toIndex)
    {
        var removedItem = list[fromIndex];

        list.RemoveAt(fromIndex);
        list.Insert(toIndex, removedItem);

        BoundModel?.RemoveAt(fromIndex);
        BoundModel?.Insert(toIndex, removedItem.BoundModel!);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemMoved(removedItem, fromIndex, toIndex);

        UndoManager.RegisterAction($"Moved {removedItem}", () => Move(toIndex, fromIndex), () => Move(fromIndex, toIndex));
    }

    public void Insert(int index, TViewModel item)
    {
        list.Insert(index, item);

        if (BoundModel != null)
        {
            var vm = item;
            if (vm.BoundModel == null)
            {
                vm.Bind(new());
                vm.SyncToModel();
            }
            BoundModel.Insert(index, vm.BoundModel!);
        }

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, item, index);

        UndoManager.RegisterAction($"Added {item}", () => RemoveAt(index), () => Insert(index, item));
    }

    public void RemoveAt(int index)
    {
        var item = list[index];
        list.RemoveAt(index);

        BoundModel?.RemoveAt(index);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Remove, item, index);

        UndoManager.RegisterAction($"Removed {item}", () => Insert(index, item), () => RemoveAt(index));
    }

    public bool Remove(TViewModel item)
    {
        int index = list.IndexOf(item);
        if (index == -1)
            return false;

        list.RemoveAt(index);

        BoundModel?.RemoveAt(index);

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

        BoundModel?.RemoveRange(index, count);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Remove, removed, index);

        UndoManager.RegisterAction($"Removed {count} items", () => AddRange(removed), () => RemoveLast(count));
    }

    public void Add(TViewModel item)
    {
        int index = list.Count;
        list.Add(item);

        if (BoundModel != null)
        {
            var vm = item;
            if (vm.BoundModel == null)
            {
                vm.Bind(new());
                vm.SyncToModel();
            }
            BoundModel.Add(vm.BoundModel!);
        }

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, item, index);

        UndoManager.RegisterAction($"Added {item}", () => RemoveAt(index), () => Add(item));
    }

    public void AddRange(IEnumerable<TViewModel> items)
    {
        int index = list.Count;
        list.AddRange(items);
        int added = list.Count - index;

        if (BoundModel != null)
        {
            BoundModel.Capacity = list.Count;
            for (int i = index; i < list.Count; i++)
            {
                var vm = list[i];
                if (vm.BoundModel == null)
                {
                    vm.Bind(new());
                    vm.SyncToModel();
                }
                BoundModel.Add(vm.BoundModel!);
            }
        }

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, items, index);

        UndoManager.RegisterAction($"Added {added} items", () => RemoveLast(added), () => AddRange(items));
    }

    public void Clear()
    {
        var oldItems = list.ToArray();
        list.Clear();

        BoundModel?.Clear();

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnCollectionChanged(_collectionResetEventArgs);

        UndoManager.RegisterAction($"Cleared collection", () => AddRange(oldItems), () => Clear());
    }

    public int IndexOf(TViewModel item) => list.IndexOf(item);
    public bool Contains(TViewModel item) => list.Contains(item);
    public void CopyTo(TViewModel[] array, int arrayIndex = 0) => list.CopyTo(array, arrayIndex);

    public IEnumerator<TViewModel> GetEnumerator() => list.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #region Model Sync
    /// <summary>
    /// Resynchronises all the cues in this <see cref="VisualCueList"/> to the bound model 
    /// (set with <see cref="Bind(List{Cue})"/>).
    /// </summary>
    public override void SyncToModel()
    {
        if (boundModel == null)
            return;
        base.SyncToModel();

        CollectionsMarshal.SetCount(boundModel, list.Count);
        var dst = CollectionsMarshal.AsSpan(boundModel);
        for (int i = 0; i < list.Count; i++)
        {
            var item = list[i];
            var model = dst[i];
            if (model == null)
                dst[i] = model = new();

            item.Bind(model);
            item.SyncToModel();
        }
    }

    /// <summary>
    /// Syncronises the contents of this CueList with the cue models in the bound model 
    /// (set via <see cref="Bind(List{Cue})"/>).
    /// </summary>
    public override void SyncFromModel()
    {
        base.SyncFromModel();

        if (boundModel == null)
            return;

        CollectionsMarshal.SetCount(list, boundModel.Count);
        var dst = CollectionsMarshal.AsSpan(list);
        for (int i = 0; i < boundModel.Count; i++)
        {
            TModel model = boundModel[i];
            TViewModel item = dst[i];
            if (item == null)
                dst[i] = item = new();
            if (model == null)
            {
                // Create a new default instance using the values in this view model.
                boundModel[i] = model = new();
                item.Bind(model);
                item.SyncToModel();
            }
            else
            {
                item.Bind(model);
                item.SyncFromModel();
            }
        }
        OnCollectionChanged();
    }
    #endregion
}

/// <summary>
/// An array-backed list of values which implements <see cref="INotifyCollectionChanged"/> and registers changes with the <see cref="UndoManager"/>.
/// </summary>
/// <typeparam name="T"></typeparam>
public partial class UndoableObservableCollection<T> : BindableViewModel<List<T>>, IList<T>, INotifyCollectionChanged, INotifyPropertyChanged
    where T : struct
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

    /*public UndoableObservableCollection(IEnumerable<T> enumerable)
    {
        list = new(enumerable);
    }*/

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    private void OnCollectionChanged() => CollectionChanged?.Invoke(this, _collectionResetEventArgs);
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
        BoundModel?[ind] = upd;
        UndoManager.RegisterAction($"Changed {upd}", () => SetItem(old, ind), () => SetItem(upd, ind));
    }

    public void Move(int fromIndex, int toIndex)
    {
        var removedItem = list[fromIndex];

        list.RemoveAt(fromIndex);
        list.Insert(toIndex, removedItem);

        BoundModel?.RemoveAt(fromIndex);
        BoundModel?.Insert(toIndex, removedItem);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemMoved(removedItem, fromIndex, toIndex);

        UndoManager.RegisterAction($"Moved {removedItem}", () => Move(toIndex, fromIndex), () => Move(fromIndex, toIndex));
    }

    public void Insert(int index, T item)
    {
        list.Insert(index, item);

        BoundModel?.Insert(index, item);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, item, index);

        UndoManager.RegisterAction($"Added {item}", () => RemoveAt(index), () => Insert(index, item));
    }

    public void RemoveAt(int index)
    {
        var item = list[index];
        list.RemoveAt(index);

        BoundModel?.RemoveAt(index);

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

        BoundModel?.RemoveAt(index);

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

        BoundModel?.RemoveRange(index, count);

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Remove, removed, index);

        UndoManager.RegisterAction($"Removed {count} items", () => AddRange(removed), () => RemoveLast(count));
    }

    public void Add(T item)
    {
        int index = list.Count;
        list.Add(item);

        BoundModel?.Add(item);

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

        BoundModel?.AddRange(list.AsSpan(index, added));

        OnPropertyChanged(_countChangedEventArgs);
        OnPropertyChanged(_indexerChangedEventArgs);
        OnItemChanged(NotifyCollectionChangedAction.Add, items, index);

        UndoManager.RegisterAction($"Added {added} items", () => RemoveLast(added), () => AddRange(items));
    }

    public void Clear()
    {
        var oldItems = list.ToArray();
        list.Clear();

        BoundModel?.Clear();

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

    #region Model Sync
    /// <summary>
    /// Resynchronises all the cues in this <see cref="VisualCueList"/> to the bound model 
    /// (set with <see cref="Bind(List{Cue})"/>).
    /// </summary>
    public override void SyncToModel()
    {
        if (boundModel == null)
            return;
        base.SyncToModel();

        CollectionsMarshal.SetCount(boundModel, list.Count);
        var dst = CollectionsMarshal.AsSpan(boundModel);
        list.CopyTo(dst);
    }

    /// <summary>
    /// Syncronises the contents of this CueList with the cue models in the bound model 
    /// (set via <see cref="Bind(List{Cue})"/>).
    /// </summary>
    public override void SyncFromModel()
    {
        base.SyncFromModel();

        if (boundModel == null)
            return;

        CollectionsMarshal.SetCount(list, boundModel.Count);
        var dst = CollectionsMarshal.AsSpan(list);
        boundModel.CopyTo(dst);

        OnCollectionChanged();
    }
    #endregion
}
