using QPlayer.Models;
using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Windows.Input;

namespace QPlayer.ViewModels;

/*
 All the cue stack manipulation methods in the MainViewModel.
 */
public partial class MainViewModel
{
    /// <summary>
    /// Deletes the cue at the given index from the cue stack.
    /// </summary>
    /// <param name="index">The 0-based index of the cue in the cue stack.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns>The cue which was just removed, or null if it wasn't found.</returns>
    public CueViewModel? DeleteCue(int index, bool recordUndo = true)
    {
        var cue = cues.Delete(index);
        if (cue == null)
            return null;

        if (recordUndo)
            UndoManager.RegisterAction($"Deleted cue '{cue.Name}' ({cue.FullQID})",
                () => { SelectedCue = null; InsertCue(index, cue, true, false); },
                () => DeleteCue(index));
        return cue;
    }

    /// <summary>
    /// Deletes the given cue from the cue stack.
    /// </summary>
    /// <param name="cue">The cue instance to remove from the cue stack.</param>
    /// <returns><see langword="true"/> if the cue was successfully removed.</returns>
    public bool DeleteCue(CueViewModel cue)
    {
        int ind = FindCueIndex(cue);
        if (ind == -1)
            return false;
        DeleteCue(ind);
        return true;
    }

    /// <summary>
    /// Deletes the given cue from the cue stack.
    /// </summary>
    /// <param name="cue">The cue instance to remove from the cue stack.</param>
    /// <returns><see langword="true"/> if the cue was successfully removed.</returns>
    public bool DeleteCue(string qid)
    {
        if (!FindCue(qid, out var cue))
            return false;

        return DeleteCue(cue);
    }

    /// <summary>
    /// Deletes the cues at the given index from the cue stack.
    /// </summary>
    /// <param name="startIndex">The 0-based index of the first cue in the cue stack to delete.</param>
    /// <param name="count">The number of cues to delete.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns>The cues which were just removed, or null if they weren't found.</returns>
    public CueViewModel[] DeleteCues(int startIndex, int count = 1, bool recordUndo = true)
    {
        if (count == 0 || startIndex < 0 || startIndex >= cues.Count)
            return [];

        if (count == 1 && cues[startIndex] is not GroupCueViewModel)
        {
            // Simple case where only one cue is being deleted
            var single = DeleteCue(startIndex, recordUndo);
            return single == null ? [] : [single];
        }

        int lastInd = startIndex + count - 1;
        if (lastInd >= cues.Count)
            return [];

        var deleted = cues.Delete(Enumerable.Range(startIndex, count), needsSorting: false);
        deleted = DeduplicateCues(deleted).ToArray();

        if (recordUndo)
            UndoManager.RegisterAction($"Deleted {deleted.Length} cues",
                () => InsertCues([.. Enumerable.Range(startIndex, deleted.Length)], deleted, true),
                () => DeleteCues(startIndex, count, false));
        return deleted;
    }

    /// <summary>
    /// Deletes the given cues by index from the cue stack.
    /// </summary>
    /// <param name="indices">The indices to delete.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns>An array of the deleted cues.</returns>
    public CueViewModel[] DeleteCues(int[] indices, bool recordUndo = true)
    {
        if (indices.Length == 0)
            return [];
        if (indices.Length == 1)
        {
            var single = DeleteCue(indices[0], recordUndo);
            return single == null ? [] : [single];
        }

        indices.Sort(); // The indices need to be sorted such that we can delete the cues by index correctly
        // Indices are invalid, abort
        if (indices[0] < 0 || indices[^1] >= cues.Count)
            return [];

        var deleted = cues.Delete(indices, needsSorting: false);
        deleted = DeduplicateCues(deleted).ToArray();

        if (recordUndo)
            UndoManager.RegisterAction($"Deleted {deleted.Length} cues",
                () => InsertCues(indices, deleted, true, false),
                () => DeleteCues(indices, false));
        return deleted;
    }

    /// <summary>
    /// Inserts an existing cue into the cue stack. This method should be used with care as 
    /// it does not check if the cue already exists in the stack. To duplicate a cue, use 
    /// <see cref="DuplicateCue(CueViewModel?)"/>.
    /// </summary>
    /// <param name="index">The index at which to insert the cue.</param>
    /// <param name="cue">The cue to insert.</param>
    /// <param name="select">Whether the newly inserted cue should be selected.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    /// <param name="insertIntoGroup">If <paramref name="index"/> succeeds the index of a cue in a group, then this 
    /// option will attempt to insert the <paramref name="cue"/> into that group.</param>
    public void InsertCue(int index, CueViewModel cue, bool select = false, bool registerUndo = true, bool insertIntoGroup = false)
    {
        index = Math.Clamp(index, 0, cues.Count);
        Cues.Insert(index, cue, insertIntoGroup);
        if (registerUndo)
            UndoManager.RegisterAction($"Inserted cue '{cue.Name}' ({cue.FullQID})",
                () => DeleteCue(index),
                () => InsertCue(index, cue));
        if (select)
            SelectedCueInd = index;
    }

    /// <summary>
    /// Inserts existing cues into the cue stack. This method should be used with care as 
    /// it does not check if the cues already exists in the stack. To duplicate a cue, use 
    /// <see cref="DuplicateCue(CueViewModel?)"/>.
    /// </summary>
    /// <param name="indices">The indices at which to insert the cues.</param>
    /// <param name="cues">The cues to insert.</param>
    /// <param name="select">Whether the newly inserted cues should be selected.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    public void InsertCues(int[] indices, CueViewModel[] cues, bool select = false, bool registerUndo = true)
    {
        Cues.Insert(indices, cues, collectResults: false);

        if (registerUndo)
            UndoManager.RegisterAction($"Inserted {cues.Length} cues",
                () => DeleteCues(indices, false),
                () => InsertCues(indices, cues, select, false));
        if (select)
            MultiSelect(indices, true);
    }

    /// <summary>
    /// Duplicates the given cue in the cue stack.
    /// </summary>
    /// <param name="src">The cue to duplicate, or <see langword="null"/> to use the currently selected cue.</param>
    /// <param name="select">Whether the newly created cue should be selected.</param>
    /// <returns>The instance of the newly created cue, or <see langword="null"/> if no cue was duplicated.</returns>
    public CueViewModel? DuplicateCue(CueViewModel? src = null, bool select = false)
    {
        src ??= SelectedCue;
        if (src == null)
            return null;

        return CreateCue(src.TypeName, false, false, select, src);
    }

    /// <inheritdoc cref="DuplicateCues(IEnumerable{CueViewModel}, int, bool, bool)"/>
    public void DuplicateCues(IEnumerable<CueViewModel> cues, bool select = true, bool registerUndo = true)
    {
        int dstInd;
        if (cues is ISet<CueViewModel> set)
        {
            var allCues = this.cues;
            for (dstInd = allCues.Count - 1; dstInd >= 0; dstInd--)
            {
                if (set.Contains(allCues[dstInd]))
                    break;
            }
            dstInd++;
        }
        else
        {
            dstInd = cues.Max(x => FindCueIndex(x)) + 1;
        }

        DuplicateCues(cues, dstInd, select, registerUndo);
    }

    /// <summary>
    /// Copies the given collection of cues to the new index.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="index"></param>
    /// <param name="select"></param>
    /// <param name="registerUndo"></param>
    public void DuplicateCues(IEnumerable<CueViewModel> cues, int index, bool select = true, bool registerUndo = true)
    {
        UndoManager.SuppressRecording();
        index = Math.Clamp(index, 0, Cues.Count);
        var srcArray = cues.ToArray();
        int i = index;
        foreach (var cue in srcArray)
        {
            var copy = CreateCue(cue.TypeName, i, false, cue);
            i++;
        }
        MultiSelect(index, srcArray.Length, true);
        UndoManager.UnSuppressRecording();

        if (registerUndo)
        {
            UndoManager.RegisterAction($"Duplicated {srcArray.Length} cues",
                () => DeleteCues(index, srcArray.Length, false),
                () => DuplicateCues(srcArray, index, select, false));
        }
    }

    /// <summary>
    /// Tries to find a cue view model given a cue ID.
    /// </summary>
    /// <remarks>
    /// The <paramref name="id"/> can be one of the following types:
    /// <see langword="int"/>,
    /// <see langword="float"/>,
    /// <see langword="decimal"/>,
    /// <see langword="string"/>,
    /// </remarks>
    /// <param name="id">The cue ID to search for.</param>
    /// <param name="cue">The returned cue view model if it was found.</param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool FindCue(object id, [NotNullWhen(true)] out CueViewModel? cue)
    {
        cue = null;
        switch (id)
        {
            case string idString:
                return cues.Find(idString.ToString(numberFormat), out cue);
            case int idInt:
                return cues.Find(idInt.ToString(numberFormat), out cue);
            case float idFloat:
                return cues.Find(decimal.CreateTruncating(idFloat).ToString(numberFormat), out cue);
            case decimal idDec:
                return cues.Find(idDec.ToString(numberFormat), out cue);
            default:
                cue = null;
                Log($"Couldn't find cue with ID: {id}!", LogLevel.Warning);
                return false;
        }
    }

    /// <summary>
    /// Tries to find a cue view model given a cue ID.
    /// </summary>
    /// <param name="id">The cue ID to search for.</param>
    /// <param name="cue">The returned cue view model if it was found.</param>
    /// <returns><see langword="true"/> if the cue was found.</returns>
    public bool FindCue(decimal id, [NotNullWhen(true)] out CueViewModel? cue) => cues.Find(id.ToString(numberFormat), out cue);

    /// <summary>
    /// Gets the index of a cue in the cue list. 
    /// </summary>
    /// <param name="cue">The cue instance to search for.</param>
    /// <returns>The index of the cue in the <see cref="Cues"/> list or <c>-1</c> if it wasn't found.</returns>
    public int FindCueIndex(CueViewModel? cue)
    {
        if (cue == null)
            return -1;
        // If we could guarantee that the cue list was in order, we could use a binary
        // search, but this can't be guaranteed. If there's ever a need, we could also
        // maintain an index dictionary.
        return cues.FindVisualIndex(cue);
    }

    /// <summary>
    /// Moves the given cue up or down by one, swapping position with the cue above or below it. 
    /// Renumbers the given cue to remain in order.
    /// </summary>
    /// <param name="cue">The cue instance to move.</param>
    /// <param name="down">Whether the cue should be moved up or down.</param>
    /// <param name="select">Whether the cue should be reselected after it's moved.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool MoveCue(CueViewModel cue, bool down, bool select = true, bool registerUndo = true)
    {
        int ind = FindCueIndex(cue);
        if (ind == -1)
            return false;

        int dir = down ? 2 : -1;
        return MoveCue(ind, ind + dir, select, null, registerUndo);
    }

    /// <summary>
    /// Moves the selected cues up or down by one, swapping position with the cues above or below them. 
    /// Renumbers the given cue to remain in order.
    /// </summary>
    /// <param name="down">Whether the cues should be moved up or down.</param>
    /// <param name="select">Whether the cues should be reselected after they're moved.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    public void MoveSelectedCues(bool down, bool select = true, bool registerUndo = true)
    {
        // Fidn the index at which to move the cues.
        int dstInd;
        var selected = multiSelection;
        var allCues = cues;
        if (!down)
        {
            for (dstInd = 0; dstInd < allCues.Count; dstInd++)
            {
                if (selected.Contains(allCues[dstInd]))
                    break;
            }
            dstInd--;
        }
        else
        {
            for (dstInd = allCues.Count - 1; dstInd >= 0; dstInd--)
            {
                if (selected.Contains(allCues[dstInd]))
                    break;
            }
            dstInd += 2;
        }

        MoveCues(multiSelection, dstInd, select, registerUndo);
    }

    /// <summary>
    /// Moves the given collection of cues to the new index.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="index"></param>
    /// <param name="select"></param>
    /// <param name="registerUndo"></param>
    public void MoveCues(IEnumerable<CueViewModel> cues, int index, bool select = true, bool registerUndo = true)
    {
        if (cues.TryGetNonEnumeratedCount(out var count) && count <= 1)
        {
            using var cuesEnum = cues.GetEnumerator();
            if (count == 1 && cuesEnum.MoveNext())
                MoveCue(cuesEnum.Current, index, select, null, registerUndo);
            return;
        }

        UndoManager.SuppressRecording();
        index = Math.Clamp(index, 0, Cues.Count);
        var srcIndices = cues.Select(x => FindCueIndex(x)).ToArray();
        srcIndices.Sort();
        var srcArray = srcIndices.Select(x => this.cues[x]).ToArray();
        var srcQIDs = srcArray.Select(x => x.QID).ToArray();

        MoveCuesInternal(srcArray, srcIndices, index, out int removedBefore);

        if (select)
            MultiSelect(Enumerable.Range(index - removedBefore, srcIndices.Length));
        UndoManager.UnSuppressRecording();

        if (registerUndo)
        {
            UndoManager.RegisterAction($"Moved {srcArray.Length} cues",
                () => MoveCuesBack(srcArray, srcIndices, srcQIDs),
                () => MoveCues(srcArray, index, select, false));
        }
    }

    /// <summary>
    /// Moves an ordered array of cues to the new index <paramref name="index"/> in the cue stack.
    /// This method assumes that <paramref name="cues"/> and <paramref name="indices"/> are already 
    /// sorted.
    /// </summary>
    /// <param name="cues">The sorted array of cues.</param>
    /// <param name="indices">The sorted array of cue indices. Enumerables which implement <see cref="IReadOnlyList{T}"/> are preferred.</param>
    /// <param name="index">The starting index to move the cues to.</param>
    /// <param name="removedBefore">The number of cues which were at an index smaller than <paramref name="index"/>.</param>
    /// <param name="moveIntoGroup">If <paramref name="index"/> succeeds the index of a cue in a group, then this 
    /// option will attempt to move the cues into that group.</param>
    private void MoveCuesInternal(CueViewModel[] cues, IEnumerable<int> indices, int index, out int removedBefore, bool moveIntoGroup = false)
    {
        removedBefore = 0;
        foreach (var ind in indices.FastReverse())
        {
            DeleteCue(ind, false);
            if (ind < index)
                removedBefore++;
        }
        int i = index - removedBefore;
        foreach (var cue in cues)
        {
            cue.QID = ChooseQID(i - 1, false);
            InsertCue(i, cue, false, false, moveIntoGroup);
            i++;
        }
    }

    private void MoveCuesBack(CueViewModel[] cues, int[] indices, decimal[] qids)
    {
        using var _ = UndoManager.ScopedSuppress();
        foreach (var cue in cues)
            DeleteCue(FindCueIndex(cue), false);
        for (int i = 0; i < cues.Length; i++)
        {
            var cue = cues[i];
            cue.QID = qids[i];
            InsertCue(indices[i], cue, true, false);
        }
    }

    /// <summary>
    /// Moves the given cue in the cue stack to the specified index.
    /// </summary>
    /// <param name="cue">The cue to move in the cue stack.</param>
    /// <param name="index">The index within the cue stack to move the cue to.</param>
    /// <param name="select">Whether the moved cue should be reselected.</param>
    /// <param name="newQID">Optionally, the new QID to assign to the cue once it's moved. 
    /// Otherwise, this method will choose a new QID itself (recommended).</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    /// <returns><see langword="true"/> if the cue was moved successfully.</returns>
    public bool MoveCue(CueViewModel cue, int index, bool select = true, decimal? newQID = null, bool registerUndo = true)
    {
        // Find the src and dst indices
        index = Math.Clamp(index, 0, Cues.Count);
        int srcIndex = FindCueIndex(cue);
        if (srcIndex == -1)
            return false;

        return MoveCue(srcIndex, index, select, newQID, registerUndo);
    }

    /// <summary>
    /// Moves the given cue in the cue stack to the specified index.
    /// </summary>
    /// <remarks>
    /// The insertion index behaves a little bit odd compared to regular insertion behaviour. 
    /// To illustrate:
    /// <code>
    /// srcInd:  [0] [1] [2] [3] [4]
    /// dstInd: 0   1   2   3   4   5
    /// </code>
    /// The cue is always inserted at an index 'between' two cues, so if we want to move q1 to 
    /// before q0, we would pick index 0, but to move it after q2, we would pick index 3. 
    /// Effectively, dstInd 1 and 2 don't move the cue.
    /// </remarks>
    /// <param name="cue">The cue to move in the cue stack.</param>
    /// <param name="dstIndex">The index within the cue stack to move the cue to.</param>
    /// <param name="select">Whether the moved cue should be reselected.</param>
    /// <param name="newQID">Optionally, the new QID to assign to the cue once it's moved. 
    /// Otherwise, this method will choose a new QID itself (recommended).</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    /// <returns><see langword="true"/> if the cue was moved successfully.</returns>
    public bool MoveCue(int srcIndex, int dstIndex, bool select = true, decimal? newQID = null, bool registerUndo = true)
    {
        int origDst = dstIndex;
        int origSrc = srcIndex;
        if (dstIndex > srcIndex)
        {
            dstIndex--;
        }
        else
        {
            origSrc++;
        }

        // Find the src and dst indices
        dstIndex = Math.Clamp(dstIndex, 0, cues.Count);
        if (srcIndex < 0 || srcIndex >= cues.Count)
            return false;

        // The cue doesn't need to be moved, don't do anything.
        if (srcIndex == dstIndex)
            return false;

        var cue = DeleteCue(srcIndex, false);
        if (cue == null)
        {
            Log($"Couldn't move cue from index {srcIndex} to {dstIndex}! This is probably a bug in QPlayer. Please re-load your showfile to avoid corruption and file a bug report.", LogLevel.Warning);
            return false;
        }

        var oldQID = cue.QID;
        var oldFullQID = cue.FullQID;
        using (UndoManager.ScopedSuppress())
            cue.QID = newQID ?? ChooseQID(dstIndex - 1, false);

        InsertCue(dstIndex, cue, select, false);

        if (registerUndo)
            UndoManager.RegisterAction($"Moved cue Q{oldFullQID} --> Q{cue.FullQID}",
                () => MoveCue(dstIndex, origSrc, select, oldQID, false),
                () => MoveCue(srcIndex, origDst, select, null, false));

        if (select)
            SelectedCueInd = dstIndex;

        return true;
    }

    /// <summary>
    /// Creates a new cue of the given type and inserts it into the cue stack at the selected position.
    /// </summary>
    /// <param name="type">The type of cue to create.</param>
    /// <param name="beforeCurrent">Whether the new cue should be inserted before the selected cue.</param>
    /// <param name="afterLast">Whether the new cue should be inserted at the end of the cue stack.</param>
    /// <param name="select">Whether the new cue should be selected.</param>
    /// <param name="src">Optionally, a cue to copy properties from.</param>
    /// <returns>The view model instance of the newly created cue.</returns>
    public CueViewModel? CreateCue(string? type, bool beforeCurrent = false, bool afterLast = false, bool select = true, CueViewModel? src = null)
    {
        if (string.IsNullOrEmpty(type))
            return null;

        int ind = SelectedCueInd + 1;
        if (beforeCurrent)
            ind--;
        if (afterLast)
            ind = cues.Count;

        return CreateCue(type, ind, select, src);
    }

    /// <summary>
    /// Creates a new cue of the given type and inserts it into the cue stack at the given position.
    /// </summary>
    /// <param name="type">The type of cue to create.</param>
    /// <param name="index">The index in the cue stac to insert the cue</param>
    /// <param name="select">Whether the newly created cue should be selected.</param>
    /// <param name="src">Optionally, a cue to copy properties from.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns>The view model instance of the newly created cue.</returns>
    public CueViewModel? CreateCue(string? type, int index, bool select = true, CueViewModel? src = null, bool recordUndo = true)
    {
        index = Math.Clamp(index, 0, cues.Count);

        // No need to suppress undo here, these methods already suppress it.
        var qid = ChooseQID(index - 1);
        var cue = CreateCueNoInsert(type, qid, src);
        if (cue == null)
            return null;

        InsertCue(index, cue, select, false);

        if (recordUndo)
        {
            string action = src == null ? $"Created {cue.TypeDisplayName}" : $"Duplicated '{cue.Name}'";
            UndoManager.RegisterAction($"{action} ({cue.FullQID})",
                () => DeleteCue(index, false),
                () => InsertCue(index, cue, select, false));
        }

        return cue;
    }

    /// <summary>
    /// Creates a new cue of the given type without inserting it into the cue stack. Use this method with caution as 
    /// QPlayer assumes cues are always in the cue stack.
    /// </summary>
    /// <param name="type">The type of cue to create.</param>
    /// <param name="src">Optionally, copies properties from this cue.</param>
    /// <returns>The view model instance of the newly created cue.</returns>
    public CueViewModel? CreateCueNoInsert(string? type, decimal? qid = null, CueViewModel? src = null)
    {
        if (string.IsNullOrEmpty(type))
            return null;

        using var _ = UndoManager.ScopedSuppress();

        CueViewModel? ret;
        Cue? model;
        if (src != null)
            model = CueFactory.CreateCueForViewModel(src, true);
        else
            model = CueFactory.CreateCue(type);

        if (model == null)
            return null;

        model.qid = qid ?? -1;
        ret = CueFactory.CreateViewModelForCue(model, this);

        return ret;
    }

    private IEnumerable<CueViewModel> EnumerateParents(CueViewModel cue)
    {
        var parent = cue;
        while ((parent = parent.Parent) != null)
            yield return parent;
    }

    private IEnumerable<CueViewModel> DeduplicateCues(IEnumerable<CueViewModel> src)
    {
        var hashset = new HashSet<CueViewModel>(src);
        foreach (var cue in src)
        {
            foreach (var parent in EnumerateParents(cue))
            {
                if (hashset.Contains(parent))
                {
                    hashset.Remove(cue);
                    break;
                }
            }
        }
        return hashset;
    }

    /// <summary>
    /// Groups a number of cues into a target cue. If the target cue is a group cue, the new cues are appended 
    /// to the end of the group. If the target cue is not a group cue, then a new group cue is created 
    /// containing the target cue followed by the other cues.
    /// </summary>
    /// <param name="cues">An enumerable of cues already in the cue stack to group.</param>
    /// <param name="target">The cue to group into.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    public void GroupCues(IEnumerable<CueViewModel> cues, CueViewModel target, bool registerUndo = true)
    {
        if (target is not GroupCueViewModel group)
        {
            var toGroup = GetCuesToGroup().ToArray();
            var targetInd = FindCueIndex(target);
            GroupCues(toGroup, targetInd, registerUndo, needsSorting: false);
            return;
        }

        var deleted = Cues.Delete(GetCuesExceptTarget());
        if (deleted.Length == 0)
            return;
        Cues.Insert(group.Cues.CreateCuePosition(group.Cues.Count), DeduplicateCues(deleted));

        IEnumerable<CueViewModel> GetCuesToGroup()
        {
            yield return target;
            foreach (var cue in cues)
                if (cue != target)
                    yield return cue;
        }

        IEnumerable<CueViewModel> GetCuesExceptTarget()
        {
            foreach (var cue in cues)
                if (cue != target)
                    yield return cue;
        }
    }

    /// <summary>
    /// Creates a new group cue containing the specified cues.
    /// </summary>
    /// <param name="cues">The cues to put in the group, the order within the cue stack of these cues is maintained.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    public void GroupCues(IEnumerable<CueViewModel> cues, bool registerUndo = true)
    {
        var (srcIndices, srcCues) = Cues.SortCues(cues);

        if (srcIndices.Length == 0)
            return;

        GroupCues(srcCues, srcIndices[0], registerUndo, false, srcIndices);
    }

    /// <summary>
    /// Creates a new group cue containing the specified cues.
    /// </summary>
    /// <remarks>
    /// If <paramref name="needsSorting"/> is false, then the caller must ensure that all the cues in 
    /// <paramref name="cues"/> are in the visual cue list.
    /// </remarks>
    /// <param name="cues">The ordered indices of the cues to add to the group.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    /// <param name="needsSorting">Whether the cues array needs sorting by visual index.</param>
    public void GroupCues(CueViewModel[] cues, int dstIndex, bool registerUndo = true, bool needsSorting = true, int[]? cueIndices = null)
    {
        if (cues.Length == 0)
            return;

        if (needsSorting) // Expensive
            (cueIndices, cues) = Cues.SortCues(cues);

        cueIndices ??= [.. cues.Select(x => FindCueIndex(x))];

        // Find the lowest common parent of the given cues
        var parent = cues[0].Parent;
        foreach (var cue in cues)
        {
            if (parent == null)
                break;
            while (parent != null && !cue.HasParent(parent))
                parent = parent.Parent;
        }

        UndoManager.SuppressRecording();

        // Find an appropriate place to put the group cue
        if (dstIndex > 0 || dstIndex < Cues.Count)
        {
            // We're not in the target group at this index, try to reduce the index till we're out of said group.
            while (Cues[dstIndex].Parent != parent
                && Cues.Find(dstIndex, out var pos))
            {
                dstIndex -= pos.index + 1;
            }
        }

        var groupQid = ChooseQID(dstIndex, true);
        var group = (GroupCueViewModel)CreateCueNoInsert(nameof(GroupCue), groupQid)!;

        Cues.Delete(cueIndices, false);
        group.Cues.InsertInternal(0, cues);
        InsertCue(dstIndex, group, select: true, registerUndo: false);
        RenumberCues(group, false);

        UndoManager.UnSuppressRecording();

        if (registerUndo)
        {
            UndoManager.RegisterAction($"Grouped {cues.Length} cues",
                () => UngroupCues(group, false),
                () => GroupCues(cues, dstIndex, false, false, cueIndices));
        }
    }

    /// <summary>
    /// Ungroups the given cues by removing each cue in the given list and reinserting 
    /// it just before it's group cue. Empty group cues encountered in doing so are
    /// deleted.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void UngroupCues(IEnumerable<CueViewModel> cues, bool recordUndo = true)
    {
        var deleted = Cues.Delete(cues.Where(CueHasParent));
        foreach (var cue in DeduplicateCues(deleted).FastReverse())
        {
            if (cue.Parent is not GroupCueViewModel group)
                continue;

            if (Cues.Find(group, out var groupPos))
            {
                // If the group is empty, remove it
                if (group.Cues.IsEmpty)
                    Cues.Delete(groupPos);
                // Reinsert the ungrouped cue just before it's old group.
                Cues.Insert(groupPos, cue);
            }
        }

        static bool CueHasParent(CueViewModel x) => x.HasParent();
    }

    /// <summary>
    /// Deletes this group cue and reinserts it's contents where the group used to be.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void UngroupCues(GroupCueViewModel group, bool recordUndo = true)
    {
        if (!Cues.Find(group, out var pos))
            return;

        Cues.Delete(pos);
        Cues.Insert(pos, group.Cues);
    }

    /*
     Choose QID spec:

    [0, 1, 2, 3, 4, 5]
        |
     c  a  b
    Find the QID of the given index and the one after it.
    Try to generate a new ID between these two values (a, b).
    If ignoreCurrent is set, pick between (c, b).
    To generate a new ID, take a and increment by 1, 0.1, 
    0.01, 0.001, ... until it's less than b.

    [0, .1, .2, .3, 2, 4]
         c   a   b
    When adding to a group decrease the increment by an order 
    of magnitude of the group depth of the insertion position.
     */

    private decimal ChooseQID(AbstractCueList.CuePosition insertAfter, bool replace = false)
    {
        var list = (AbstractCueList?)insertAfter.group?.Cues ?? Cues;
        var rootList = list.RootCueList;

        var prevInd = replace ? insertAfter.index - 1 : insertAfter.index;
        var prev = prevInd >= 0 && prevInd < rootList.Count ? rootList[prevInd] : null;
        var nextInd = insertAfter.index + 1;
        var next = nextInd >= 0 && nextInd < rootList.Count ? rootList[nextInd] : null;

        var prevId = prev != null ? prev.QID : (insertAfter.group != null ? insertAfter.group.QID : 0);
        var nextId = next != null ? next.QID : decimal.MaxValue;
        decimal newId = 0;

        var increment = 1m;
        for (int i = 0; i < 6; i++)
        {
            newId = ((int)(prevId / increment) * increment) + increment;
            if (newId < nextId)
                return newId.Normalize();
            increment *= 0.1m;
        }

        // No suitable cue ID could be found, renumber subsequent cues to fit this one in
        /*decimal last = newId;
        for (int i = insertBeforeInd; i < Cues.Count; i++)
        {
            var next = Cues[i].QID;
            if (next > last)
                break;
            Cues[i].QID = last = (next + increment).Normalize();
        }*/

        return newId.Normalize();
    }

    /// <summary>
    /// Generates a QID at the given insertion point in the cue stack, renumbering cues if needed.
    /// </summary>
    /// <param name="insertAfterInd">The index of the cue after which to insert the new QID.</param>
    /// <param name="ignoreCurrent">When enabled the first parameter is the index of the cue to 
    /// renumber such that it fits in between it's neighbours.</param>
    /// <returns></returns>
    private decimal ChooseQID(int insertAfterInd, bool ignoreCurrent = false)
    {
        if (insertAfterInd < 0)
            return ChooseQID(default(AbstractCueList.CuePosition), ignoreCurrent);
        if (insertAfterInd >= Cues.Count)
            return ChooseQID(Cues.CreateCuePosition(Cues.RootCueList.Count - 1), ignoreCurrent);
        Cues.Find(Cues[insertAfterInd], out var pos);
        return ChooseQID(pos, ignoreCurrent);
    }

    /// <summary>
    /// Renumbers the specified cues.
    /// </summary>
    /// <param name="cueInds"></param>
    /// <param name="startID"></param>
    /// <param name="increment"></param>
    /// <param name="recordUndo"></param>
    public void RenumberCues(IEnumerable<int> cueInds, decimal startID = -1, decimal increment = -1, bool recordUndo = true)
    {
        using TemporaryList<int> inds = new(cueInds);
        if (inds.Count == 0)
            return;

        using var _ = UndoManager.ScopedGroup($"Renumbered {inds.Count} cues");

        inds.Sort();

        // Once converting from visual index --> CuePosition is faster, then this code should just use
        // CuePositions instead of searching the list for parents.
        int ind = -1;
        for (int i = 0; i < inds.Count; i++)
        {
            var last = ind == -1 ? null : cues[ind];
            var lastQID = (int?)last?.QID ?? 0;
            ind = inds[i];
            var cue = cues[ind];

            var lastParent = last?.Parent;
            var parent = cue.Parent;
            if (parent != lastParent)
            {
                if (parent != null && parent.Parent == lastParent)
                {
                    // Start of a new group, restart numbering
                    lastQID = 0;
                }
                else
                {
                    // Search for the last cue at this hierarchy level
                    for (int j = ind - 1; j >= 0; j--)
                    {
                        var cand = cues[j];
                        if (cand.Parent == parent)
                        {
                            lastQID = (int)cand.QID;
                            break;
                        }
                    }
                }
            }

            cue.QID = lastQID + 1;
        }

        return;
    }

    public void RenumberCues(GroupCueViewModel group, bool recordUndo = true)
    {
        using IDisposable _ = recordUndo ? UndoManager.ScopedSuppress() : UndoManager.ScopedGroup($"Renumbered {group.Cues.Count} cues");

        var qid = 1m;
        foreach (var cue in group.Cues.RootCueList)
        {
            cue.QID = qid++;
        }
    }

    /// <summary>
    /// Selects a cue by reference, applying multiselection rules based on the current keyboard modifier keys.
    /// </summary>
    /// <remarks>This method may be moved to the MainWindow class in the future. 
    /// The <see cref="SelectionMode"/> field can also be used to control multi-selection behaviour.</remarks>
    /// <param name="cue">The cue to select.</param>
    public void MultiSelect(CueViewModel? cue) => MultiSelect(FindCueIndex(cue));

    /// <summary>
    /// Selects a cue by index, applying multiselection rules based on the current keyboard modifier keys.
    /// </summary>
    /// <remarks>This method may be moved to the MainWindow class in the future. 
    /// The <see cref="SelectionMode"/> field can also be used to control multi-selection behaviour.</remarks>
    /// <param name="cueInd">The index of the cue to select.</param>
    public void MultiSelect(int cueInd)
    {
        var modifiers = InputManager.Current.PrimaryKeyboardDevice.Modifiers;
        modifiers &= ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt;
        var selMode = modifiers switch
        {
            ModifierKeys.Control => SelectionMode.Toggle,
            ModifierKeys.Shift => SelectionMode.Range,
            ModifierKeys.Control | ModifierKeys.Shift => SelectionMode.Add,
            ModifierKeys.Control | ModifierKeys.Alt => SelectionMode.Subtract,
            _ => SelectionMode.Normal,
        };

        MultiSelect(cueInd, selMode);
    }

    /// <summary>
    /// Selects all of the given cues by index.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="replace">Whether the existing selection should be replaced.</param>
    public void MultiSelect(IEnumerable<CueViewModel> cues, bool replace = true)
    {
        var toNotify = new TemporaryList<CueViewModel>();

        var prev = selectedCueInd;
        var clear = Cues.Count;
        if (replace)
        {
            HandleSelection(prev, ref clear, SelectionMode.Normal, ref toNotify);
            prev = clear;
        }

        int value = prev;
        foreach (var ind in cues.FastReverse())
        {
            value = FindCueIndex(ind);
            HandleSelection(prev, ref value, SelectionMode.Add, ref toNotify);
            prev = value;
        }

        NotifySelection(prev, value, ref toNotify);
        toNotify.Dispose();
    }

    /// <summary>
    /// Selects all the cues in the given range.
    /// </summary>
    /// <param name="startInd">The first cue index in the list to select.</param>
    /// <param name="count">The number of contiguous cues to select.</param>
    /// <param name="replace">Whether the existing selection should be replaced.</param>
    public void MultiSelect(int startInd, int count, bool replace = true)
    {
        var toNotify = new TemporaryList<CueViewModel>();

        var prev = selectedCueInd;
        var clear = cues.Count;
        if (replace)
        {
            HandleSelection(prev, ref clear, SelectionMode.Normal, ref toNotify);
            prev = clear;
        }

        int value = prev;
        for (int i = startInd + count - 1; i >= startInd; i--)
        {
            value = i;
            HandleSelection(prev, ref value, SelectionMode.Add, ref toNotify);
            prev = value;
        }

        NotifySelection(prev, value, ref toNotify);
        toNotify.Dispose();
    }

    /// <summary>
    /// Selects all of the given cues by index.
    /// </summary>
    /// <param name="cueInds"></param>
    /// <param name="replace">Whether the existing selection should be replaced.</param>
    public void MultiSelect(IEnumerable<int> cueInds, bool replace = true)
    {
        var toNotify = new TemporaryList<CueViewModel>();

        var prev = selectedCueInd;
        var clear = cues.Count;
        if (replace)
        {
            HandleSelection(prev, ref clear, SelectionMode.Normal, ref toNotify);
            prev = clear;
        }

        int value = prev;
        foreach (var ind in cueInds.FastReverse())
        {
            value = ind;
            HandleSelection(prev, ref value, SelectionMode.Add, ref toNotify);
            prev = value;
        }

        NotifySelection(prev, value, ref toNotify);
        toNotify.Dispose();
    }

    /// <summary>
    /// Selects a cue by index, applying the given multiselection rules.
    /// </summary>
    /// <param name="cueInd">The index of the cue to select.</param>
    /// <param name="mode">The multiselection mode to use.</param>
    public void MultiSelect(int cueInd, SelectionMode mode)
    {
        var toNotify = new TemporaryList<CueViewModel>();

        var prev = selectedCueInd;
        HandleSelection(prev, ref cueInd, mode, ref toNotify);
        NotifySelection(prev, cueInd, ref toNotify);
        toNotify.Dispose();
    }

    private void NotifySelection(int prev, int value, ref TemporaryList<CueViewModel> toNotify)
    {
        selectedCueInd = value; // Set the backing field directly here as this method is used by the property's setter.
        // TODO: Currently we just notify all the cues for simplicity. The toNotify list is correct, but the UI requires
        // that directly adjacent cues are also notified to fix the selection outline.
        foreach (var cue in cues)
            NotifyCueSelectionChanged(cue);

        if (prev != value)
        {
            OnPropertyChanged(nameof(SelectedCue));
            SelectedCue?.OnFocussed();
        }
    }

    private void HandleSelection(int prevSelected, ref int selected, SelectionMode mode, ref TemporaryList<CueViewModel> cuesToNotify)
    {
        int nCues = cues.Count;
        selected = Math.Clamp(selected, 0, nCues);
        var prev = prevSelected < nCues ? cues[prevSelected] : null;

        if (selected == nCues)
        {
            cuesToNotify.AddRange(multiSelection);
            multiSelection.Clear();
            return;
        }

        var cue = cues[selected];

        switch (mode)
        {
            case SelectionMode.PassThrough:
                cuesToNotify.AddRange(multiSelection);
                cuesToNotify.Add(cue);
                multiSelection.Replace(cue);
                break;
            case SelectionMode.Normal:
                // Replace the current selection, unless the multiselection contains the new item
                // Currently, when clicking an already (primary) selected cue nothing happens, it
                // would be useful if doing so would clear the multiselection, but this breaks
                // drag & drop.
                if (/*prev != cue &&*/ multiSelection.Count > 1 && multiSelection.Contains(cue))
                {
                    if (prev != null)
                        cuesToNotify.Add(prev);
                    cuesToNotify.Add(cue);
                    break;
                }
                cuesToNotify.AddRange(multiSelection);
                cuesToNotify.Add(cue);
                multiSelection.Replace(cue);
                break;
            case SelectionMode.Add:
                if (prev != null)
                    cuesToNotify.Add(prev);
                cuesToNotify.Add(cue);
                multiSelection.Add(cue);
                break;
            case SelectionMode.Subtract:
                multiSelection.Remove(cue);
                FindNewPrimarySelection(ref selected, ref cuesToNotify);
                cuesToNotify.Add(cue); // The cue that was just removed
                break;
            case SelectionMode.Toggle:
                cuesToNotify.Add(cue);
                if (multiSelection.Remove(cue))
                    FindNewPrimarySelection(ref selected, ref cuesToNotify);
                else
                {
                    if (prev != null)
                        cuesToNotify.Add(prev);
                    multiSelection.Add(cue);
                }
                break;
            case SelectionMode.Range:
                int start, end;
                if (prevSelected < selected)
                {
                    start = prevSelected;
                    end = selected;
                }
                else
                {
                    start = selected;
                    end = prevSelected;
                }

                bool shouldRemove = multiSelection.Contains(cues[selected]);
                start = Math.Min(start, nCues - 1);
                end = Math.Min(end, nCues - 1);

                for (int i = start; i <= end; i++)
                {
                    var q = cues[i];
                    cuesToNotify.Add(q);
                    if (shouldRemove)
                        multiSelection.Remove(q);
                    else
                        multiSelection.Add(q);
                }
                break;
        }

        void FindNewPrimarySelection(ref int selected, ref TemporaryList<CueViewModel> cuesToNotify)
        {
            if (multiSelection.Count > 0 && selected == prevSelected)
            {
                // Try to find a new primary selection within the current multiselection
                selected--;
                while (selected >= 0 && !multiSelection.Contains(cues[selected]))
                    selected--;

                if (selected == -1)
                    selected = FindCueIndex(multiSelection.FirstOrDefault());

                cuesToNotify.Add(cues[selected]); // The new primary cue
            }
            else
                selected = prevSelected;
        }
    }

    private void NotifyCueSelectionChanged(CueViewModel? cue)
    {
        if (cue == null)
        {
            prevPrimarySelectedCue?.PropertyChanged -= OnMainSelectionPropertyChanged;
            prevPrimarySelectedCue = null;
            return;
        }

        cue.OnSelectionChanged();
        if (cue.IsSelected)
        {
            if (cue == prevPrimarySelectedCue)
                return;

            prevPrimarySelectedCue?.PropertyChanged -= OnMainSelectionPropertyChanged;
            cue.PropertyChanged += OnMainSelectionPropertyChanged;
            prevPrimarySelectedCue = cue;
        }
    }

    private void OnMainSelectionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (multiSelection.Count <= 1 || e.PropertyName == null || UndoManager.IsRecordingSuppressed)
            return;

        var mainSel = SelectedCue;
        if (mainSel == null)
        {
            Log($"Tried to change a property on an unselected cue unexpectedly! Please submit a bug report with the steps that led up to this. Restarting QPlayer is recommended to avoid potential corruption.", LogLevel.Warning);
            return;
        }

        if (!mainSel.IsPropertyUndoable(e.PropertyName))
            return;

#if DEBUG
        if (sender is CueViewModel q && !q.IsSelected)
        {
            Log($"Tried to propagate a prop change on a non-primary cue!", LogLevel.Error);
            return;
        }
#endif

        // This heuristic prevents accidental property copying during bulk operations
        if (UndoManager.IsRecordingSuppressed)
            return;

        using var _ = UndoManager.ScopedGroup($"Changed {e.PropertyName} on {multiSelection.Count} cues");
        // This is needed so that the prop change that triggered this sync gets grouped into the same undo action.
        UndoManager.PopLastUndoIntoGroup();

        foreach (var cue in multiSelection)
        {
            if (cue == mainSel)
                continue;

            cue.CopyRemoteProperty(mainSel, e.PropertyName);
        }
    }
}

/// <summary>
/// Defines the behaviour of the multi-selection system 
/// </summary>
public enum SelectionMode
{
    /// <summary>
    /// Selecting an item adds it to the multi-selection if the multi-selection already contains multiple items, otherwise it replaces it.
    /// </summary>
    PassThrough,
    /// <summary>
    /// Selecting an item clears any multi-selected items, unless the selected item is already in the multi-selection.
    /// </summary>
    Normal,
    /// <summary>
    /// Selecting an item adds it to the multi-selection list, or removes it if it is already selected.
    /// </summary>
    Toggle,
    /// <summary>
    /// Selecting an item adds it to the multi-selection list.
    /// </summary>
    Add,
    /// <summary>
    /// Selecting an item removes it from the multi-selection list
    /// </summary>
    Subtract,
    /// <summary>
    /// Selecting an item adds it, and any items between the last selection and this item to the multi-selection list.
    /// </summary>
    Range
}
