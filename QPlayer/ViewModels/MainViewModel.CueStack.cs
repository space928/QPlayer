using QPlayer.Models;
using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
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
        if (index < 0 || index >= cues.Count)
            return null;

        var cue = cues[index];
        Cues.RemoveAt(index);
        showFile.cues.RemoveAt(index);
        if (recordUndo)
            UndoManager.RegisterAction($"Deleted cue '{cue.Name}' ({cue.QID})",
                () => { SelectedCue = null; InsertCue(index, cue, true); },
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
    public bool DeleteCue(decimal qid)
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
        if (count == 1)
        {
            var single = DeleteCue(startIndex, recordUndo);
            return single == null ? [] : [single];
        }
        if (startIndex < 0)
            return [];

        using TemporaryList<CueViewModel> deleted = [];
        for (int i = count - 1; i >= 0; i--)
        {
            int ind = i + startIndex;
            if (ind >= cues.Count)
                break;

            var cue = cues[ind];
            Cues.RemoveAt(ind);
            showFile.cues.RemoveAt(ind);
            deleted.Add(cue);
        }

        var deletedArr = deleted.ToArray();
        if (recordUndo)
            UndoManager.RegisterAction($"Deleted {deletedArr.Length} cues",
                () => InsertCues([.. Enumerable.Range(startIndex, deletedArr.Length)], deletedArr, true),
                () => DeleteCues(startIndex, count, false));
        return deletedArr;
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

        using TemporaryList<CueViewModel> deleted = [];
        for (int i = indices.Length - 1; i >= 0; i--)
        {
            int ind = indices[i];
            var cue = cues[ind];
            Cues.RemoveAt(ind);
            showFile.cues.RemoveAt(ind);
            deleted.Add(cue);
        }

        var deletedArr = deleted.Reverse().ToArray();
        if (recordUndo)
            UndoManager.RegisterAction($"Deleted {deletedArr.Length} cues",
                () => InsertCues(indices, deletedArr, true, false),
                () => DeleteCues(indices, false));
        return deletedArr;
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
    public void InsertCue(int index, CueViewModel cue, bool select = false, bool registerUndo = true)
    {
        Cues.Insert(index, cue);
        showFile.cues.Insert(index, cue.BoundModel!);
        if (registerUndo)
            UndoManager.RegisterAction($"Inserted cue '{cue.Name}' ({cue.QID})", 
                () => DeleteCue(index), 
                () => InsertCue(index, cue));
        if (select)
            SelectedCueInd = index;
    }

    /// <summary>
    /// Inserts an existing cue into the cue stack according to it's qid. This method 
    /// should be used with care as it does not check if the cue already exists in the 
    /// stack. To duplicate a cue, use <see cref="DuplicateCue(CueViewModel?)"/>.
    /// </summary>
    /// <param name="cue">The cue to insert in the stack.</param>
    /// <param name="select">Whether the newly inserted cue should be selected.</param>
    /// <param name="registerUndo">Whether an undo item should be recorded.</param>
    public void InsertCue(CueViewModel cue, bool select = false, bool registerUndo = true)
    {
        int ind;
        var newId = cue.QID;
        for (ind = 0; ind < Cues.Count; ind++)
        {
            var qid = Cues[ind].QID;
            if (newId == qid)
                newId = ChooseQID(ind);
            if (newId >= qid)
                break;
        }
        cue.QID = newId;
        InsertCue(ind, cue, select, registerUndo);
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
        for (int i = 0; i < indices.Length; i++)
        {
            var ind = indices[i];
            var cue = cues[i];
            Cues.Insert(ind, cue);
            showFile.cues.Insert(ind, cue.BoundModel!);
        }

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
            dstInd = cues.Select(x => FindCueIndex(x)).Max() + 1;
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
            case int idInt:
                return cuesDict.TryGetValue(idInt, out cue);
            case float idFloat:
                return cuesDict.TryGetValue(decimal.CreateTruncating(idFloat), out cue);
            case decimal idDec:
                return cuesDict.TryGetValue(idDec, out cue);
            case string idString:
                if (decimal.TryParse(idString, numberFormat, out var idNum))
                    return cuesDict.TryGetValue(idNum, out cue);
                else
                    return false;
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
    public bool FindCue(decimal id, [NotNullWhen(true)] out CueViewModel? cue)
    {
        return cuesDict.TryGetValue(id, out cue);
    }

    /// <summary>
    /// Gets the index of a cue in the cue list. Executes in O(n) time.
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
        return cues.IndexOf(cue);
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
    /// Moves the given cue in the cue stack to the given cue ID, or directly after it if the 
    /// specified cue ID is already in use.
    /// </summary>
    /// <param name="cue">The cue to move in the cue stack.</param>
    /// <param name="newId">The cue ID to insert the cue at.</param>
    /// <param name="select">Whether the moved cue should be reselected.</param>
    public void MoveCue(CueViewModel cue, decimal newId, bool select = true)
    {
        int ind;
        for (ind = 0; ind < Cues.Count; ind++)
        {
            var qid = Cues[ind].QID;
            if (newId == qid)
                newId = ChooseQID(ind);
            if (newId >= qid)
                break;
        }

        MoveCue(cue, ind, select, newId);
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
        var allCues = this.cues;
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
        UndoManager.SuppressRecording();
        index = Math.Clamp(index, 0, Cues.Count);
        var srcIndices = cues.Select(x => FindCueIndex(x)).ToArray();
        srcIndices.Sort();
        var srcArray = srcIndices.Select(x => this.cues[x]).ToArray();
        var srcQIDs = srcArray.Select(x => x.QID).ToArray();
        int removedBefore = 0;
        foreach (var ind in srcIndices.FastReverse())
        {
            DeleteCue(ind, false);
            if (ind < index)
                removedBefore++;
        }
        int i = index - removedBefore;
        foreach (var cue in srcArray)
        {
            cue.QID = ChooseQID(i - 1, false);
            InsertCue(i, cue, false, false);
            i++;
        }

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
        if (srcIndex < 0 || srcIndex > cues.Count)
            return false;

        // The cue doesn't need to be moved, don't do anything.
        if (srcIndex == dstIndex)
            return false;

        var cue = DeleteCue(srcIndex, false)!;
        InsertCue(dstIndex, cue, select, false);

        var oldQID = cue.QID;
        using (UndoManager.ScopedSuppress())
            cue.QID = newQID ?? ChooseQID(dstIndex, true);

        if (registerUndo)
            UndoManager.RegisterAction($"Moved cue Q{oldQID} --> Q{cue.QID}",
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
    /// <returns>The view model instance of the newly created cue.</returns>
    public CueViewModel? CreateCue(string? type, int index, bool select = true, CueViewModel? src = null)
    {
        index = Math.Clamp(index, 0, cues.Count);

        // No need to suppress undo here, these methods already suppress it.
        var qid = ChooseQID(index - 1);
        var cue = CreateCueNoInsert(type, qid, src);
        if (cue == null)
            return null;

        InsertCue(index, cue, select, false);

        string action = src == null ? $"Created {cue.TypeDisplayName}" : $"Duplicated '{cue.Name}'";
        UndoManager.RegisterAction($"{action} ({qid})", () => DeleteCue(index, false), () => InsertCue(index, cue, select, false));

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

    /// <summary>
    /// Renumbers the specified cues.
    /// </summary>
    /// <param name="cueInds"></param>
    /// <param name="startID"></param>
    /// <param name="increment"></param>
    /// <param name="recordUndo"></param>
    public bool RenumberCues(IEnumerable<int> cueInds, decimal startID = -1, decimal increment = -1, bool recordUndo = true)
    {
        using TemporaryList<int> inds = new(cueInds);
        if (inds.Count == 0)
            return true;

        using var _ = UndoManager.ScopedGroup($"Renumbered {inds.Count} cues");

        inds.Sort();
        // Check the cues are contiguous
        int last = inds[0];
        for (int i = 1; i < inds.Count; i++)
        {
            int j = inds[i];
            if (j - last != 1)
                return false;
        }

        decimal maxQID = inds[^1] + 1 < cues.Count ? cues[inds[^1] + 1].QID : decimal.MaxValue;
        decimal qid = startID;
        if (qid == -1)
            qid = inds[0] - 1 > 0 ? cues[inds[0] - 1].QID + 1 : 1;

        int k = inds[0];
        for (int i = inds.Count - 1; i >= 0; i--)
        {
            var cue = cues[k];
            if (qid != -1)
            {
                cue.QID = qid;
                if (increment == -1)
                {
                    qid++;
                    //if (qid >= maxQID)
                    //    qid = ChooseQID();
                }
                else
                {
                    qid += increment;
                }
            }
            k++;
        }

        return true;
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
        if (Cues.Count == 0)
            return 1;

        decimal newId = 1;
        decimal prevId = 0;
        decimal nextId = decimal.MaxValue;

        int insertBeforeInd = insertAfterInd + 1;
        if (ignoreCurrent)
        {
            insertAfterInd--;
            //insertBeforeInd++;
        }

        if (insertAfterInd >= 0)
            prevId = Cues[Math.Min(insertAfterInd, Cues.Count - 1)].QID;

        if (insertBeforeInd - 1 < Cues.Count - 1)
            nextId = Cues[Math.Max(insertBeforeInd, 0)].QID;

        decimal increment = 10;
        for (int i = 0; i < 4; i++)
        {
            increment *= 0.1m;
            newId = ((int)(prevId / increment) * increment) + increment;//(int)(prevId * increment) + increment;
            if (newId < nextId)
                return newId.Normalize();
        }

        // No suitable cue ID could be found, renumber subsequent cues to fit this one in
        decimal prev = newId;
        for (int i = insertBeforeInd; i < Cues.Count; i++)
        {
            var next = Cues[i].QID;
            if (next > prev)
                break;
            Cues[i].QID = prev = (next + increment).Normalize();
        }

        return newId.Normalize();
    }

    /// <summary>
    /// Selects all of the given cues by index.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="replace">Whether the existing selection should be replaced.</param>
    public void MultiSelect(IEnumerable<CueViewModel> cues, bool replace = true)
    {
        if (replace)
            multiSelection.Clear();
        SelectionMode = SelectionMode.Add;
        foreach (var cue in cues.FastReverse())
            SelectedCue = cue;
        SelectionMode = SelectionMode.Normal;
    }

    /// <summary>
    /// Selects all the cues in the given range.
    /// </summary>
    /// <param name="startInd">The first cue index in the list to select.</param>
    /// <param name="count">The number of contiguous cues to select.</param>
    /// <param name="replace">Whether the existing selection should be replaced.</param>
    public void MultiSelect(int startInd, int count, bool replace = true)
    {
        if (replace)
            multiSelection.Clear();
        SelectionMode = SelectionMode.Add;
        for (int i = startInd + count - 1; i >= startInd; i--)
            SelectedCueInd = i;
        SelectionMode = SelectionMode.Normal;
    }

    /// <summary>
    /// Selects all of the given cues by index.
    /// </summary>
    /// <param name="cueInds"></param>
    /// <param name="replace">Whether the existing selection should be replaced.</param>
    public void MultiSelect(IEnumerable<int> cueInds, bool replace = true)
    {
        if (replace)
            multiSelection.Clear();
        SelectionMode = SelectionMode.Add;
        foreach (var ind in cueInds.FastReverse())
            SelectedCueInd = ind;
        SelectionMode = SelectionMode.Normal;
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
    /// Selects a cue by index, applying the given multiselection rules.
    /// </summary>
    /// <param name="cueInd">The index of the cue to select.</param>
    /// <param name="mode">The multiselection mode to use.</param>
    public void MultiSelect(int cueInd, SelectionMode mode)
    {
        SelectionMode = mode;
        SelectedCueInd = cueInd;
        SelectionMode = SelectionMode.Normal;
    }

    private void HandleSelection(int prevSelected, ref int selected)
    {
        int nCues = cues.Count;
        selected = Math.Clamp(selected, 0, nCues);
        var prev = prevSelected < nCues ? cues[prevSelected] : null;

        if (selected == nCues)
        {
            multiSelection.Clear();
            return;
        }

        var cue = cues[selected];
        cue.OnFocussed();

        switch (selectionMode)
        {
            case SelectionMode.PassThrough:
                multiSelection.Replace(cue);
                break;
            case SelectionMode.Normal:
                // Replace the current selection, unless the multiselection contains the new item
                if (prev != cue && multiSelection.Count > 1 && multiSelection.Contains(cue))
                    break;
                multiSelection.Replace(cue);
                break;
            case SelectionMode.Add:
                multiSelection.Add(cue);
                break;
            case SelectionMode.Subtract:
                multiSelection.Remove(cue);
                selected = prevSelected;
                break;
            case SelectionMode.Toggle:
                if (multiSelection.Remove(cue))
                    selected = prevSelected;
                else
                    multiSelection.Add(cue);
                break;
            case SelectionMode.Range:
                int start, end;
                if (prevSelected < selected)
                {
                    start = prevSelected + 1;
                    end = selected + 1;
                }
                else
                {
                    start = selected;
                    end = prevSelected;
                }

                for (int i = start; i < end; i++)
                {
                    var q = cues[i];
                    multiSelection.Add(q);
                    NotifyCueSelectionChanged(q);
                }
                break;
        }
    }

    private void NotifyCueSelectionChanged(CueViewModel? cue)
    {
        if (cue == null)
            return;
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

        var mainSel = SelectedCue!;
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

    private void SyncCueDict(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                {
                    CueViewModel item = (CueViewModel)e.NewItems![0]!;
                    cuesDict.TryAdd(item.QID, item);
                    break;
                }
            case NotifyCollectionChangedAction.Remove:
                {
                    CueViewModel item = (CueViewModel)e.OldItems![0]!;
                    cuesDict.Remove(item.QID);
                    break;
                }
            case NotifyCollectionChangedAction.Replace:
                {
                    CueViewModel oldItm = (CueViewModel)e.OldItems![0]!;
                    CueViewModel newItm = (CueViewModel)e.NewItems![0]!;
                    cuesDict.Remove(oldItm.QID);
                    cuesDict.TryAdd(newItm.QID, newItm);
                    break;
                }
            case NotifyCollectionChangedAction.Move:
                break;
            case NotifyCollectionChangedAction.Reset:
                {
                    cuesDict.Clear();
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                        {
                            CueViewModel x = (CueViewModel)item!;
                            cuesDict.TryAdd(x.QID, x);
                        }
                    }
                    break;
                }
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
    /// Selecting an item clears any multi-selected items.
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
