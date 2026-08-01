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
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using static QPlayer.ViewModels.MainViewModel;

namespace QPlayer.ViewModels;

/// <summary>
/// The base class for a hierarchical list of cues.
/// </summary>
public class CueList : BindableViewModel<List<Cue>>, IReadOnlyCollection<CueViewModel>
{
    private readonly List<CueViewModel> rootCueList = [];
    private readonly HashSet<GroupCueViewModel> groups = [];
    private readonly MainViewModel? mainViewModel;
    private readonly GroupCueViewModel? ownerGroup;
    /// <summary>
    /// The attached visual cue list.
    /// </summary>
    private VisualCueList? visualList;
    /// <summary>
    /// Stores the total number of cues in this list. This number is set by the <see cref="VisualCueList"/> at the root of the hierarchy.
    /// </summary>
    private int totalCount = 0;
    /// <summary>
    /// Is this cue list in the <see cref="visualList"/>.
    /// </summary>
    internal bool inMainList = false;

    /// <summary>
    /// This list of cues managed by this cue list, doesn't contain any cues belonging to child groups or parents.
    /// </summary>
    internal ReadOnlyCollection<CueViewModel> Cues { get; init; }
    /// <summary>
    /// The group cue which owns this cue list or <see langword="null"/> if this cue list is not owned by a group (or is the root cue list).
    /// </summary>
    internal GroupCueViewModel? OwnerGroup => ownerGroup;

    /// <summary>
    /// The number of cues at the root of the cue list (ie: not counting sub-cues)
    /// </summary>
    public int Count => rootCueList.Count;
    /// <summary>
    /// The total number of cues in the cue list.
    /// </summary>
    public int TotalCount => totalCount;
    /// <summary>
    /// Whether this cue list is empty.
    /// </summary>
    public bool IsEmpty => rootCueList.Count == 0;

    /// <summary>
    /// Gets the cue list which this cue list is inside of.
    /// </summary>
    private CueList? ParentList => ownerGroup?.Parent is GroupCueViewModel parentGroup ? parentGroup.Cues : (inMainList ? mainViewModel?.CueList : null);

    /// <summary>
    /// A delegate for changes in the contents of this CueList.
    /// </summary>
    /// <param name="wasInserted">Whether this changed cues in this event were inserted or deleted.</param>
    /// <param name="changedCues">The list of cues which were changed.</param>
    /// <param name="positions">The list of cue positions of the changed cues.</param>
    public delegate void CueListChangedDelegate(bool wasInserted, IEnumerable<CueViewModel> changedCues, IEnumerable<CuePosition>? positions);
    /// <summary>
    /// An event raised whenever the contents of this cue list has changed. (Only changes to direct descendants are raised)
    /// </summary>
    public event CueListChangedDelegate? CueListChanged;

    /// <summary>
    /// Gets a cue by position.
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    public CueViewModel this[CuePosition pos]
    {
        get => pos.group != null ? pos.group.Cues[pos.index] : Cues[pos.index];
    }

    /// <summary>
    /// Gets a root cue by index.
    /// </summary>
    /// <param name="index"></param>
    /// <returns></returns>
    public CueViewModel this[int index]
    {
        get => rootCueList[index];
    }

    public CueList(MainViewModel? mainViewModel, GroupCueViewModel? ownerGroup = null)
    {
        Cues = rootCueList.AsReadOnly();
        this.mainViewModel = mainViewModel;
        this.ownerGroup = ownerGroup;
        if (ownerGroup == null)
            visualList = mainViewModel?.Cues;
        //parentList = ownerGroup?.Parent is GroupCueViewModel parentGroup ? parentGroup.Cues : visualList?.CueList;
    }

    #region Public API
    public CuePosition CreateCuePosition(int index) => new(index, OwnerGroup);

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

    /// <summary>
    /// Shuffles the contents of this subgroup by deleting and re-inserting them.
    /// </summary>
    internal void Shuffle()
    {
        if (ownerGroup == null)
            return;
        var cues = Delete(new CuePositionRangeEnumerable(ownerGroup, 0, Count), false);
        Random.Shared.Shuffle(cues);
        Insert(CreateCuePosition(0), cues);
    }

    /// <summary>
    /// Inserts a single cue into the cue list or sublist.
    /// </summary>
    /// <param name="pos">The position at which to insert the cue.</param>
    /// <param name="cue">The cue to insert.</param>
    public void Insert(CuePosition pos, CueViewModel cue)
    {
        InsertSingleInternal(pos, cue);
        NotifyVisualInsert(new OneEnumerable<CueViewModel>(cue), new OneEnumerable<CuePosition>(pos));
    }

    /// <summary>
    /// Inserts a collection of cues at the given position in the cue list or sublist.
    /// </summary>
    /// <param name="pos">The position at which to insert the first cue in the collection.</param>
    /// <param name="cues">The collection of cues to insert.</param>
    public void Insert(CuePosition pos, IEnumerable<CueViewModel> cues)
    {
        var list = GetList(pos);
        int ind = Math.Max(0, pos.index);
        int count = 0;
        foreach (var cue in cues)
        {
            ind = Math.Min(ind, list.Count);
            if (!list.InsertInternal(ind, cue))
                Log($"Failed to insert cue {cue.FullQID} at position {pos}!", LogLevel.Warning);
            count++;
            ind++;
        }

        NotifyVisualInsert(cues, new CuePositionRangeEnumerable(pos.group, ind - count, count));
    }

    /// <summary>
    /// Inserts a collection of cues at the given positions in the cues list or sublist.
    /// </summary>
    /// <remarks>
    /// Note, that the cues are inserted in order hence care needs to be taken specifying the cue 
    /// positions as these may need to shift as cues are inserted.
    /// </remarks>
    /// <param name="positions">The positions at which to insert the cues.</param>
    /// <param name="cues">The cues to insert into the list.</param>
    public void Insert(IEnumerable<CuePosition> positions, IEnumerable<CueViewModel> cues)
    {
        foreach (var (pos, cue) in positions.FastZip(cues))
            InsertSingleInternal(pos, cue);

        NotifyVisualInsert(cues, positions);
    }

    private void InsertSingleInternal(CuePosition pos, CueViewModel cue)
    {
        var list = GetList(pos);
        int ind = Math.Clamp(pos.index, 0, list.Count);
        if (!list.InsertInternal(ind, cue))
            Log($"Failed to insert cue {cue.FullQID} at position {pos}!", LogLevel.Warning);
    }

    /// <summary>
    /// Removes a cue from this cue list (or it's children).
    /// </summary>
    /// <param name="cue">The cue instance to remove.</param>
    /// <returns>Whether the cue was deleted.</returns>
    public bool Delete(CueViewModel cue)
    {
        if (!Find(cue, out var pos))
            return false;

        var list = GetList(pos);
        var res = list.DeleteInternal(pos.index);

        if (res != null)
            NotifyVisualDelete([res], [pos]);

        return res != null;
    }

    /// <summary>
    /// Removes cues from this cue list (or it's children).
    /// </summary>
    /// <param name="cues">The cue instances to remove.</param>
    /// <returns>The cues which were deleted.</returns>
    public CueViewModel[] Delete(IEnumerable<CueViewModel> cues, bool collapseChildren = false)
    {
        using var positions = GetPositionsSorted(cues);
        var results = Delete(positions, false, collapseChildren);

        return results;
    }

    /// <summary>
    /// Removes a cue from this cue list (or it's children).
    /// </summary>
    /// <param name="pos">The cue position to remove.</param>
    /// <returns>The deleted cue, or <see langword="null"/> if the <paramref name="pos"/> was invalid.</returns>
    public CueViewModel? Delete(CuePosition pos) => Delete(new OneEnumerable<CuePosition>(pos), false).FirstOrDefault();

    /// <summary>
    /// Removes cues from this cue list (or it's children).
    /// </summary>
    /// <param name="positions">The cue positions to remove.</param>
    /// <param name="needsSorting">If the enumerable of cues is already in the order defined by 
    /// <see cref="SortPositions(Span{CuePosition})"/>, specify <see langword="false"/> to skip sorting again.</param>
    /// <param name="collapseChildren">When specified, positions which are children of another position in the enumerable are 
    /// skipped. This makes logical sense as deleting a group already implies deleting it's children, this option prevents those 
    /// children from being removed from the deleted group.</param>
    /// <returns>The cues which were deleted.</returns>
    public CueViewModel[] Delete(IEnumerable<CuePosition> positions, bool needsSorting = true, bool collapseChildren = false)
    {
        using TemporaryList<CuePosition> cuesList = default;
        using TemporaryList<CueViewModel> removedItemsRev = default; // Add the removed items in reverse

        if (needsSorting)
        {
            cuesList.AddRange(positions);
            SortPositions(cuesList.AsSpan());
            positions = cuesList;
        }

        if (collapseChildren)
        {
            // Reuse the cuesList temp list as a buffer for CollapseCuesOrdered
            if (cuesList.Count == 0)
                cuesList.AddRange(positions);

            int i = 0;
            using var collapseEnum = CollapseCuesOrdered(positions).GetEnumerator();
            while (collapseEnum.MoveNext())
            {
                cuesList[i] = collapseEnum.Current;
                i++;
            }

            positions = cuesList;
        }

        foreach (var pos in positions.FastReverse())
        {
            var list = GetList(pos);
            if (list.DeleteInternal(pos.index) is not CueViewModel removed)
                continue;

            removedItemsRev.Add(removed);
        }

        var removedArr = removedItemsRev.FastReverse().ToArray();
        NotifyVisualDelete(removedArr, positions);

        return removedArr;
    }

    /// <summary>
    /// Clears all the cues from this list.
    /// </summary>
    public void Clear()
    {
        visualList?.ResetVisualList();
        ClearInternal();
    }
    #endregion

    #region Internal Insert/Delete

    private IEnumerable<CuePosition> CollapseCuesOrdered(IEnumerable<CuePosition> positions)
    {
        HashSet<GroupCueViewModel> skip = [];
        HashSet<GroupCueViewModel> keep = [];
        foreach (var pos in positions)
        {
            if (this[pos] is GroupCueViewModel group)
                skip.Add(group);

            // Top-level cues are always returned
            if (pos.group == null)
            {
                yield return pos;
                continue;
            }
            // Groups in the keep set are always returned
            if (keep.Contains(pos.group))
            {
                yield return pos;
                continue;
            }
            // Groups in the skip set are skipped
            if (skip.Contains(pos.group))
                continue;

            if (pos.group.Parent != null)
            {
                bool skipped = false;
                foreach (var parent in EnumerateParents(pos.group.Parent))
                {
                    if (skip.Contains(parent))
                    {
                        skipped = true;
                        break;
                    }
                }
                if (!skipped)
                {
                    keep.Add(pos.group);
                    yield return pos;
                }
                else
                    skip.Add(pos.group);
                continue;
            }
            else
            {
                keep.Add(pos.group);
                yield return pos;
            }
        }
    }

    private static IEnumerable<CueViewModel> EnumerateParents(CueViewModel cue)
    {
        var parent = cue;
        while ((parent = parent.Parent) != null)
            yield return parent;
    }

    private CueList GetList(CuePosition pos) => pos.group != null ? pos.group.Cues : this; // TODO: This should assert that the group is part of this cue list's hierarchy

    /// <summary>
    /// Sorts a span of cue positions by their depth-first hierarchical order. This is the same as the visual order of the 
    /// cues, but doesn't depend on them being in the visual list. This method relies on the parents of the positions existing
    /// in this cue list.
    /// </summary>
    /// <param name="positions"></param>
    private void SortPositions(Span<CuePosition> positions)
    {
        if (positions.Length < 2)
            return;

        if (positions.Length < totalCount / 4)
        {
            // Sort by comparing positions
            // O(n log n) where n is positions, the comparer is also O(n) worst case
            var comparer = new CuePositionComparer(this);
            positions.Sort(comparer);
        }
        else
        {
            // Check for trivially sorted inputs
            var last = positions[0];
            bool sorted = true;
            for (int j = 1; j < positions.Length; j++)
            {
                var next = positions[j];
                if (last.group != next.group || last.index > next.index)
                {
                    sorted = false;
                    break;
                }
                last = next;
            }
            if (sorted)
                return;

            // Sort by enumerating and filtering the whole cue list.
            // O(m) where m is cue list length
            var positionsSet = new HashSet<CuePosition>(positions.Length);
            foreach (var pos in positions)
                positionsSet.Add(pos);
            int i = 0;
            foreach (var cand in EnumerateAllPositions())
            {
                if (positionsSet.Contains(cand))
                    positions[i++] = cand;
                if (i == positions.Length) 
                    break;
            }
            for (; i < positions.Length; i++)
                positions[i] = CuePosition.Invalid;
        }

        /*Dictionary<GroupCueViewModel, CuePosition> groupPositions = [];

        // https://en.wikipedia.org/wiki/Heapsort#Standard_implementation
        int start = positions.Length / 2;
        int end = positions.Length;
        while (end > 1)
        {
            // Extract
            if (start > 0)
                start--;
            else
            {
                end--;
                (positions[end], positions[0]) = (positions[0], positions[end]);
            }

            // Sift down
            int root = start;
            int child;
            while ((child = LeftChild(root)) < end)
            {
                if (child + 1 < end && LessThan(positions[child], positions[child + 1]))
                    child++;

                if (LessThan(positions[root], positions[child]))
                {
                    (positions[root], positions[child]) = (positions[child], positions[root]);
                }
                else
                    break;
            }
        }

        //static int LeftChild(int i) => (i >> 1) + 1;
        //int RightChild(int i) => (i >> 1) + 2;
        //int Parent(int i) => (i - 1) << 1;
        bool LessThan(CuePosition a, CuePosition b)
        {
            // Trivial case
            if (a.group == b.group)
                return a.index < b.index;

            int aParentCount = CountParents(a.group);
            int bParentCount = CountParents(b.group);
            // Move to the same parent depth
            while (aParentCount > bParentCount)
            {
                a = GetParentPos(a.group!); // a must have at least one parent in this path
                aParentCount--;
            }
            while (bParentCount > aParentCount)
            {
                b = GetParentPos(b.group!); // b must have at least one parent in this path
                bParentCount--;
            }
            while (a.group != b.group)
            {
                a = GetParentPos(a.group!); // this is safe, since they should reach null (the root) at the same
                                            // time, hence the while loop would exit before this is dereferenced
                b = GetParentPos(b.group!);
            }

            return a.index < b.index;
        }

        CuePosition GetParentPos(GroupCueViewModel cue)
        {
            if (groupPositions.TryGetValue(cue, out var pos))
                return pos;
            if (Find(cue, out pos))
            {
                groupPositions.Add(cue, pos);
                return pos;
            }
            return CuePosition.Invalid;
        }

        static int CountParents(CueViewModel? cue)
        {
            int count = 0;
            while (cue != null)
            {
                count++;
                cue = cue.Parent;
            }
            return count;
        }*/
    }

    private readonly struct CuePositionComparer(CueList cueList) : IComparer<CuePosition>
    {
        private readonly Dictionary<GroupCueViewModel, CuePosition> groupPositions = [];

        public int Compare(CuePosition x, CuePosition y)
        {
            // Trivial case
            if (x.group == y.group)
                return x.index.CompareTo(y.index);

            int aParentCount = CountParents(x.group);
            int bParentCount = CountParents(y.group);
            // Move to the same parent depth
            while (aParentCount > bParentCount)
            {
                x = GetParentPos(x.group!); // a must have at least one parent in this path
                aParentCount--;
            }
            while (bParentCount > aParentCount)
            {
                y = GetParentPos(y.group!); // b must have at least one parent in this path
                bParentCount--;
            }
            while (x.group != y.group)
            {
                x = GetParentPos(x.group!); // this is safe, since they should reach null (the root) at the same
                                            // time, hence the while loop would exit before this is dereferenced
                y = GetParentPos(y.group!);
            }

            return x.index.CompareTo(y.index);
        }

        CuePosition GetParentPos(GroupCueViewModel cue)
        {
            if (groupPositions.TryGetValue(cue, out var pos))
                return pos;
            if (cueList.Find(cue, out pos))
            {
                groupPositions.Add(cue, pos);
                return pos;
            }
            return CuePosition.Invalid;
        }

        static int CountParents(CueViewModel? cue)
        {
            int count = 0;
            while (cue != null)
            {
                count++;
                cue = cue.Parent;
            }
            return count;
        }
    }

    private readonly struct CuePositionIndComparer : IComparer<CuePosition>
    {
        public readonly int Compare(CuePosition x, CuePosition y) => x.index.CompareTo(y.index);
    }

    internal void AttachVisualList(VisualCueList? list) => visualList = list;

    private void NotifyVisualInsert(IEnumerable<CueViewModel> cues, IEnumerable<CuePosition> positions)
    {
        var list = this;
        while (list != null)
        {
            list.visualList?.NotifyVisualInsert(cues, positions);

            list = list.ParentList;
            if (list?.ownerGroup is GroupCueViewModel group && group.IsCollapsed)
                break;
        }
    }

    private void NotifyVisualDelete(IEnumerable<CueViewModel> cues, IEnumerable<CuePosition> positions)
    {
        var list = this;
        while (list != null)
        {
            list.visualList?.NotifyVisualDelete(cues, positions);

            list = list.ParentList;
            if (list?.ownerGroup is GroupCueViewModel group && group.IsCollapsed)
                break;
        }
    }

    private void IncrementTotalCount(int delta)
    {
        if (delta == 0)
            return;

        totalCount += delta;

        var parent = ParentList;
        while (parent != null)
        {
            parent.totalCount += delta;
            parent = parent.ParentList;
        }
    }

    /// <summary>
    /// Inserts a cue at the given index in the root cue list.
    /// </summary>
    /// <remarks>Should only be called by <see cref="VisualCueList"/></remarks>
    /// <param name="index"></param>
    /// <param name="item"></param>
    /// <returns></returns>
    private bool InsertInternal(int index, CueViewModel item)
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

        OnCueListChanged(true, item, CreateCuePosition(index));
        //if (group != null)
        //    OnCueListChanged(true, group.Cues.EnumerateAll())

        return true;
    }

    /// <summary>
    /// Inserts a range of ordered cues at the specified index in the root cue list.
    /// </summary>
    /// <remarks>Should only be called by <see cref="VisualCueList"/></remarks>
    /// <param name="index"></param>
    /// <param name="items"></param>
    /// <returns></returns>
    private bool InsertInternal(int index, IEnumerable<CueViewModel> items)
    {
        var list = rootCueList;
        var model = boundModel;
        if (index < 0 || index > list.Count)
            return false;

        list.InsertRange(index, items);
        model?.InsertRange(index, items.Select(x => x.BoundModel!));

        int added = 0;
        int itemsCount = 0;
        foreach (var item in items)
        {
            added++;
            if (item is GroupCueViewModel group)
            {
                groups.Add(group);
                added += group.Cues.totalCount;
            }
            item.Parent = OwnerGroup;
            itemsCount++;
        }
        IncrementTotalCount(added);
        OnCueListChanged(true, items, Enumerable.Range(index, itemsCount).Select(CreateCuePosition));

        return true;
    }

    /// <summary>
    /// Removes a cue by index from the root cue list.
    /// </summary>
    /// <remarks>Should only be called by <see cref="VisualCueList"/></remarks>
    /// <param name="index"></param>
    /// <returns>The cue that was removed or <see langword="null"/> if the index was invalid.</returns>
    private CueViewModel? DeleteInternal(int index)
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
        item.Parent = null;
        UndoManager.UnSuppressRecording();
        OnCueListChanged(false, item, CreateCuePosition(index));

        return item;
    }

    /// <summary>
    /// Removes a range of cues by index from the root cue list.
    /// </summary>
    /// <remarks>Should only be called by <see cref="VisualCueList"/></remarks>
    /// <param name="indices">The enumerable of indices to remove.</param>
    /// <param name="isSorted">If the <paramref name="indices"/> is already sorted in ascending order, 
    /// then this skips needing to copy and sort the indices before use.</param>
    /// <returns>An array of cues that were removed or an empty array if none were removed. 
    /// Note, that group cues are not enumerated in this array.</returns>
    private CueViewModel[] DeleteInternal(IEnumerable<int> indices, bool isSorted = false)
    {
        var list = rootCueList;
        var model = boundModel;

        using TemporaryList<CueViewModel> results = [];

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

        OnCueListChanged(false, results, inds.Select(CreateCuePosition));

        tl.Dispose();
        return results.ToArray();
    }

    /// <summary>
    /// Removes all cues from the root cue list.
    /// </summary>
    /// <remarks>Should only be called by <see cref="VisualCueList"/></remarks>
    private void ClearInternal()
    {
        UndoManager.SuppressRecording();
        foreach (var cue in rootCueList)
            cue.Parent = null;
        UndoManager.UnSuppressRecording();

        IncrementTotalCount(-totalCount);
        OnCueListChanged(false, rootCueList, Enumerable.Range(0, rootCueList.Count).Select(CreateCuePosition));
        rootCueList.Clear();
        boundModel?.Clear();
        groups?.Clear();
    }

    private void OnCueListChanged(bool inserted, CueViewModel cue, CuePosition pos) => CueListChanged?.Invoke(inserted, new OneEnumerable<CueViewModel>(cue), new OneEnumerable<CuePosition>(pos));
    private void OnCueListChanged(bool inserted, IEnumerable<CueViewModel> cues, IEnumerable<CuePosition> pos) => CueListChanged?.Invoke(inserted, cues, pos);
    private void OnCueListChanged(bool inserted, CueViewModel cue) => CueListChanged?.Invoke(inserted, new OneEnumerable<CueViewModel>(cue), []);
    private void OnCueListChanged(bool inserted, IEnumerable<CueViewModel> cues) => CueListChanged?.Invoke(inserted, cues, []);

    #endregion

    #region Enumerators
    /// <summary>
    /// Gets a list of sorted cue positions for the given enumerable of cue instances.
    /// </summary>
    /// <param name="cues"></param>
    /// <returns></returns>
    internal TemporaryList<CuePosition> GetPositionsSorted(IEnumerable<CueViewModel> cues)
    {
        var positions = new TemporaryList<CuePosition>();
        foreach (var cue in cues)
        {
            if (!Find(cue, out var pos))
                continue;
            positions.Add(pos);
        }
        SortPositions(positions.AsSpan());
        return positions;
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
    /// See also <seealso cref="VisualCueList.VisualCues"/> for more efficient visual 
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

    /// <summary>
    /// Enumerates every existing cue position in this cue list and it's sub lists.
    /// </summary>
    /// <returns></returns>
    protected internal IEnumerable<CuePosition> EnumerateAllPositions()
    {
        var parent = OwnerGroup;
        for (int i = 0; i < rootCueList.Count; i++)
        {
            yield return new(i, parent);
            if (rootCueList[i] is GroupCueViewModel subgroup)
            {
                var children = subgroup.Cues.EnumerateAllPositions();
                foreach (var child in children)
                    yield return child;
            }
        }
    }

    public virtual IEnumerator<CueViewModel> GetEnumerator() => rootCueList.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <inheritdoc cref="List{T}.ToArray"/>
    public CueViewModel[] ToArray() => rootCueList.ToArray();
    #endregion

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

        CollectionsMarshal.SetCount(boundModel, rootCueList.Count);
        var dst = CollectionsMarshal.AsSpan(boundModel);
        for (int i = 0; i < rootCueList.Count; i++)
        {
            CueViewModel? cue = rootCueList[i];
            dst[i] = cue.BoundModel!;

            if (cue is GroupCueViewModel group)
                group.Cues.SyncToModel();
        }
    }

    /// <summary>
    /// Syncronises the contents of this CueList with the cue models in the bound model 
    /// (set via <see cref="Bind(List{Cue})"/>).
    /// </summary>
    public override void SyncFromModel()
    {
        if (mainViewModel == null)
            return;

        SyncFromModel(cue =>
        {
            try
            {
                return CueFactory.CreateViewModelForCue(cue, mainViewModel);
            }
            catch (Exception ex)
            {
                Log($"Couldn't create cue (qid: {cue.qid}) from save file! Ensure that any plugins used by the save file are loaded. {ex.Message}\n{ex}", LogLevel.Error);
            }
            return null;
        });
    }

    /// <summary>
    /// Syncronises the contents of this CueList with the cue models in the bound model 
    /// (set via <see cref="Bind(List{Cue})"/>) using the given Model --> ViewModel 
    /// converter.
    /// </summary>
    /// <param name="convertCue"></param>
    public virtual void SyncFromModel(Func<Cue, CueViewModel?> convertCue)
    {
        if (boundModel == null)
            return;

        base.SyncFromModel();

        // Clear
        OnCueListChanged(false, rootCueList, Enumerable.Range(0, rootCueList.Count).Select(CreateCuePosition));

        int deltaCount = 0;
        IncrementTotalCount(-totalCount);
        CollectionsMarshal.SetCount(rootCueList, boundModel.Count);
        var dst = CollectionsMarshal.AsSpan(rootCueList);
        for (int i = 0; i < boundModel.Count; i++)
        {
            var cue = boundModel[i];
            var cueVm = convertCue(cue) ?? CreateFallbackCue(cue);
            cueVm.Parent = OwnerGroup;
            dst[i] = cueVm;
            deltaCount++;
            if (cueVm is GroupCueViewModel group)
            {
                group.Cues.SyncFromModel(convertCue);
                groups.Add(group);
                deltaCount += group.Cues.totalCount;
            }
        }
        IncrementTotalCount(deltaCount);

        // Insert
        OnCueListChanged(true, rootCueList, Enumerable.Range(0, rootCueList.Count).Select(CreateCuePosition));

        visualList?.ResetVisualList();
    }

    public delegate ValueTask<CueViewModel?> ConvertCueDelegate(Cue src);

    /// <inheritdoc cref="SyncFromModel(Func{Cue, CueViewModel?})"/>
    public virtual async Task SyncFromModelAsync(ConvertCueDelegate convertCue)
    {
        if (boundModel == null)
            return;

        base.SyncFromModel();

        // Clear
        OnCueListChanged(false, rootCueList, Enumerable.Range(0, rootCueList.Count).Select(CreateCuePosition));

        int deltaCount = 0;
        IncrementTotalCount(-totalCount);
        CollectionsMarshal.SetCount(rootCueList, boundModel.Count);
        for (int i = 0; i < boundModel.Count; i++)
        {
            var cue = boundModel[i];
            var cueVm = await convertCue(cue) ?? CreateFallbackCue(cue);
            cueVm.Parent = OwnerGroup;
            rootCueList[i] = cueVm;
            deltaCount++;
            if (cueVm is GroupCueViewModel group)
            {
                await group.Cues.SyncFromModelAsync(convertCue);
                groups.Add(group);
                deltaCount += group.Cues.totalCount;
            }
        }
        IncrementTotalCount(deltaCount);

        // Insert
        OnCueListChanged(true, rootCueList, Enumerable.Range(0, rootCueList.Count).Select(CreateCuePosition));

        visualList?.ResetVisualList();
    }

    private CueViewModel CreateFallbackCue(Cue src)
    {
        var vm = CueFactory.CreateViewModel(nameof(DummyCue), mainViewModel!)!;
        vm.QID = src.qid;
        vm.Name = src.name;
        vm.Description = "QPlayer failed to load this cue from the showfile! If this cue type is defined by a plugin, please ensure it is installed correctly.";
        return vm;
    }
    #endregion
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

public class VisualCueList : IReadOnlyList<CueViewModel>, INotifyCollectionChanged, INotifyPropertyChanged
{
    private readonly CueList cues;
    private readonly List<CueViewModel> visualCues = [];
    private readonly MultiDict<string, CueViewModel> cuesDict = [];

    public int Count => visualCues.Count;
    public int TotalCount => cues.TotalCount;
    public CueList CueList => cues;

    public CueViewModel this[int index] => visualCues[index];
    public CueViewModel this[CuePosition index] => cues[index];

    public event NotifyCollectionChangedEventHandler? CollectionChanged;
    public event PropertyChangedEventHandler? PropertyChanged;

    private static readonly PropertyChangedEventArgs CountPropertyChanged = new(nameof(Count));
    private static readonly PropertyChangedEventArgs IndexerPropertyChanged = new("Item[]");
    private static readonly NotifyCollectionChangedEventArgs ResetCollectionChanged = new(NotifyCollectionChangedAction.Reset);

    internal VisualCueList(CueList cues)
    {
        this.cues = cues;
        cues.AttachVisualList(this);
    }

    #region Getters
    /// <summary>
    /// Checks if the cue at the given visual index is the last cue in a group.
    /// </summary>
    /// <param name="visualPos"></param>
    /// <returns></returns>
    internal bool IsLastInGroup(int visualPos)
    {
        if (visualPos >= visualCues.Count - 1)
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

    /// <summary>
    /// Tries to find a cue view model given a cue ID.
    /// </summary>
    /// <param name="id">The cue ID to search for.</param>
    /// <param name="cue">The returned cue view model if it was found.</param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool Find(string id, [NotNullWhen(true)] out CueViewModel? cue)
    {
        return cuesDict.TryGetValue(id, out cue);
    }

    /// <summary>
    /// Tries to find a cue's <see cref="CuePosition"/> given a cue ID.
    /// </summary>
    /// <param name="id">The cue ID to search for.</param>
    /// <param name="pos">The position of the cue in the cue stack if it was found.</param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool Find(string id, out CuePosition pos)
    {
        if (cuesDict.TryGetValue(id, out CueViewModel? val))
            return cues.Find(val!, out pos);
        pos = default;
        return false;
    }

    /// <summary>
    /// Finds the index of the specified cue in the visual cue list, or <c>-1</c> if the cue does not exist in the visual list.
    /// </summary>
    /// <param name="item"></param>
    /// <returns></returns>
    public int FindVisualIndex(CueViewModel cue)
    {
        return visualCues.IndexOf(cue);
    }

    /// <summary>
    /// Finds the index of the specified cue position in the visual cue list, or <c>-1</c> if the position does not exist in the visual list.
    /// </summary>
    /// <param name="pos"></param>
    /// <returns></returns>
    public int FindVisualIndex(CuePosition pos)
    {
        if (pos.index >= 0 && !BoundsCheck(pos, false))
        {
            // The position is after the end of the sublist, find the position of the last cue in the sublist
            if (pos.index == 0)
                return pos.group == null ? 0 : (FindVisualIndex(pos.group) + 1);
            else
                return FindVisualIndex(pos - 1) + 1;
        }
        return FindVisualIndex(this[pos]);
    }

    /*/// <inheritdoc cref="FindVisualIndex(CueViewModel)"/>
    public int FindVisualIndex(CuePosition pos)
    {
        var cue = cues[pos];
        return FindVisualIndex(cue);
    }

    /// <inheritdoc cref="FindVisualIndex(CuePosition)"/>
    public bool FindVisualIndex(CuePosition pos, out int visualIndex)
    {
        visualIndex = FindVisualIndex(pos);
        return visualIndex != -1;
    }*/

    /// <inheritdoc cref="FindVisualIndex(CueViewModel)"/>
    public bool FindVisualIndex(CueViewModel cue, out int visualIndex)
    {
        visualIndex = FindVisualIndex(cue);
        return visualIndex != -1;
    }

    /// <summary>
    /// Checks if the given cue position is within the bounds of it's sublist.
    /// </summary>
    /// <param name="pos"></param>
    /// <returns><see langword="true"/> if the position is valid.</returns>
    public bool BoundsCheck(CuePosition pos, bool canAppend = false)
    {
        if (pos.index < 0)
            return false;
        if (canAppend)
        {
            if (pos.group == null)
                return pos.index <= cues.Count;
            else
                return pos.index <= pos.group.Cues.Count;
        }
        else
        {
            if (pos.group == null)
                return pos.index < cues.Count;
            else
                return pos.index < pos.group.Cues.Count;
        }
    }

    public CuePosition ClampCuePos(CuePosition pos, bool canAppend)
    {
        if (pos.index < 0)
            return new(0, pos.group);
        var count = pos.group == null ? cues.Count : pos.group.Cues.Count;
        if (canAppend && pos.index >= count)
            return new(count, pos.group);
        else if (pos.index >= count)
            return new(count - 1, pos.group);
        else
            return pos;
    }
    #endregion

    internal void ResetVisualList()
    {
        ResetVisualCache();
        OnCollectionChanged();
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
    internal void NotifyQIDChanged(string oldVal, string newVal, CueViewModel src)
    {
        cuesDict.UpdateKey(oldVal, newVal, src);
        //if (!cuesDict.UpdateKey(oldVal, newVal, src))
        //    cuesDict.Add(newVal, src);
    }

    /// <summary>
    /// Updates the visual list and cue dictionary with newly inserted items.
    /// </summary>
    /// <param name="cues">The cues to insert in the visual list. Subcues are automatically added as well.</param>
    /// <param name="positions">The positions of the cues to insert. These positions are all expected to belong to a single group.</param>
    internal void NotifyVisualInsert(IEnumerable<CueViewModel> cues, IEnumerable<CuePosition> positions)
    {
        using var addedCues = new TemporaryList<CueViewModel>();
        using var addedInds = new TemporaryList<int>();
        bool isContiguous = true;
        var lastVisPos = -2;
        var lastPos = default(CuePosition);
        using var _ = UndoManager.ScopedSuppress();

        foreach (var (cue, pos) in cues.FastZip(positions))
        {
            var posClamp = ClampCuePos(pos, true);
            // Compute visPos
            int visPos = ComputeNewVisPos(posClamp, lastVisPos, lastPos);
            if (visPos == -1)
                continue;

            if (lastVisPos != -2 && visPos - 1 != lastVisPos)
                isContiguous = false;
            lastVisPos = visPos;
            lastPos = posClamp;

            AddCueToDict(cue);

            // Update the visual list
            addedInds.Add(visPos);
            addedCues.Add(cue);

            if (cue is GroupCueViewModel group)
                InsertGroupCueContents(visPos + 1, group);
        }

        if (addedInds.Count > 0)
        {
            // Update the visual list
            if (isContiguous)
            {
                visualCues.InsertRange(addedInds[0], addedCues);
                OnCollectionChanged(NotifyCollectionChangedAction.Add, addedCues, addedInds[0]);
            }
            else
            {
                for (int i = 0; i < addedInds.Count; i++)
                    visualCues.Insert(addedInds[i], addedCues[i]);

                OnCollectionChanged();
            }
        }

        void InsertGroupCueContents(int visualIndex, GroupCueViewModel group)
        {
            group.Cues.inMainList = true;
            group.OnCollapse += OnVisualGroupCollapsed;
            if (group.IsCollapsed)
            {
                AddCuesToDict(group.Cues.EnumerateAll());
                return;
            }

            var visible = group.Cues.EnumerateVisiblePositions();
            foreach (var (cue, pos) in visible)
            {
                if (cue is GroupCueViewModel subgroup)
                {
                    //subgroup.Cues.inMainList = true;
                    subgroup.OnCollapse += OnVisualGroupCollapsed;
                    if (subgroup.IsCollapsed) // This subgroup contains cues that aren't visible, add them to the cache anyway
                        AddCuesToDict(subgroup.Cues.EnumerateAll());
                }

                // Update the visual list
                addedInds.Add(visualIndex++);
                addedCues.Add(cue);

                AddCueToDict(cue);
            }
        }
    }

    /// <summary>
    /// Updates the visual list and cue dictionary with newly deleted items.
    /// </summary>
    /// <param name="cues">The cues to delete from the visual list. Subcues are automatically removed as well.</param>
    /// <param name="positions">The positions of the cues to remove. These positions are all expected to belong to a single group.</param>
    internal void NotifyVisualDelete(IEnumerable<CueViewModel> cues, IEnumerable<CuePosition> positions)
    {
        _ = cues.TryGetNonEnumeratedCount(out int cuesCount);
        using var removedVisualsRev = new TemporaryList<CueViewModel>(cuesCount);
        using var visPosesRev = new TemporaryList<int>(cuesCount);
        bool isContiguous = true;
        var lastVisPos = -2;
        foreach (var (cue, pos) in cues.FastZip(positions).FastReverse())
        {
            RemoveCueFromDict(cue);
            if (cue is GroupCueViewModel group)
                RemoveGroupCueContents(group);

            AddVisCue(cue);
        }

        if (visPosesRev.Count > 0)
        {
            // Update the visual list
            if (isContiguous)
            {
                visualCues.RemoveRange(visPosesRev[^1], visPosesRev.Count);
                OnCollectionChanged(NotifyCollectionChangedAction.Remove, removedVisualsRev.FastReverse(), visPosesRev[^1]);
            }
            else
            {
                foreach (var pos in visPosesRev)
                    visualCues.RemoveAt(pos);

                OnCollectionChanged();
            }
        }

        void RemoveGroupCueContents(GroupCueViewModel group)
        {
            group.OnCollapse -= OnVisualGroupCollapsed;
            group.Cues.inMainList = false;
            if (group.IsCollapsed)
            {
                RemoveCuesFromDict(group.Cues.EnumerateAll());
                return;
            }

            foreach (var subcue in group.Cues.EnumerateVisible().FastReverse())
            {
                AddVisCue(subcue);

                RemoveCueFromDict(subcue);
                if (subcue is GroupCueViewModel subgroup)
                {
                    subgroup.OnCollapse -= OnVisualGroupCollapsed;
                    if (subgroup.IsCollapsed) // Remove any hidden cues too.
                        RemoveCuesFromDict(subgroup.Cues.EnumerateAll());
                }
            }
        }

        void AddVisCue(CueViewModel cue)
        {
            var visPos = FindVisPosContiguous(cue, lastVisPos, true);
            if (visPos >= 0)
            {
                removedVisualsRev.Add(cue);
                visPosesRev.Add(visPos);
                if (lastVisPos != -2 && visPos + 1 != lastVisPos)
                    isContiguous = false;
                lastVisPos = visPos;
            }
        }
    }

    #region Enumerators
    /// <summary>
    /// Enumerates the cues in this list from their <see cref="CueList.CuePosition"/>s.
    /// </summary>
    /// <param name="positions"></param>
    /// <returns></returns>
    internal IEnumerable<CueViewModel> GetCues(IEnumerable<CuePosition> positions)
    {
        foreach (var pos in positions)
        {
            var src = pos.group?.Cues?.Cues ?? cues.Cues;
            if (pos.index < 0 || pos.index >= src.Count)
                continue;
            yield return src[pos.index];
        }
    }

    internal IEnumerable<CuePosition> GetPositions(IEnumerable<CueViewModel> cues) => this.cues.GetPositions(cues);

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
            if (ind < 0)
                continue;
            if (allowAdd) // Add
            {
                if (ind >= visualCues.Count)
                {
                    pos = new(cues.Count, null);
                    parent = null;
                    goto Found;
                }
            }
            else if (ind >= visualCues.Count)
            {
                continue;
            }

            parent = visualCues[ind].Parent as GroupCueViewModel;
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

            if (cues.Find(visualCues[ind], out pos))
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

        if (inds.Count == 0)
            return ([], []);

        // Sort them
        inds.Sort();

        // Get the corresponding visual cue index
        for (int i = 0; i < inds.Count; i++)
            cuesList.Add(visualCues[inds[i]]);

        return (inds.ToArray(), cuesList.ToArray());
    }

    public IEnumerable<CueViewModel> EnumerateAll() => cues.EnumerateAll();

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

    public IEnumerator<CueViewModel> GetEnumerator() => visualCues.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    #endregion

    #region Internals
    private void OnPropertyChanged(PropertyChangedEventArgs args) => PropertyChanged?.Invoke(this, args);

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

    /// <summary>
    /// Finds the index of the given cue in the visual list, or <c>-1</c> if the cue does not exist in the visible 
    /// list. This is an optimised version of <see cref="FindVisualIndex(CueViewModel)"/> which can find consecutive 
    /// cues faster.
    /// </summary>
    /// <param name="cue">The cue to find.</param>
    /// <param name="lastVisPos">The last visual position found by this method.</param>
    /// <param name="reverse">Whether to search the cue list in reverse.</param>
    /// <returns></returns>
    private int FindVisPosContiguous(CueViewModel cue, int lastVisPos, bool reverse)
    {
        if (lastVisPos < 0 || lastVisPos >= visualCues.Count)
            return FindVisualIndex(cue);

        int res;
        if (!reverse)
        {
            res = visualCues.IndexOf(cue, lastVisPos);
            if (res == -1)
                res = visualCues.IndexOf(cue, 0, lastVisPos);
        }
        else
        {
            res = visualCues.LastIndexOf(cue, lastVisPos);
            if (res == -1)
                res = visualCues.LastIndexOf(cue, visualCues.Count - 1, visualCues.Count - lastVisPos);
        }
        return res;
    }

    /// <summary>
    /// Computes the visual position of a given <see cref="CuePosition"/> for a newly inserted cue.
    /// </summary>
    /// <param name="pos"></param>
    /// <param name="lastVisPos"></param>
    /// <param name="lastPos"></param>
    /// <returns></returns>
    private int ComputeNewVisPos(CuePosition pos, int lastVisPos, CuePosition lastPos)
    {
        int visPos = -1;

        if (pos.index < 0)
            return visPos;

        // Contiguous cues shortcut
        if (lastVisPos >= 0
            && pos.group == lastPos.group && lastPos.index == pos.index - 1)
            return lastVisPos + 1;

        // Early out for positions in collapsed groups
        {
            var parent = pos.group;
            while (parent != null)
            {
                if (parent.IsCollapsed)
                    return visPos;
                parent = parent.Parent as GroupCueViewModel;
            }
        }

        var list = (pos.group?.Cues) ?? cues;
        if (list.Count > 1 && pos.index < list.Count - 1) // TODO: This -1 here is only correct if only 1 cue has been added to the list.
        {
            // Search the visual list for the matching position
            int localPos = 0;
            for (int i = 0; i < visualCues.Count; i++)
            {
                var visCue = visualCues[i];
                if (visCue.Parent != pos.group)
                {
                    // Skip any cues which don't belong to the group we're searching for.
                    if (visCue is GroupCueViewModel group
                        && pos.group != null
                        && group != pos.group
                        && !pos.group.HasParent(group))
                        i += group.Cues.TotalCount - 1;
                    continue;
                }
                if (localPos == pos.index)
                {
                    visPos = i;
                    break;
                }
                localPos++;
            }
            if (visPos == -1)
                visPos = visualCues.Count; // Default to the end of the list, it's probably right...
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
                {
                    // Scan till the end of this group
                    visPos = groupPos + 1 + pos.group.Cues.Count; // Start as far into the group as we can
                    for (; visPos < visualCues.Count; visPos++)
                    {
                        var visCue = visualCues[visPos];
                        if (!visCue.HasParent(pos.group))
                            break;
                    }
                    visPos--;
                }
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
            Log($"Tried to collapse/expand group which is not in the cue list!", LogLevel.Warning);
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

    private void AddCuesToDict(IEnumerable<CueViewModel> cues)
    {
        foreach (var cue in cues)
            cuesDict.TryAdd(cue.FullQID, cue);
    }
    private void AddCueToDict(CueViewModel cue)
    {
        cuesDict.TryAdd(cue.FullQID, cue);
    }
    private void RemoveCuesFromDict(IEnumerable<CueViewModel> cues)
    {
        foreach (var cue in cues)
            cuesDict.Remove(cue.FullQID, cue);
    }
    private void RemoveCueFromDict(CueViewModel cue)
    {
        cuesDict.Remove(cue.FullQID, cue);
    }

    private void ResetVisualCache()
    {
        // Unsubscribe old event listeners
        foreach (var oldGroup in visualCues.OfType<GroupCueViewModel>())
        {
            oldGroup.OnCollapse -= OnVisualGroupCollapsed;
            oldGroup.Cues.inMainList = false;
        }

        // Clear the visual cache
        visualCues.Clear();
        cuesDict.Clear();

        // Re-create the visual cue list from scratch
        int visPos = 0;
        foreach (var (cue, pos) in cues.EnumerateVisiblePositions())
        {
            visualCues.Add(cue);
            AddCueToDict(cue);
            if (cue is GroupCueViewModel subgroup)
            {
                subgroup.Cues.inMainList = subgroup.Parent == null;
                subgroup.OnCollapse += OnVisualGroupCollapsed;
                if (subgroup.IsCollapsed)
                    AddCuesToDict(subgroup.Cues.EnumerateAll());
            }

            visPos++;
        }

        OnCollectionChanged();
    }
    #endregion
}

