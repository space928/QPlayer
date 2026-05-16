using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Models;
using QPlayer.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace QPlayer.ViewModels;

/// <summary>
/// The base class for a hierarchical list of cues.
/// </summary>
public class AbstractCueList : ObservableObject, IReadOnlyCollection<CueViewModel>
{
    protected internal readonly List<CueViewModel> rootCueList = [];
    protected internal List<Cue>? boundModel = [];

    /// <summary>
    /// The number of cues at the root of the cue list (ie: not counting sub-cues)
    /// </summary>
    public int Count => rootCueList.Count;

    /// <summary>
    /// Gets a root cue by index.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public CueViewModel this[int index]
    {
        get => rootCueList[index];
    }

    /// <summary>
    /// Inserts a cue at the given index in the root cue list.
    /// </summary>
    /// <param name="index"></param>
    /// <param name="item"></param>
    /// <returns></returns>
    protected internal bool Insert(int index, CueViewModel item)
    {
        var list = rootCueList;
        var model = boundModel;
        if (index < 0 || index > list.Count)
            return false;
        if (index == list.Count)
        {
            list.Add(item);
            model?.Add(item.BoundModel!);
        }
        else
        {
            list.Insert(index, item);
            model?.Insert(index, item.BoundModel!);
        }
        //CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Add, item, index));
        //OnPropertyChanged(nameof(Count));

        return true;
    }

    /// <summary>
    /// Inserts a range of ordered cues at the specified index in the root cue list.
    /// </summary>
    /// <param name="index"></param>
    /// <param name="items"></param>
    /// <returns></returns>
    protected internal bool Insert(int index, IEnumerable<CueViewModel> items)
    {
        var list = rootCueList;
        var model = boundModel;
        if (index < 0 || index > list.Count)
            return false;

        list.InsertRange(index, items);
        model?.InsertRange(index, items.Select(x => x.BoundModel!));

        //CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Add, item, index));
        //OnPropertyChanged(nameof(Count));

        return true;
    }

    /// <summary>
    /// Removes a cue by index from the root cue list.
    /// </summary>
    /// <param name="index"></param>
    /// <returns>The cue that was removed or <see langword="null"/> if the index was invalid.</returns>
    protected internal CueViewModel? Remove(int index)
    {
        var list = rootCueList;
        var model = boundModel;
        if (index < 0 || index >= list.Count)
            return null;

        var item = list[index];

        list.RemoveAt(index);
        model?.RemoveAt(index);

        //CollectionChanged?.Invoke(this, new(NotifyCollectionChangedAction.Remove, item, index));
        //OnPropertyChanged(nameof(Count));

        return item;
    }

    /// <summary>
    /// Removes a range of cues by index from the root cue list.
    /// </summary>
    /// <param name="indices">The enumerable of indices to remove.</param>
    /// <param name="returnRemoved">Whether the list of removed cue instances should be collected and returned.</param>
    /// <param name="isSorted">If the <paramref name="indices"/> is already sorted in ascending order, 
    /// then this skips needing to copy and sort the indices before use.</param>
    /// <returns>An array of cues that were removed or an empty array if none were removed. 
    /// Note, that group cues are not enumerated in this array.</returns>
    protected internal CueViewModel[] Remove(IEnumerable<int> indices, bool returnRemoved = true, bool isSorted = false)
    {
        var list = rootCueList;
        var model = boundModel;

        using TemporaryList<CueViewModel> results = default;

        var inds = indices;
        TemporaryList<int> tl = default;
        if (!isSorted)
        {
            tl = indices.ToTempList();
            tl.Sort();
            inds = tl;
        }
        inds = inds.FastReverse();

        foreach (var ind in indices)
        {
            if (ind < 0 || ind > list.Count)
                continue;

            if (returnRemoved)
                results.Add(list[ind]);

            list.RemoveAt(ind);
            model?.RemoveAt(ind);
        }

        tl.Dispose();

        if (returnRemoved)
            return results.ToArray();
        else
            return [];
    }

    protected internal virtual void Clear()
    {
        rootCueList.Clear();
        boundModel?.Clear();
    }

    /// <summary>
    /// Recursively enumerates all root cues and sub cues in this cue list in depth-first order.
    /// </summary>
    /// <returns>An iterator which enumerates each cue in order.</returns>
    protected internal IEnumerable<CueViewModel> EnumerateAll()
    {
        foreach (var cue in rootCueList)
        {
            yield return cue;
            if (cue is GroupCueViewModel group)
            {
                var children = group.Cues.EnumerateAll();
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Enumerates all the visible cues (and subcues) in this cue list. 
    /// Cues belonging to collapsed groups will not be enumerated.
    /// See also <seealso cref="CueList.VisualCues"/> for more efficient visual 
    /// cue enumeration.
    /// </summary>
    /// <returns></returns>
    protected internal IEnumerable<CueViewModel> EnumerateVisible()
    {
        foreach (var cue in rootCueList)
        {
            yield return cue;
            if (cue is GroupCueViewModel group && !group.IsCollapsed)
            {
                var children = group.Cues.EnumerateVisible();
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Enumerates all the visible cues (and subcues) in this cue list as well as their <see cref="CuePosition"/>.
    /// </summary>
    /// <param name="parent">The group cue which is the parent of this <see cref="AbstractCueList"/> or <see langword="null"/>.</param>
    /// <returns></returns>
    protected internal IEnumerable<(CueViewModel cue, CuePosition pos)> EnumerateVisiblePositions(GroupCueViewModel? parent = null)
    {
        int i = 0;
        foreach (var cue in rootCueList)
        {
            yield return (cue, new(i++, parent));
            if (cue is GroupCueViewModel group && !group.IsCollapsed)
            {
                var children = group.Cues.EnumerateVisiblePositions(group);
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    public IEnumerator<CueViewModel> GetEnumerator() => rootCueList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => rootCueList.GetEnumerator();

    /// <summary>
    /// Binds this <see cref="CueList"/> to the given model list. Changes to this list will 
    /// automatically be synchronised to the bound model.
    /// </summary>
    /// <param name="model"></param>
    public void Bind(List<Cue> model)
    {
        boundModel = model;
    }

    /// <summary>
    /// Resynchronises all the cues in this <see cref="CueList"/> to the bound model 
    /// (set with <see cref="Bind(List{Cue})"/>).
    /// </summary>
    public void SyncToModel()
    {
        if (boundModel == null)
            return;

        CollectionsMarshal.SetCount(boundModel, rootCueList.Count);
        var dst = CollectionsMarshal.AsSpan(boundModel);
        for (int i = 0; i < rootCueList.Count; i++)
        {
            CueViewModel? cue = rootCueList[i];
            dst[i] = cue.BoundModel!;
        }
    }

    /// <summary>
    /// Syncronises the contents of this CueList with the cue models in the bound model 
    /// (set via <see cref="Bind(List{Cue})"/>).
    /// </summary>
    /// <param name="convertCue"></param>
    public virtual void SyncFromModel(Func<Cue, CueViewModel?> convertCue)
    {
        if (boundModel == null)
            return;

        CollectionsMarshal.SetCount(rootCueList, boundModel.Count);
        var dst = CollectionsMarshal.AsSpan(rootCueList);
        for (int i = 0; i < boundModel.Count; i++)
        {
            var cue = boundModel[i];
            var cueVm = convertCue(cue);
            if (cueVm == null)
                continue;
            dst[i] = cueVm;
            if (cueVm is GroupCueViewModel group)
                group.Cues.SyncFromModel(convertCue);
        }
    }

    public delegate ValueTask<CueViewModel?> ConvertCueDelegate(Cue src);

    /// <inheritdoc cref="SyncFromModel(Func{Cue, CueViewModel?})"/>
    public virtual async Task SyncFromModelAsync(ConvertCueDelegate convertCue)
    {
        if (boundModel == null)
            return;

        CollectionsMarshal.SetCount(rootCueList, boundModel.Count);
        for (int i = 0; i < boundModel.Count; i++)
        {
            var cue = boundModel[i];
            var cueVm = await convertCue(cue);
            if (cueVm == null)
                continue;
            rootCueList[i] = cueVm;
            if (cueVm is GroupCueViewModel group)
                await group.Cues.SyncFromModelAsync(convertCue);
        }
    }

    /// <summary>
    /// Returns an <see cref="IEnumerable{T}"/> for the contents of this cue list.
    /// </summary>
    /// <param name="mode"></param>
    /// <returns></returns>
    public virtual IEnumerable<CueViewModel> Enumerate(EnumerationMode mode)
    {
        return mode switch
        {
            EnumerationMode.Root => rootCueList,
            EnumerationMode.Visible => EnumerateVisible(),
            EnumerationMode.All => EnumerateAll(),
            _ => EnumerateAll()
        };
    }

    public IEnumerator<CueViewModel> GetEnumerator(EnumerationMode mode) => Enumerate(mode).GetEnumerator();

    /// <summary>
    /// Represents the hierarchical position of a cue in the cue stack.
    /// </summary>
    /// <param name="index">The index of the cue in the stack (or sub-stack).</param>
    /// <param name="group">The group cue this cue belongs to or <see langword="null"/> if this cue is part of the root cue list.</param>
    public readonly struct CuePosition(int index, GroupCueViewModel? group)
    {
        public readonly int index = index;
        public readonly GroupCueViewModel? group = group;
    }

    /// <summary>
    /// A comparer which orders cues by their <see cref="CueViewModel.QID"/>.
    /// </summary>
    public readonly struct QIDComparer : IComparer<CueViewModel>
    {
        public readonly int Compare(CueViewModel? x, CueViewModel? y)
        {
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            return x.QID.CompareTo(y.QID);
        }
    }

    public enum EnumerationMode
    {
        /// <summary>
        /// All cues and sub-cues (cues belonging to groups) will enumerated recursively.
        /// </summary>
        All,
        /// <summary>
        /// Only the visible cues (ie: all cues, except those which belong to collapsed groups) will be enumerated.
        /// </summary>
        Visible,
        /// <summary>
        /// Only the cues in the top-level of the cue hierarchy (ie: don't belong to a group) will be enumerated.
        /// </summary>
        Root
    }
}

/// <summary>
/// A hierarchical cue list which belongs to a group cue.
/// </summary>
public class SubCueList : AbstractCueList
{

}

/// <summary>
/// A hierarchical cue list.
/// </summary>
public class CueList : AbstractCueList, INotifyCollectionChanged
{
    private readonly List<CueViewModel> visualCues = [];
    private readonly List<CuePosition> visualPositions = [];
    private readonly Dictionary<CueViewModel, (CuePosition pos, int visPos)> positionCache = [];
    private readonly VisualCueList visualCueList;

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    private static readonly PropertyChangedEventArgs CountPropertyChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerPropertyChanged = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs ResetCollectionChanged = new(NotifyCollectionChangedAction.Reset);

    /// <summary>
    /// An enumerable cue list of only the visible cues in the cue list.
    /// </summary>
    public VisualCueList VisualCues => visualCueList;

    public CueList() : base()
    {
        visualCueList = new(this);
    }

    private void OnCollectionChanged()
    {
        CollectionChanged?.Invoke(this, ResetCollectionChanged);
        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, object? changedItem, int index)
    {
        CollectionChanged?.Invoke(this, new(action, changedItem, index));
        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);
    }

    private void OnCollectionChanged(NotifyCollectionChangedAction action, object[]? changedItems)
    {
        CollectionChanged?.Invoke(this, new(action, changedItems));
        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);
    }

    internal CueViewModel GetVisualCue(int index) => visualCues[index];

    /// <summary>
    /// Finds the index of the specified cue in the visual cue list, or <c>-1</c> if the cue does not exist in the visual list.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    public int FindVisualIndex(CueViewModel cue)
    {
        /*if (assumeSorted)
            return Math.Max(-1, visualCues.BinarySearch(item, default(QIDComparer)));
        else
            return visualCues.IndexOf(item);*/

        if (positionCache.TryGetValue(cue, out var x))
            return x.visPos;
        return -1;
    }

    /// <summary>
    /// Finds the <see cref="CuePosition"/> of the specified cue in the cue list.
    /// </summary>
    /// <param name="cue"></param>
    /// <param name="position"></param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool Find(CueViewModel cue, out CuePosition position)
    {
        if (positionCache.TryGetValue(cue, out var x))
        {
            position = x.pos;
            return true;
        }
        position = default;
        return false;
    }

    /// <summary>
    /// Finds the <see cref="CuePosition"/> of the specified cue by it's visual index in the cue list.
    /// </summary>
    /// <param name="visualIndex"></param>
    /// <param name="position"></param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool Find(int visualIndex, out CuePosition position)
    {
        position = default;
        if (visualIndex < 0 || visualIndex >= visualCues.Count)
            return false;
        position = visualPositions[visualIndex];
        return true;
    }

    private IEnumerable<CueViewModel> GetCues(IEnumerable<CuePosition> positions)
    {
        foreach (var pos in positions)
        {
            var src = pos.group?.Cues?.rootCueList ?? rootCueList;
            if (pos.index < 0 || pos.index >= src.Count)
                continue;
            yield return src[pos.index];
        }
    }

    private IEnumerable<CuePosition> GetPositions(IEnumerable<CueViewModel> cues)
    {
        foreach (var cue in cues)
        {
            if (Find(cue, out var pos))
                yield return pos;
        }
    }

    private IEnumerable<CuePosition> GetPositions(IEnumerable<int> visualInds)
    {
        return visualInds.Select(ind => visualPositions[ind]);
    }

    private void OnVisualGroupCollapsed(GroupCueViewModel group, bool isCollapsed)
    {
        var startInd = FindVisualIndex(group);
        if (startInd == -1)
        {
            MainViewModel.Log($"Tried to collapse/expand group which is not in the cue list!", MainViewModel.LogLevel.Warning);
            return;
        }
        startInd++;

        if (isCollapsed)
        {
            // Remove this group's contents from the visual list
            var toRemove = group.Cues.EnumerateVisible().Count();
            // Unsubscribe from subgroups and remove from cache
            for (int i = startInd; i < startInd + toRemove; i++)
            {
                var cue = visualCues[i];
                if (cue is GroupCueViewModel subgroup)
                    subgroup.OnCollapse -= OnVisualGroupCollapsed;
                positionCache.Remove(cue);
            }
            // Remove from visual list
            visualCues.RemoveRange(startInd, toRemove);
            visualPositions.RemoveRange(startInd, toRemove);
        }
        else
        {
            // Collect the visible subcues
            using var subcues = group.Cues.EnumerateVisiblePositions(group).ToTempList();
            using var subcueInst = subcues.Select(x => x.cue).ToTempList();
            using var subcuePos = subcues.Select(x => x.pos).ToTempList();

            // Insert them into the visible list and subscribe to events
            visualCues.InsertRange(startInd, subcueInst);
            visualPositions.InsertRange(startInd, subcuePos);
            int i = startInd;
            foreach (var (cue, pos) in subcues)
            {
                positionCache.Add(cue, (pos, i));
                if (cue is GroupCueViewModel subgroup)
                    subgroup.OnCollapse += OnVisualGroupCollapsed;
                i++;
            }
        }
    }

    protected internal CueViewModel? Delete(int visualIndex)
    {
        if (!Find(visualIndex, out var pos))
            return null;

        return Delete(pos);
    }
    protected internal bool Delete(CueViewModel cue)
    {
        if (!Find(cue, out var pos))
            return false;

        return Delete(pos) != null;
    }
    protected internal CueViewModel[] Delete(IEnumerable<CueViewModel> cues, bool collectResults = true) => Delete(GetPositions(cues), collectResults);

    protected internal CueViewModel[] Delete(IEnumerable<int> visualIndices, bool collectResults = true) => Delete(GetPositions(visualIndices), collectResults);

    protected internal CueViewModel[] Delete(IEnumerable<CuePosition> cues, bool collectResults = true, bool needsSorting = true)
    {
        // using var deleted = GetCues(cues).ToTempList();

        using var cuesList = cues.ToTempList();
        // using var removedInds = new TemporaryList<int>();
        var removedItems = new TemporaryList<CueViewModel>();

        if (!needsSorting)
            cuesList.Sort(default(CuePositionComparer));

        ref var removedItemsRef = ref (collectResults ? ref removedItems : ref Unsafe.NullRef<TemporaryList<CueViewModel>>());
        foreach (var pos in cuesList.FastReverse())
        {
            AbstractCueList list = this;
            if (pos.group != null)
                list = pos.group.Cues;

            if (list.Remove(pos.index) is not CueViewModel removed)
                continue;

            if (collectResults)
                removedItems.Add(removed);

            if (removed is GroupCueViewModel group && !group.IsCollapsed)
            {
                RemoveGroupCueContents(group, ref removedItemsRef);
                group.OnCollapse -= OnVisualGroupCollapsed;
            }

            // Update the visual list
            if (positionCache.Remove(removed, out var oldPos) && oldPos.visPos != -1)
            {
                visualCues.RemoveAt(oldPos.visPos);
                visualPositions.RemoveAt(oldPos.visPos);
                // removedInds.Add(oldPos.visPos);
            }
        }

        if (collectResults)
        {
            var removedArr = removedItems.ToArray();
            removedItems.Dispose();
            OnCollectionChanged(NotifyCollectionChangedAction.Remove, removedArr);
            return removedArr;
        }
        else
        {
            OnCollectionChanged();
            return [];
        }
    }

    protected internal CueViewModel? Delete(CuePosition cue)
    {
        AbstractCueList list = this;
        if (cue.group != null)
            list = cue.group.Cues;

        if (list.Remove(cue.index) is not CueViewModel removed)
            return null;

        if (removed is GroupCueViewModel group && !group.IsCollapsed)
        {
            RemoveGroupCueContents(group, ref Unsafe.NullRef<TemporaryList<CueViewModel>>());
            group.OnCollapse -= OnVisualGroupCollapsed;
        }

        // Update the visual list
        if (positionCache.Remove(removed, out var oldPos) && oldPos.visPos != -1)
        {
            visualCues.RemoveAt(oldPos.visPos);
            visualPositions.RemoveAt(oldPos.visPos);

            OnCollectionChanged(NotifyCollectionChangedAction.Remove, removed, oldPos.visPos);
        }

        return removed;
    }

    private void RemoveGroupCueContents(GroupCueViewModel group, ref TemporaryList<CueViewModel> removed)
    {
        using var visible = group.Cues.EnumerateVisible().ToTempList();
        foreach (var item in visible.FastReverse())
        {
            if (positionCache.Remove(item, out var oldPos) && oldPos.visPos != -1)
            {
                visualCues.RemoveAt(oldPos.visPos);
                visualPositions.RemoveAt(oldPos.visPos);
                // removedInds.Add(oldPos.visPos);
            }

            if (item is GroupCueViewModel subgroup)
                subgroup.OnCollapse -= OnVisualGroupCollapsed;
        }

        if (!Unsafe.IsNullRef(ref removed))
            removed.AddRange(visible.FastReverse());
    }

    protected internal int[] Insert(IEnumerable<int> visualIndices, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        return Insert(visualIndices.Select(x => visualPositions[x]), cues, collectResults);
    }

    protected internal int Insert(int visualIndex, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        if (visualIndex < 0 || visualIndex >= visualPositions.Count)
            return -2;
        return Insert(visualPositions[visualIndex], cues, collectResults);
    }

    protected internal new int Insert(int visualIndex, CueViewModel cue)
    {
        if (visualIndex < 0 || visualIndex >= visualPositions.Count)
            return -2;
        return Insert(visualPositions[visualIndex], cue);
    }

    protected internal int[] Insert(IEnumerable<CuePosition> positions, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        using var added = new TemporaryList<CueViewModel>();
        using var addedInds = new TemporaryList<int>();

        foreach (var (cue, pos) in cues.Zip(positions))
        {
            AbstractCueList list = this;
            if (pos.group != null)
                list = pos.group.Cues;

            if (!list.Insert(pos.index, cue))
                continue;

            // Compute visPos
            int visPos = ComputeNewVisPos(pos);
            // Update cache
            positionCache.Add(cue, (pos, visPos));

            // Update the visual list
            if (visPos != -1)
            {
                visualCues.Insert(visPos, cue);
                visualPositions.Insert(visPos, pos);
                if (collectResults)
                {
                    added.Add(cue);
                    addedInds.Add(visPos);
                }
                if (cue is GroupCueViewModel group)
                {
                    var res = added;
                    if (!collectResults)
                        res = [];
                    int n = res.Count;

                    InsertGroupCueContents(visPos + 1, group, ref res);
                    addedInds.AddRange(Enumerable.Range(visPos + 1, res.Count - n));

                    if (!collectResults)
                        res.Dispose();
                }
            }
        }
        if (collectResults)
        {
            OnCollectionChanged(NotifyCollectionChangedAction.Add, added.ToArray());
            return addedInds.ToArray();
        }

        OnCollectionChanged();
        return [];
    }

    protected internal int Insert(CuePosition pos, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        var added = new TemporaryList<CueViewModel>();

        int firstVisPos = -2;
        foreach (var cue in cues)
        {
            AbstractCueList list = this;
            if (pos.group != null)
                list = pos.group.Cues;

            if (!list.Insert(pos.index, cue))
                continue;

            // Compute visPos
            int visPos = ComputeNewVisPos(pos);
            // Update cache
            positionCache.Add(cue, (pos, visPos));
            if (firstVisPos == -2)
                firstVisPos = visPos;

            // Update the visual list
            if (visPos != -1)
            {
                visualCues.Insert(visPos, cue);
                visualPositions.Insert(visPos, pos);
                if (collectResults)
                    added.Add(cue);

                if (cue is GroupCueViewModel group)
                    InsertGroupCueContents(visPos + 1, group, ref collectResults ? ref added : ref Unsafe.NullRef<TemporaryList<CueViewModel>>());
            }

            // Increment the position
            pos = new(pos.index + 1, pos.group);
        }
        if (collectResults)
            OnCollectionChanged(NotifyCollectionChangedAction.Add, added.ToArray(), firstVisPos);
        else
            OnCollectionChanged();

        added.Dispose();
        return firstVisPos;
    }

    /// <summary>
    /// Inserts a cue into the cue stack in the specified position.
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="cue"></param>
    /// <returns>The visual index of the newly inserted cue or <c>-1</c> if it's currently hidden, or <c>-2</c> if the cue couldn't be inserted.</returns>
    protected internal int Insert(CuePosition pos, CueViewModel cue)
    {
        AbstractCueList list = this;
        if (pos.group != null)
            list = pos.group.Cues;

        if (!list.Insert(pos.index, cue))
            return -2;

        // Compute visPos
        int visPos = ComputeNewVisPos(pos);

        // Update cache
        positionCache.Add(cue, (pos, visPos));

        // Update the visual list
        if (visPos != -1)
        {
            visualCues.Insert(visPos, cue);
            visualPositions.Insert(visPos, pos);

            if (cue is GroupCueViewModel group)
            {
                TemporaryList<CueViewModel> added = [cue];
                InsertGroupCueContents(visPos + 1, group, ref added);
                OnCollectionChanged(NotifyCollectionChangedAction.Add, added.ToArray());
                added.Dispose();
            }
            else
            {
                OnCollectionChanged(NotifyCollectionChangedAction.Add, cue, visPos);
            }
        }

        return visPos;
    }

    private int ComputeNewVisPos(CuePosition pos)
    {
        int visPos = -1;
        if (pos.index != 0)
        {
            AbstractCueList list = (pos.group?.Cues as AbstractCueList) ?? this;
            var prev = list[pos.index - 1];
            int prevPos = FindVisualIndex(prev);
            if (prevPos != -1)
                visPos = prevPos + 1;
        }
        else
        {
            var prev = pos.group;
            if (prev == null)
                visPos = pos.index;
            else
            {
                int prevPos = FindVisualIndex(prev);
                if (prevPos != -1)
                    visPos = prevPos + 1;
            }
        }
        return visPos;
    }

    private void InsertGroupCueContents(int visualIndex, GroupCueViewModel group, ref TemporaryList<CueViewModel> inserted)
    {
        using var visible = group.Cues.EnumerateVisiblePositions().ToTempList();
        foreach (var (cue, pos) in visible)
        {
            positionCache.Add(cue, (pos, visualIndex));

            // Update the visual list
            visualCues.Insert(visualIndex, cue);
            visualPositions.Insert(visualIndex, pos);

            if (cue is GroupCueViewModel subgroup)
                subgroup.OnCollapse += OnVisualGroupCollapsed;
        }

        if (!Unsafe.IsNullRef(ref inserted))
            inserted.AddRange(visible.Select(x => x.cue));
    }

    private void ResetVisualCache()
    {
        // Unsubscribe old event listeners
        foreach (var oldGroup in visualCues.OfType<GroupCueViewModel>())
            oldGroup.OnCollapse -= OnVisualGroupCollapsed;

        // Clear the visual cache
        visualCues.Clear();
        visualPositions.Clear();
        positionCache.Clear();

        // Re-create the visual cue list from scratch
        int visPos = 0;
        foreach (var (cue, pos) in EnumerateVisiblePositions())
        {
            visualCues.Add(cue);
            visualPositions.Add(pos);
            positionCache.Add(cue, (pos, visPos));
            if (cue is GroupCueViewModel subgroup)
                subgroup.OnCollapse += OnVisualGroupCollapsed;

            visPos++;
        }

        OnCollectionChanged();
    }

    // These are all extensions of Insert and Delete
    /*public void Duplicate() { }

    public void Move() { }

    public void Create() { }*/


    protected internal override void Clear()
    {
        base.Clear();
        ResetVisualCache();
    }

    public bool Contains(CueViewModel item) => Find(item, out _);

    public override IEnumerable<CueViewModel> Enumerate(EnumerationMode mode)
    {
        return mode switch
        {
            EnumerationMode.Root => rootCueList,
            EnumerationMode.Visible => visualCues,
            EnumerationMode.All => EnumerateAll(),
            _ => EnumerateAll()
        };
    }

    public override void SyncFromModel(Func<Cue, CueViewModel?> convertCue)
    {
        base.SyncFromModel(convertCue);
        ResetVisualCache();
    }

    public override async Task SyncFromModelAsync(ConvertCueDelegate convertCue)
    {
        await base.SyncFromModelAsync(convertCue);
        ResetVisualCache();
    }

    public readonly struct CuePositionComparer : IComparer<CuePosition>
    {
        public readonly int Compare(CuePosition x, CuePosition y)
        {
            if (x.group == null && y.group != null)
                return 1;
            if (y.group == null && x.group != null)
                return -1;

            return x.index.CompareTo(y.index);
        }
    }

    public struct VisualCueList : IReadOnlyList<CueViewModel>, INotifyCollectionChanged, INotifyPropertyChanged
    {
        private readonly CueList list;

        public readonly int Count => list.Count;

        public readonly CueViewModel this[int index] => list.GetVisualCue(index);

        public event NotifyCollectionChangedEventHandler? CollectionChanged;
        public event PropertyChangedEventHandler? PropertyChanged;

        internal VisualCueList(CueList list)
        {
            this.list = list;
            list.CollectionChanged += List_CollectionChanged;
        }

        private readonly void List_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            CollectionChanged?.Invoke(sender, e);
        }

        public readonly IEnumerator<CueViewModel> GetEnumerator() => list.GetEnumerator(AbstractCueList.EnumerationMode.Visible);
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

