using CommunityToolkit.Mvvm.ComponentModel;
using QPlayer.Models;
using QPlayer.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
    private readonly List<CueViewModel> rootCueList = [];
    protected readonly HashSet<GroupCueViewModel> groups = [];
    protected internal List<Cue>? boundModel = [];
    /// <summary>
    /// Stores the total number of cues in this list. This number is set by the <see cref="CueList"/> at the root of the hierarchy.
    /// </summary>
    protected int totalCount = 0;

    /// <summary>
    /// This list of cues managed by this cue list, doesn't contain any cues belonging to child groups or parents.
    /// </summary>
    internal ReadOnlyCollection<CueViewModel> RootCueList { get; init; }
    /// <summary>
    /// The group cue which owns this cue list or <see langword="null"/> if this cue list is not owned by a group (or is the root cue list).
    /// </summary>
    internal virtual GroupCueViewModel? OwnerGroup => null;

    /// <summary>
    /// The number of cues at the root of the cue list (ie: not counting sub-cues)
    /// </summary>
    public virtual int Count => rootCueList.Count;
    /// <summary>
    /// The total number of cues in the cue list.
    /// </summary>
    public int TotalCount => totalCount;

    public bool IsEmpty => rootCueList.Count == 0;

    /// <summary>
    /// Gets a cue by position.
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    public CueViewModel this[CuePosition pos]
    {
        get => pos.group != null ? pos.group.Cues[pos.index] : RootCueList[pos.index];
    }

    /// <summary>
    /// Gets a root cue by index.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public virtual CueViewModel this[int index]
    {
        get => rootCueList[index];
    }

    public AbstractCueList()
    {
        RootCueList = rootCueList.AsReadOnly();
    }

    /// <summary>
    /// Finds the <see cref="CuePosition"/> of the specified cue in the cue list.
    /// </summary>
    /// <param name="cue"></param>
    /// <param name="position"></param>
    /// <param name="defaultGroup">Used internally.</param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool Find(CueViewModel cue, out CuePosition position, GroupCueViewModel? defaultGroup = null)
    {
        // TODO: There isn't really a good way to make this more efficient, which unfortunately affects the
        // performance of many methods that depend on this one. If testing indicates that this is a performance
        // bottleneck, then maybe we could consider building an index cache. This cache would probably only be
        // generated on save and would become invalid as soon as this cue list is mutated. I can't think of a
        // way to keep an index cache up-to-date for cheap (even if it's just to get an approximately correct
        // index).
        int ind = rootCueList.IndexOf(cue);
        if (ind == -1)
        {
            foreach (var group in groups)
            {
                var res = group.Cues.Find(cue, out position, group);
                if (res)
                    return true;
            }
            position = default;
            return false;
        }

        position = new(ind, defaultGroup);
        return true;
    }

    private void IncrementTotalCount(int delta)
    {
        if (delta == 0)
            return;

        totalCount += delta;
        CueViewModel? parent = OwnerGroup;
        if (parent != null)
        {
            while (parent.Parent is GroupCueViewModel group)
            {
                group.Cues.totalCount += delta;
                parent = group;
            }
            parent.MainViewModel.Cues.totalCount += delta;
        }
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
        int added = 1;
        if (item is GroupCueViewModel group)
        {
            groups.Add(group);
            added += group.Cues.totalCount;
        }

        item.Parent = OwnerGroup;
        IncrementTotalCount(added);

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

        int added = 0;
        foreach (var item in items)
        {
            added++;
            if (item is GroupCueViewModel group)
            {
                groups.Add(group);
                added += group.Cues.totalCount;
            }
            item.Parent = OwnerGroup;
        }
        IncrementTotalCount(added);

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

        int removed = 1;
        if (item is GroupCueViewModel group)
        {
            groups.Remove(group);
            removed += group.Cues.totalCount;
        }
        IncrementTotalCount(-removed);
        UndoManager.SuppressRecording();
        item?.Parent = null;
        UndoManager.UnSuppressRecording();

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

        int removed = 0;
        foreach (var ind in indices)
        {
            if (ind < 0 || ind > list.Count)
                continue;

            var item = list[ind];
            if (returnRemoved)
                results.Add(item);

            list.RemoveAt(ind);
            model?.RemoveAt(ind);

            removed++;
            if (item is GroupCueViewModel group)
            {
                groups.Remove(group);
                removed += group.Cues.totalCount;
            }
            UndoManager.SuppressRecording();
            item?.Parent = null;
            UndoManager.UnSuppressRecording();
        }
        IncrementTotalCount(-removed);

        tl.Dispose();

        if (returnRemoved)
            return results.ToArray();
        else
            return [];
    }

    /// <summary>
    /// Removes all cues from the root cue list.
    /// </summary>
    protected internal virtual void Clear()
    {
        UndoManager.SuppressRecording();
        foreach (var cue in rootCueList)
            cue.Parent = null;
        UndoManager.UnSuppressRecording();

        IncrementTotalCount(-totalCount);
        rootCueList.Clear();
        boundModel?.Clear();
        groups?.Clear();
    }

    /// <summary>
    /// Recursively enumerates all root cues and sub cues in this cue list in depth-first order.
    /// </summary>
    /// <returns>An iterator which enumerates each cue in order.</returns>
    protected internal IEnumerable<CueViewModel> EnumerateAll()
    {
        if (groups.Count == 0)
            return rootCueList;

        return EnumerateAllInternal();
    }

    private IEnumerable<CueViewModel> EnumerateAllInternal()
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
        if (groups.Count == 0)
            return rootCueList;
        return EnumerateVisibleInternal();
    }

    private IEnumerable<CueViewModel> EnumerateVisibleInternal()
    {
        foreach (var cue in rootCueList)
        {
            yield return cue;
            if (cue is GroupCueViewModel group && !group.IsCollapsed)
            {
                var children = group.Cues.EnumerateVisibleInternal();
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    /// <summary>
    /// Enumerates all the visible cues (and subcues) in this cue list as well as their <see cref="CuePosition"/>.
    /// </summary>
    /// <returns></returns>
    protected internal IEnumerable<(CueViewModel cue, CuePosition pos)> EnumerateVisiblePositions()
    {
        int i = 0;
        var parent = OwnerGroup;
        if (parent?.IsCollapsed ?? false)
            yield break;

        foreach (var cue in rootCueList)
        {
            yield return (cue, new(i++, parent));
            if (cue is GroupCueViewModel group && !group.IsCollapsed)
            {
                var children = group.Cues.EnumerateVisiblePositions();
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    public virtual IEnumerator<CueViewModel> GetEnumerator() => rootCueList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

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

    internal readonly struct CuePositionRangeEnumerable(GroupCueViewModel? group, int startInd, int count) : IEnumerable<CuePosition>, IList<CuePosition>, IReadOnlyList<CuePosition>
    {
        public CuePosition this[int index] { get => throw new NotImplementedException(); set => throw new InvalidOperationException(); }

        public int Count => count;
        public bool IsReadOnly => true;

        public IEnumerator<CuePosition> GetEnumerator() => new CuePositionRangeEnumerator(group, startInd, count);
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public void CopyTo(CuePosition[] array, int arrayIndex)
        {
            int j = startInd;
            for (int i = 0; i < count; i++)
                array[i + arrayIndex] = new(j++, group);
        }

        public bool Contains(CuePosition item) => item.group == group && item.index >= startInd && item.index < startInd + count;
        public int IndexOf(CuePosition item) => Contains(item) ? item.index - startInd : -1;

        public void Add(CuePosition item) => throw new InvalidOperationException();
        public void Clear() => throw new InvalidOperationException();
        public void Insert(int index, CuePosition item) => throw new InvalidOperationException();
        public bool Remove(CuePosition item) => throw new InvalidOperationException();
        public void RemoveAt(int index) => throw new InvalidOperationException();

        internal struct CuePositionRangeEnumerator(GroupCueViewModel? group, int startInd, int count) : IEnumerator<CuePosition>
        {
            private int pos = -1;
            private readonly int end = startInd + count - 1;
            private readonly GroupCueViewModel? group = group;
            private readonly int startInd = startInd;

            public readonly CuePosition Current => new(pos, group);

            readonly object IEnumerator.Current => Current;

            public readonly void Dispose() { }

            public bool MoveNext()
            {
                if (pos == -1)
                    pos = startInd;
                else
                    pos++;
                return pos < end;
            }

            public void Reset()
            {
                pos = -1;
            }
        }
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
    private readonly GroupCueViewModel? ownerGroup;
    public SubCueList(GroupCueViewModel? ownerGroup) : base()
    {
        this.ownerGroup = ownerGroup;
    }

    internal override GroupCueViewModel? OwnerGroup => ownerGroup;

    /// <summary>
    /// Shuffles the contents of this subgroup by deleting and re-inserting them.
    /// </summary>
    internal void Shuffle()
    {
        if (ownerGroup == null)
            return;
        var mainList = ownerGroup.MainViewModel.Cues;
        var cues = mainList.Delete(new CuePositionRangeEnumerable(ownerGroup, 0, Count), false);
        Random.Shared.Shuffle(cues);
        mainList.Insert(new CuePosition(0, ownerGroup), cues, false);
    }
}

/// <summary>
/// A hierarchical cue list.
/// </summary>
public class CueList : AbstractCueList, INotifyCollectionChanged
{
    private readonly List<CueViewModel> visualCues = [];
    //private readonly List<CuePosition> visualPositions = [];
    //private readonly Dictionary<CueViewModel, (CuePosition pos, int visPos)> positionCache = [];
    private readonly VisualCueList visualCueList;
    private readonly MultiDict<decimal, CueViewModel> cuesDict = [];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;

    private static readonly PropertyChangedEventArgs CountPropertyChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerPropertyChanged = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs ResetCollectionChanged = new(NotifyCollectionChangedAction.Reset);

    /// <summary>
    /// An enumerable cue list of only the visible cues in the cue list.
    /// </summary>
    public VisualCueList VisualCues => visualCueList; // ---> remove this?? to make things easier, the CueList effectively just implements this (overriding the AbstractCueList behaviour)

    public CueList() : base()
    {
        visualCueList = new(this);
    }

    /// <summary>
    /// The number of cues visible in the cue list.
    /// </summary>
    public override int Count => visualCues.Count;

    /// <summary>
    /// Gets a visual cue by index.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public override CueViewModel this[int index]
    {
        get => visualCues[index];
    }

    /// <summary>
    /// Informs the cue stack that the cue ID of a given cue view model has been changed. This should be called whenever a QID is changed.
    /// <para/>
    /// Note that since <see cref="CueViewModel.QID"/>'s setter calls this method, users which change QID's through this 
    /// setter need not call this method.
    /// </summary>
    /// <param name="oldVal"></param>
    /// <param name="newVal"></param>
    /// <param name="src"></param>
    internal void NotifyQIDChanged(decimal oldVal, decimal newVal, CueViewModel src)
    {
        if (!cuesDict.UpdateKey(oldVal, newVal, src))
            cuesDict.Add(newVal, src);
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

    private void OnCollectionChanged(NotifyCollectionChangedAction action, IList? changedItems, int startIndex)
    {
        CollectionChanged?.Invoke(this, new(action, changedItems, startIndex));
        OnPropertyChanged(CountPropertyChanged);
        OnPropertyChanged(IndexerPropertyChanged);
    }

    #region Getters
    /// <summary>
    /// Checks if the cue at the given visual index is the last cue in a group.
    /// </summary>
    /// <param name="visualPos"></param>
    /// <returns></returns>
    internal bool IsLastInGroup(int visualPos)
    {
        if (visualPos >= visualCues.Count)
            return true;  // Last cue in the stack must be at the end
        if (visualPos < 0)
            return false;  // Not in cue stack

        var cur = visualCues[visualPos];
        var next = visualCues[visualPos + 1];
        // A change in parent indicates that this must be the last cue in this group
        // unless the next parent is the current cue, in which case it's the start
        // of a group.
        if (cur.Parent != next.Parent && next.Parent != cur)
            return true;
        return false;
    }

    internal CueViewModel GetVisualCue(int index) => visualCues[index];

    /// <summary>
    /// Finds the index of the specified cue in the visual cue list, or <c>-1</c> if the cue does not exist in the visual list.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    public int FindVisualIndex(CueViewModel cue)
    {
        return visualCues.IndexOf(cue);
    }

    /// <inheritdoc cref="FindVisualIndex(CueViewModel)"/>
    public int FindVisualIndex(CuePosition pos)
    {
        var cue = this[pos];
        return FindVisualIndex(cue);
    }

    /// <inheritdoc cref="FindVisualIndex(CuePosition)"/>
    public bool FindVisualIndex(CuePosition pos, out int visualIndex)
    {
        visualIndex = FindVisualIndex(pos);
        return visualIndex != -1;
    }

    /// <inheritdoc cref="FindVisualIndex(CueViewModel)"/>
    public bool FindVisualIndex(CueViewModel cue, out int visualIndex)
    {
        visualIndex = FindVisualIndex(cue);
        return visualIndex != -1;
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
        return Find(visualCues[visualIndex], out position);
    }

    /// <summary>
    /// Tries to find a cue view model given a cue ID.
    /// </summary>
    /// <param name="id">The cue ID to search for.</param>
    /// <param name="cue">The returned cue view model if it was found.</param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool Find(decimal id, [NotNullWhen(true)] out CueViewModel? cue)
    {
        return cuesDict.TryGetValue(id, out cue);
    }

    /// <summary>
    /// Tries to find a cue's <see cref="CuePosition"/> given a cue ID.
    /// </summary>
    /// <param name="id">The cue ID to search for.</param>
    /// <param name="pos">The position of the cue in the cue stack if it was found.</param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool Find(decimal id, out CuePosition pos)
    {
        if (cuesDict.TryGetValue(id, out CueViewModel? val))
            return Find(val!, out pos);
        pos = default;
        return false;
    }

    /// <summary>
    /// Enumerates the cues in this list from their <see cref="AbstractCueList.CuePosition"/>s.
    /// </summary>
    /// <param name="positions"></param>
    /// <returns></returns>
    internal IEnumerable<CueViewModel> GetCues(IEnumerable<CuePosition> positions)
    {
        foreach (var pos in positions)
        {
            var src = pos.group?.Cues?.RootCueList ?? RootCueList;
            if (pos.index < 0 || pos.index >= src.Count)
                continue;
            yield return src[pos.index];
        }
    }

    /// <summary>
    /// Finds the cue positions for a range of cues.
    /// </summary>
    /// <param name="cues"></param>
    /// <returns></returns>
    internal IEnumerable<CuePosition> GetPositions(IEnumerable<CueViewModel> cues)
    {
        foreach (var cue in cues)
        {
            if (Find(cue, out var pos))
                yield return pos;
        }
    }

    /// <summary>
    /// Finds the cue positions for a range of visual indices.
    /// </summary>
    /// <remarks>
    /// The method provides an optimised path for consecutive visual indices.
    /// </remarks>
    /// <param name="visualInds"></param>
    /// <param name="allowAdd"></param>
    /// <returns></returns>
    internal IEnumerable<CuePosition> GetPositions(IEnumerable<int> visualInds, bool allowAdd = false)
    {
        int lastInd = -1;
        GroupCueViewModel? lastParent = null;
        CuePosition lastCuePos = default;
        foreach (var ind in visualInds)
        {
            CuePosition pos;
            GroupCueViewModel? parent;
            if (allowAdd && ind >= visualCues.Count) // Add
            {
                pos = new(RootCueList.Count, null);
                parent = null;
                goto Found;
            }

            parent = (ind >= 0 && ind < visualCues.Count) ? visualCues[ind].Parent as GroupCueViewModel : null;
            if (lastInd != -1 && parent == lastParent)
            {
                // Shortcut for consecutive cues
                if (ind == lastInd + 1)
                {
                    pos = new(lastCuePos.index + 1, lastCuePos.group);
                    goto Found;
                }
                else if (ind == lastInd - 1)
                {
                    pos = new(lastCuePos.index - 1, lastCuePos.group);
                    goto Found;
                }
            }
            else if (Find(ind, out pos))
            {
                goto Found;
            }

            // No cue found, skip
            continue;

        Found:
            lastInd = ind;
            lastParent = parent;
            lastCuePos = pos;
            yield return pos;
        }
    }

    /// <summary>
    /// Sorts an enumerable of cues by their visual index, returning an array of cues and an array of 
    /// corresponding visual indices in ascending order. Hidden cues are not included in the results.
    /// </summary>
    /// <param name="cues">The enumerable of cues to sort.</param>
    /// <returns></returns>
    public (int[] visualIndices, CueViewModel[] sortedCues) SortCues(IEnumerable<CueViewModel> cues)
    {
        using TemporaryList<CueViewModel> cuesList = [];
        using TemporaryList<int> inds = [];
        if (cues.TryGetNonEnumeratedCount(out var estCount))
        {
            cuesList.EnsureCapacity(estCount);
            inds.EnsureCapacity(estCount);
        }

        // Gather visual indices
        foreach (var cue in cues)
            if (FindVisualIndex(cue, out var visPos))
                inds.Add(visPos);

        // Sort them
        inds.Sort();

        // Get the corresponding visual cue index
        for (int i = 0; i < inds.Count; i++)
            cuesList.Add(visualCues[inds[i]]);

        return (inds.ToArray(), cuesList.ToArray());
    }
    #endregion

    /// <summary>
    /// Keeps the visual list up to date when a group is collapsed or expanded.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="isCollapsed"></param>
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
            }
            var removed = visualCues.Slice(startInd, toRemove);
            // Remove from visual list
            visualCues.RemoveRange(startInd, toRemove);
            OnCollectionChanged(NotifyCollectionChangedAction.Remove, removed, startInd);
        }
        else
        {
            // Collect the visible subcues
            using var subcues = group.Cues.EnumerateVisiblePositions().ToTempList();
            using var subcueInst = subcues.Select(x => x.cue).ToTempList();

            // Insert them into the visible list and subscribe to events
            visualCues.InsertRange(startInd, subcueInst);
            int i = startInd;
            foreach (var (cue, pos) in subcues)
            {
                if (cue is GroupCueViewModel subgroup)
                    subgroup.OnCollapse += OnVisualGroupCollapsed;
                i++;
            }
            OnCollectionChanged(NotifyCollectionChangedAction.Add, subcueInst.ToArray(), startInd);
        }
    }

    #region Delete Cues
    /// <summary>
    /// Deletes a cue from the cue list based on it's visual index.
    /// </summary>
    /// <param name="visualIndex"></param>
    /// <returns></returns>
    protected internal CueViewModel? Delete(int visualIndex)
    {
        if (!Find(visualIndex, out var pos))
            return null;

        return Delete(pos);
    }

    /// <summary>
    /// Deletes a cue from the cue list by instance.
    /// </summary>
    /// <param name="cue"></param>
    /// <returns><see langword="true"/> if the given cue was deleted.</returns>
    protected internal bool Delete(CueViewModel cue)
    {
        if (!Find(cue, out var pos))
            return false;

        return Delete(pos) != null;
    }

    /// <summary>
    /// Deletes a range of cues from the cue list.
    /// </summary>
    /// <param name="cues">The cues to delete.</param>
    /// <param name="collectResults">Whether the deleted cues should be collected and returned as an array.</param>
    /// <returns>An array of deleted cues, sorted by visual index.</returns>
    protected internal CueViewModel[] Delete(IEnumerable<CueViewModel> cues) => Delete(GetPositions(cues));

    /// <inheritdoc cref="Delete(IEnumerable{CueViewModel}, bool)"/>
    /// <remarks>This method takes advantage of certain optimisations to make consecutive cue deletion faster.</remarks>
    protected internal CueViewModel[] Delete(IEnumerable<int> visualIndices, bool needsSorting = true)
    {
        if (needsSorting)
        {
            using var inds = visualIndices.ToTempList();
            inds.Sort();
            return Delete(GetPositions(inds), false);
        }
        return Delete(GetPositions(visualIndices), needsSorting);
    }

    /// <inheritdoc cref="Delete(IEnumerable{CueViewModel}, bool)"/>
    protected internal CueViewModel[] Delete(IEnumerable<CuePosition> cues, bool needsSorting = true)
    {
        TemporaryList<CuePosition> cuesList = default;
        var removedItems = new TemporaryList<CueViewModel>();

        if (!needsSorting)
        {
            cuesList = new(cues);
            cuesList.Sort(default(CuePositionComparer));
            cues = cuesList;
        }

        foreach (var pos in cues.FastReverse())
        {
            AbstractCueList list = this;
            if (pos.group != null)
                list = pos.group.Cues;

            if (!FindVisualIndex(pos, out var visPos))
                continue;
            if (list.Remove(pos.index) is not CueViewModel removed)
                continue;

            RemoveCueFromDict(removed);
            removedItems.Add(removed);

            if (removed is GroupCueViewModel group)
                RemoveGroupCueContents(group, ref removedItems, true);

            // Update the visual list
            visualCues.RemoveAt(visPos);
        }

        var removedArr = removedItems.FastReverse().ToArray();
        removedItems.Dispose();
        cuesList.Dispose();
        OnCollectionChanged();
        //OnCollectionChanged(NotifyCollectionChangedAction.Remove, removedArr);
        return removedArr;
    }

    /// <summary>
    /// Deletes a cue from the cue list by position.
    /// </summary>
    /// <param name="cue"></param>
    /// <returns></returns>
    protected internal CueViewModel? Delete(CuePosition cue)
    {
        AbstractCueList list = this;
        if (cue.group != null)
            list = cue.group.Cues;

        if (!FindVisualIndex(cue, out var visPos))
            return null;
        if (list.Remove(cue.index) is not CueViewModel removed)
            return null;

        RemoveCueFromDict(removed);

        if (removed is GroupCueViewModel group)
        {
            TemporaryList<CueViewModel> groupContents = [];
            RemoveGroupCueContents(group, ref groupContents, true);

            if (groupContents.Count > 0)
            {
                visualCues.RemoveAt(visPos);
                groupContents.Add(group);
                OnCollectionChanged(NotifyCollectionChangedAction.Remove, groupContents.FastReverse(), visPos);

                return removed;
            }
        }

        // Update the visual list
        visualCues.RemoveAt(visPos);
        OnCollectionChanged(NotifyCollectionChangedAction.Remove, removed, visPos);

        return removed;
    }

    private void RemoveGroupCueContents(GroupCueViewModel group, ref TemporaryList<CueViewModel> removed, bool reverse)
    {
        group.OnCollapse -= OnVisualGroupCollapsed;
        if (group.IsCollapsed)
        {
            RemoveCuesFromDict(group.Cues.EnumerateAll());
            return;
        }

        using var visible = group.Cues.EnumerateVisible().ToTempList();
        foreach (var item in visible.FastReverse())
        {
            // Update the visual list
            if (FindVisualIndex(item, out var visPos))
            {
                visualCues.RemoveAt(visPos);
            }

            RemoveCueFromDict(item);
            if (item is GroupCueViewModel subgroup)
            {
                subgroup.OnCollapse -= OnVisualGroupCollapsed;
                if (subgroup.IsCollapsed) // Remove any hidden cues too.
                    RemoveCuesFromDict(subgroup.Cues.EnumerateAll());
            }
        }

        if (!Unsafe.IsNullRef(ref removed))
        {
            if (reverse)
                removed.AddRange(visible.FastReverse());
            else
                removed.AddRange(visible);
        }
    }
    #endregion

    #region Insert Cues
    /// <summary>
    /// Inserts an ordered collection of cues at the given collection of visual indices in the cue list.
    /// </summary>
    /// <param name="visualIndices"></param>
    /// <param name="cues"></param>
    /// <param name="collectResults"></param>
    /// <returns></returns>
    protected internal int[] Insert(IEnumerable<int> visualIndices, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        return Insert(GetPositions(visualIndices), cues, collectResults);
    }

    protected internal int Insert(int visualIndex, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        if (visualIndex == visualCues.Count) // Add
            return Insert(new CuePosition(RootCueList.Count, null), cues, collectResults);
        else if (Find(visualIndex, out var pos)) // Insert
            return Insert(pos, cues, collectResults);
        return -2;
    }

    protected internal new int Insert(int visualIndex, CueViewModel cue)
    {
        if (visualIndex == visualCues.Count) // Add
            return Insert(new CuePosition(RootCueList.Count, null), cue);
        else if (Find(visualIndex, out var pos)) // Insert
            return Insert(pos, cue);
        return -2;
    }

    protected internal int[] Insert(IEnumerable<CuePosition> positions, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        using var addedInds = new TemporaryList<int>();
        using var _ = UndoManager.ScopedSuppress();

        foreach (var (cue, pos) in cues.Zip(positions))
        {
            AbstractCueList list = this;
            if (pos.group != null)
                list = pos.group.Cues;

            // Compute visPos
            int visPos = ComputeNewVisPos(pos);
            if (visPos == -1)
                continue;

            // Insert the cue into the sublist
            if (!list.Insert(pos.index, cue))
                continue;
            cue.Parent = pos.group;

            AddCueToDict(cue);

            // Update the visual list
            if (visPos != -1)
            {
                visualCues.Insert(visPos, cue);
                if (collectResults)
                    addedInds.Add(visPos);

                if (cue is GroupCueViewModel group)
                {
                    int groupCount = InsertGroupCueContents(visPos + 1, group, ref Unsafe.NullRef<TemporaryList<CueViewModel>>(), false);

                    if (collectResults)
                        addedInds.AddRange(Enumerable.Range(visPos + 1, groupCount));
                }
            }
            else
            {
                MainViewModel.Log($"Error adding cue to cue list, visPos invalid!", MainViewModel.LogLevel.Error);
            }
        }
        if (collectResults)
        {
            // There are probably some cases where sending more targetted collection change events would
            // be more efficient; alas, the interface only supports insertion starting at a single index,
            // so doing so would result in many calls to OnCollectionChanged (and lots of little
            // allocations). Which is prooooobbbbaaabbbly worse than just a single Reset notification.
            OnCollectionChanged();
            return addedInds.ToArray();
        }

        OnCollectionChanged();
        return [];
    }

    protected internal int Insert(CuePosition pos, IEnumerable<CueViewModel> cues, bool collectResults = true)
    {
        var added = new TemporaryList<CueViewModel>();
        using var _ = UndoManager.ScopedSuppress();

        int firstVisPos = -2;
        foreach (var cue in cues)
        {
            AbstractCueList list = this;
            if (pos.group != null)
                list = pos.group.Cues;

            // Compute visPos
            int visPos = ComputeNewVisPos(pos);
            if (visPos == -1)
                continue;
            if (firstVisPos == -2)
                firstVisPos = visPos;

            // Insert the cue into the sublist
            if (!list.Insert(pos.index, cue))
                continue;
            cue.Parent = pos.group;

            AddCueToDict(cue);

            // Update the visual list
            if (visPos != -1)
            {
                visualCues.Insert(visPos, cue);
                if (collectResults)
                    added.Add(cue);

                if (cue is GroupCueViewModel group)
                    InsertGroupCueContents(visPos + 1, group, ref added, collectResults);
            }
            else
            {
                MainViewModel.Log($"Error adding cue to cue list, visPos invalid!", MainViewModel.LogLevel.Error);
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
        using var _ = UndoManager.ScopedSuppress();

        AbstractCueList list = this;
        if (pos.group != null)
            list = pos.group.Cues;

        // Compute visPos
        int visPos = ComputeNewVisPos(pos);
        if (visPos == -1)
            return -1;

        // Insert the cue into the sublist
        if (!list.Insert(pos.index, cue))
            return -2;
        cue.Parent = pos.group;
        AddCueToDict(cue);

        // Update the visual list
        if (visPos != -1)
        {
            visualCues.Insert(visPos, cue);

            if (cue is GroupCueViewModel group)
            {
                TemporaryList<CueViewModel> added = [cue];
                InsertGroupCueContents(visPos + 1, group, ref added, true);
                OnCollectionChanged(NotifyCollectionChangedAction.Add, added, visPos);
                added.Dispose();
            }
            else
            {
                OnCollectionChanged(NotifyCollectionChangedAction.Add, cue, visPos);
            }
        }
        else
        {
            MainViewModel.Log($"Error adding cue to cue list, visPos invalid!", MainViewModel.LogLevel.Error);
        }

        return visPos;
    }

    private int ComputeNewVisPos(CuePosition pos)
    {
        int visPos = -1;

        var list = ((pos.group?.Cues as AbstractCueList) ?? this).RootCueList;
        if (list.Count > 0 && pos.index < list.Count)
        {
            // If possible find a cue that's already in this position and steal it's visPos
            var existing = list[pos.index];
            int existingPos = FindVisualIndex(existing);
            if (existingPos != -1)
                visPos = existingPos;
        }
        else if (pos.index > 0)
        {
            // Otherwise pick the end of the current sublist
            if (pos.group == null)
                visPos = visualCues.Count;
            else
            {
                var groupPos = FindVisualIndex(pos.group);
                if (groupPos != -1)
                    visPos = groupPos + pos.group.Cues.EnumerateVisible().Count() + 1; // Grrr, slow
            }
        }
        else if (pos.index == 0)
        {
            // Otherwise pick the start of the sublist
            if (pos.group == null)
                visPos = 0;
            else
            {
                var groupPos = FindVisualIndex(pos.group);
                if (groupPos != -1)
                    visPos = groupPos + 1;
            }
        }

        return visPos;
    }

    private int InsertGroupCueContents(int visualIndex, GroupCueViewModel group, ref TemporaryList<CueViewModel> inserted, bool collectResults)
    {
        group.OnCollapse += OnVisualGroupCollapsed;
        if (group.IsCollapsed)
        {
            AddCuesToDict(group.Cues.EnumerateAll());
            return 0;
        }

        var visible = group.Cues.EnumerateVisiblePositions();
        int addedCount = 0;
        foreach (var (cue, pos) in visible)
        {
            // Update the visual list
            visualCues.Insert(visualIndex++, cue);
            addedCount++;

            if (cue is GroupCueViewModel subgroup)
            {
                subgroup.OnCollapse += OnVisualGroupCollapsed;
                if (subgroup.IsCollapsed) // This subgroup contains cues that aren't visible, add them to the cache anyway
                    AddCuesToDict(subgroup.Cues.EnumerateAll());
            }
            if (collectResults)
                inserted.Add(cue);

            AddCueToDict(cue);
        }

        return addedCount;
    }
    #endregion

    private void AddCuesToDict(IEnumerable<CueViewModel> cues)
    {
        foreach (var cue in cues)
            cuesDict.TryAdd(cue.QID, cue);
    }
    private void AddCueToDict(CueViewModel cue)
    {
        cuesDict.TryAdd(cue.QID, cue);
    }
    private void RemoveCuesFromDict(IEnumerable<CueViewModel> cues)
    {
        foreach (var cue in cues)
            cuesDict.Remove(cue.QID, cue);
    }
    private void RemoveCueFromDict(CueViewModel cue)
    {
        cuesDict.Remove(cue.QID, cue);
    }

    private void ResetVisualCache()
    {
        // Unsubscribe old event listeners
        foreach (var oldGroup in visualCues.OfType<GroupCueViewModel>())
            oldGroup.OnCollapse -= OnVisualGroupCollapsed;

        // Clear the visual cache
        visualCues.Clear();
        cuesDict.Clear();

        // Re-create the visual cue list from scratch
        int visPos = 0;
        foreach (var (cue, pos) in EnumerateVisiblePositions())
        {
            visualCues.Add(cue);
            AddCueToDict(cue);
            if (cue is GroupCueViewModel subgroup)
            {
                subgroup.OnCollapse += OnVisualGroupCollapsed;
                if (subgroup.IsCollapsed)
                    AddCuesToDict(subgroup.Cues.EnumerateAll());
            }

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
        cuesDict.Clear();
        base.Clear();
        ResetVisualCache();
    }

    public bool Contains(CueViewModel item) => Find(item, out _);

    public override IEnumerable<CueViewModel> Enumerate(EnumerationMode mode)
    {
        return mode switch
        {
            EnumerationMode.Root => RootCueList,
            EnumerationMode.Visible => visualCues,
            EnumerationMode.All => EnumerateAll(),
            _ => EnumerateAll()
        };
    }

    /// <summary>
    /// Recursively enumerates all root cues and sub cues in this cue list in depth-first order. Starting from a given visual index.
    /// </summary>
    /// <param name="visualIndex">The index of the cue in the visual cue list to start iterating from.</param>
    /// <returns>An iterator which enumerates each cue in order.</returns>
    public IEnumerable<CueViewModel> EnumerateAllFrom(int visualIndex)
    {
        for (int i = visualIndex; i < visualCues.Count; i++)
        {
            var cue = visualCues[i];
            yield return cue;
            if (cue is GroupCueViewModel group && group.IsCollapsed)
            {
                var children = group.Cues.EnumerateAll();
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    public override IEnumerator<CueViewModel> GetEnumerator() => visualCues.GetEnumerator();

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

        public readonly IEnumerator<CueViewModel> GetEnumerator() => list.GetEnumerator(EnumerationMode.Visible);
        readonly IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}

