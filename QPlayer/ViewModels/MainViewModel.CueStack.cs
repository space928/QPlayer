using QPlayer.Models;
using QPlayer.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Windows;

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
            UndoManager.RegisterAction($"Deleted cue '{cue.Name}' ({cue.QID})", () => InsertCue(index, cue, true), () => DeleteCue(index));
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
            UndoManager.RegisterAction($"Inserted cue '{cue.Name}' ({cue.QID})", () => DeleteCue(index), () => InsertCue(index, cue));
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
}
