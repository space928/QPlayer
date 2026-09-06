using QPlayer.Models;
using QPlayer.Utilities;
using System;
using System.Collections.Generic;
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
    /// Deletes the cue at the given position from the cue stack.
    /// </summary>
    /// <param name="pos">The position of the cue in the cue stack.</param>
    /// <param name="refreshSelection">Whether the current selection should be updated after this cue is deleted.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns>The cue which was just removed, or null if it wasn't found.</returns>
    public CueViewModel? DeleteCue(CuePosition pos, bool refreshSelection = true, bool recordUndo = true)
    {
        if (CueList.Delete(pos) is not CueViewModel cue)
            return null;
        if (refreshSelection)
            RefreshSelection();

        cue.FreeResources();

        if (recordUndo)
            UndoManager.RegisterAction($"Deleted cue '{cue.Name}' ({cue.FullQID})",
                () => InsertCue(pos, cue, refreshSelection, false),
                () => DeleteCue(pos, refreshSelection, false));
        return cue;
    }

    /// <summary>
    /// Deletes the given cue from the cue stack.
    /// </summary>
    /// <param name="cue">The cue instance to remove from the cue stack.</param>
    /// <param name="refreshSelection">Whether the current selection should be updated after this cue is deleted.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns><see langword="true"/> if the cue was successfully removed.</returns>
    public bool DeleteCue(CueViewModel cue, bool refreshSelection = true, bool recordUndo = true)
    {
        if (!CueList.Find(cue, out var pos))
            return false;

        return DeleteCue(pos, refreshSelection, recordUndo) != null;
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
    /// <param name="refreshSelection">Whether the current selection should be updated after this cue is deleted.</param>
    /// <returns>The cues which were just removed, or null if they weren't found.</returns>
    public CueViewModel[] DeleteCues(CuePosition startPos, int count = 1, bool recordUndo = true, bool refreshSelection = true)
    {
        if (count == 0 || !Cues.BoundsCheck(startPos))
            return [];

        if (count == 1)
        {
            // Simple case where only one cue is being deleted
            var single = DeleteCue(startPos, recordUndo: recordUndo);
            return single == null ? [] : [single];
        }

        var lastPos = new CuePosition(startPos.index + count - 1, startPos.group);
        if (!Cues.BoundsCheck(lastPos))
            return [];

        int selInd = -1;
        if (refreshSelection)
            selInd = Cues.FindVisualIndex(startPos);
        var deleted = CueList.Delete(new CuePositionRangeEnumerable(startPos, count), needsSorting: false, collapseChildren: false);
        // deleted = DeduplicateCues(deleted).ToArray();
        if (refreshSelection)
            SelectedCueInd = selInd;

        if (deleted.Length != count)
        {
            Log($"Couldn't delete all the requested cues!", LogLevel.Warning);
            return deleted;
        }

        foreach (var cue in deleted)
            cue.FreeResources();

        if (recordUndo)
            UndoManager.RegisterAction($"Deleted {deleted.Length} cues",
                () => InsertCues(new CuePositionRangeEnumerable(startPos, deleted.Length).ToArray(), deleted, refreshSelection, false),
                () => DeleteCues(startPos, deleted.Length, false, refreshSelection));
        return deleted;
    }

    /// <summary>
    /// Deletes the given cues by index from the cue stack.
    /// </summary>
    /// <param name="positions">The cue positions to delete.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <param name="refreshSelection">Whether the current selection should be updated after this cue is deleted.</param>
    /// <returns>An array of the deleted cues.</returns>
    public CueViewModel[] DeleteCues(CuePosition[] positions, bool recordUndo = true, bool refreshSelection = true)
    {
        if (positions.Length == 0)
            return [];
        if (positions.Length == 1)
        {
            var single = DeleteCue(positions[0], recordUndo: recordUndo);
            return single == null ? [] : [single];
        }

        CueList.SortPositions(positions.AsSpan());
        var deleted = CueList.Delete(positions, needsSorting: false);
        if (refreshSelection)
            MultiSelect(positions[0]);

        if (deleted.Length != positions.Length)
        {
            Log($"Couldn't delete all the requested cues!", LogLevel.Warning);
            return deleted;
        }

        foreach (var cue in deleted)
            cue.FreeResources();

        if (recordUndo)
            UndoManager.RegisterAction($"Deleted {deleted.Length} cues",
                () => InsertCues(positions, deleted, refreshSelection, false),
                () => DeleteCues(positions, false, refreshSelection));
        return deleted;
    }

    /// <summary>
    /// Deletes the given cues from the cue stack.
    /// </summary>
    /// <param name="cues">The cues to delete.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns>An array of the deleted cues.</returns>
    public (CuePosition[] positions, CueViewModel[] cues) DeleteCues(IEnumerable<CueViewModel> cues, bool recordUndo = true)
    {
        using var positions = CueList.GetPositionsSorted(cues, true);
        var posArray = positions.ToArray();
        return (posArray, DeleteCues(posArray, recordUndo));
    }

    /// <summary>
    /// Inserts an existing cue into the cue stack. This method should be used with care as 
    /// it does not check if the cue already exists in the stack. To duplicate a cue, use 
    /// <see cref="DuplicateCue(CueViewModel?, bool)"/>.
    /// </summary>
    /// <param name="pos">The position at which to insert the cue.</param>
    /// <param name="cue">The cue to insert.</param>
    /// <param name="select">Whether the newly inserted cue should be selected.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void InsertCue(CuePosition pos, CueViewModel cue, bool select = false, bool recordUndo = true)
    {
        cue.InitResources();
        CueList.Insert(pos, cue);
        if (recordUndo)
            UndoManager.RegisterAction($"Inserted cue '{cue.Name}' ({cue.FullQID})",
                () => DeleteCue(pos, select, false),
                () => InsertCue(pos, cue, select, false));
        if (select)
            SelectedCuePos = pos;
    }

    /// <summary>
    /// Inserts existing cues into the cue stack. This method should be used with care as 
    /// it does not check if the cues already exists in the stack. To duplicate a cue, use 
    /// <see cref="DuplicateCue(CueViewModel?, bool)"/>.
    /// </summary>
    /// <param name="positions">The positions at which to insert the cues.</param>
    /// <param name="cues">The cues to insert.</param>
    /// <param name="select">Whether the newly inserted cues should be selected.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void InsertCues(CuePosition[] positions, CueViewModel[] cues, bool select = false, bool recordUndo = true)
    {
        if (positions.Length != cues.Length)
        {
            Log($"Can't insert cues, number of cue positions did not match the number of cues. This is a bug.", LogLevel.Warning);
            return;
        }
        if (positions.Length == 0)
            return;

        foreach (var cue in cues)
            cue.InitResources();

        CueList.Insert(positions, cues);

        if (recordUndo)
            UndoManager.RegisterAction($"Inserted {cues.Length} cues",
                () => DeleteCues(positions, false, select),
                () => InsertCues(positions, cues, select, false));
        if (select)
            MultiSelect(cues, true);
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
    public void DuplicateCues(IEnumerable<CueViewModel> cues, bool select = true, bool recordUndo = true)
    {
        if (cues is not ISet<CueViewModel> set)
            set = cues.ToHashSet();

        if (set.Count == 0)
            return;

        // Find the last cue in the visual list
        var allCues = this.cues;
        int lastInd;
        for (lastInd = allCues.Count - 1; lastInd >= 0; lastInd--)
        {
            if (set.Contains(allCues[lastInd]))
                break;
        }

        // Find the group that all cues share
        var last = allCues[lastInd];
        var sharedGroup = cues.FirstOrDefault()?.Parent;
        foreach (var cue in cues)
        {
            while (sharedGroup != null && !cue.HasParent(sharedGroup))
            {
                sharedGroup = sharedGroup.Parent;
                if (sharedGroup == null)
                    goto GroupFound;
            }
        }
    GroupFound:
        // Construct a cue position after the last cue but within the group all cues share
        if (!CueList.Find(last, out var dstPos))
            return;
        while (dstPos.group != sharedGroup && dstPos.group != null)
        {
            if (!CueList.Find(dstPos.group, out dstPos))
                return;
        }
        dstPos += 1;

        DuplicateCues(cues, dstPos, select, recordUndo);
    }

    /// <summary>
    /// Copies the given collection of cues to the new index.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="position"></param>
    /// <param name="select"></param>
    /// <param name="recordUndo"></param>
    public void DuplicateCues(IEnumerable<CueViewModel> cues, CuePosition position, bool select = true, bool recordUndo = true)
    {
        UndoManager.SuppressRecording();
        position = Cues.ClampCuePos(position, true);
        var srcArray = cues.ToArray();
        var range = new CuePositionRangeEnumerable(position, srcArray.Length);
        foreach (var (cue, newPos) in srcArray.FastZip(range))
            CreateCue(cue.TypeName, newPos, false, cue);

        if (select)
            MultiSelect(range.Select(x => Cues[x]), true);
        //MultiSelect(Cues.FindVisualIndex(position), srcArray.Length, true);
        UndoManager.UnSuppressRecording();

        if (recordUndo)
        {
            UndoManager.RegisterAction($"Duplicated {srcArray.Length} cues",
                () => DeleteCues(position, srcArray.Length, false, select),
                () => DuplicateCues(srcArray, position, select, false));
        }
    }

    /// <summary>
    /// Moves the given cue up or down by one, swapping position with the cue above or below it. 
    /// Renumbers the given cue to remain in order.
    /// </summary>
    /// <param name="cue">The cue instance to move.</param>
    /// <param name="down">Whether the cue should be moved up or down.</param>
    /// <param name="intoGroup">When <see langword="false"/> skips over sub-groups when moving the cue, when 
    /// <see langword="true"/> the cue can be moved into adjacant groups.</param>
    /// <param name="select">Whether the cue should be reselected after it's moved.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns><see langword="true"/> if successful.</returns>
    public bool MoveCue(CueViewModel cue, bool down, bool intoGroup, bool select = true, bool recordUndo = true)
    {
        if (!FindCuePosition(cue, out var srcPos))
            return false;

        var dstPos = srcPos + (down ? 2 : -1);
        var targetPos = srcPos + (down ? 1 : -1);

        if (intoGroup && cues.BoundsCheck(targetPos))
        {
            // Try to move the cue into the group if possible
            if (cues[targetPos] is GroupCueViewModel group)
                dstPos = group.Cues.CreateCuePosition(down ? 0 : group.Cues.Count);
        }
        else
        {
            // Trying to move a cue outside of a group
            if (!cues.BoundsCheck(targetPos, false) && targetPos.group != null)
            {
                if (!FindCuePosition(targetPos.group, out var groupPos))
                    return false;

                if (dstPos.index >= 0)
                    dstPos = groupPos + 1;
                else
                    dstPos = groupPos;
            }
        }

        return MoveCue(srcPos, dstPos, select, null, recordUndo);
    }

    /// <summary>
    /// Moves the selected cues up or down by one, swapping position with the cues above or below them. 
    /// Renumbers the given cue to remain in order.
    /// </summary>
    /// <param name="down">Whether the cues should be moved up or down.</param>
    /// <param name="intoGroup">When <see langword="false"/> skips over sub-groups when moving the cues, when 
    /// <see langword="true"/> the cues can be moved into adjacant groups.</param>
    /// <param name="select">Whether the cues should be reselected after they're moved.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void MoveSelectedCues(bool down, bool intoGroup, bool select = true, bool recordUndo = true)
    {
        // Find the index at which to move the cues.
        int endInd;
        var selected = multiSelection;
        var allCues = cues;
        CuePosition dstPos;
        CuePosition targetPos;

        if (!down)
        {
            for (endInd = 0; endInd < allCues.Count; endInd++)
            {
                if (selected.Contains(allCues[endInd]))
                    break;
            }
            FindCuePosition(allCues[endInd], out dstPos);
            dstPos -= 1;
            targetPos = dstPos;
        }
        else
        {
            for (endInd = allCues.Count - 1; endInd >= 0; endInd--)
            {
                if (selected.Contains(allCues[endInd]))
                    break;
            }
            FindCuePosition(allCues[endInd], out dstPos);
            dstPos += 2;
            targetPos = dstPos - 1;
        }

        if (intoGroup)
        {
            // Try to move the cue into the group if possible
            if (cues.BoundsCheck(targetPos) && cues[targetPos] is GroupCueViewModel group)
                dstPos = group.Cues.CreateCuePosition(down ? 0 : group.Cues.Count);
        }
        else
        {
            // Trying to move a cue outside of a group
            if (!cues.BoundsCheck(targetPos, false) && targetPos.group != null)
            {
                if (!FindCuePosition(targetPos.group, out var groupPos))
                    return;

                if (dstPos.index >= 0)
                    dstPos = groupPos + 1;
                else
                    dstPos = groupPos;
            }
        }

        MoveCues(multiSelection, dstPos, select, recordUndo);
    }

    /// <summary>
    /// Moves the given collection of cues to the new index.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="pos"></param>
    /// <param name="select"></param>
    /// <param name="recordUndo"></param>
    public void MoveCues(IEnumerable<CueViewModel> cues, CuePosition pos, bool select = true, bool recordUndo = true)
    {
        if (cues.TryGetNonEnumeratedCount(out var count) && count <= 1)
        {
            // Shortcut for a single cue
            using var cuesEnum = cues.GetEnumerator();
            if (count == 1 && cuesEnum.MoveNext())
                MoveCue(cuesEnum.Current, pos, select, null, recordUndo);
            return;
        }

        // Get the cue positions and sort them
        pos = Cues.ClampCuePos(pos, true);
        using var srcPositionsTemp = CueList.GetPositionsSorted(cues, true);
        var srcPositions = srcPositionsTemp.ToArray();
        var srcArray = Cues.GetCues(srcPositions).ToArray();
        var srcQIDs = srcArray.Select(x => x.QID).ToArray();

        UndoManager.SuppressRecording();
        MoveCuesInternal(srcArray, srcPositions, pos, select, out var dstPos);
        UndoManager.UnSuppressRecording();

        if (recordUndo)
        {
            UndoManager.RegisterAction($"Moved {srcArray.Length} cues",
                () => MoveCuesBack(srcArray, srcPositions, srcQIDs, select, dstPos),
                () => MoveCuesInternal(srcArray, srcPositions, pos, select, out _));
        }
    }

    /// <summary>
    /// Moves an ordered array of cues to the new index <paramref name="dstPos"/> in the cue stack.
    /// This method assumes that <paramref name="cues"/> and <paramref name="positions"/> are already 
    /// sorted.
    /// </summary>
    /// <param name="cues">The sorted array of cues.</param>
    /// <param name="positions">The sorted array of cue indices. Enumerables which implement <see cref="IReadOnlyList{T}"/> are preferred.</param>
    /// <param name="dstPos">The starting index to move the cues to.</param>
    /// <param name="removedBefore">The number of cues which were at an index smaller than <paramref name="dstPos"/>.</param>
    private void MoveCuesInternal(CueViewModel[] cues, CuePosition[] positions, CuePosition dstPos, bool select, out CuePosition modifiedPos)
    {
        modifiedPos = default;
        if (cues.Length != positions.Length)
        {
            Log($"Couldn't move cues, input arrays length mismatch.", LogLevel.Warning);
            return;
        }

        int removedBefore = 0;
        for (int j = positions.Length - 1; j >= 0; j--)
        {
            var pos = positions[j];
            if (pos.group == dstPos.group)
            {
                if (pos.index < dstPos.index)
                    removedBefore++;
                else
                    break;
            }
        }

        var res = CueList.Delete(positions, false, false);
        if (res.Length != cues.Length)
            return;

        dstPos -= removedBefore;
        modifiedPos = dstPos;

        CueList.Insert(dstPos, res);
        RenumberCues(dstPos, res.Length);

        if (select)
        {
            var dstInd = this.cues.FindVisualIndex(dstPos);
            MultiSelect(Enumerable.Range(dstInd, cues.Length));
        }
    }

    private void MoveCuesBack(CueViewModel[] cues, CuePosition[] positions, decimal[] qids, bool select, CuePosition dstPos)
    {
        if (qids.Length != cues.Length || qids.Length != positions.Length)
        {
            Log($"Couldn't move cues, input arrays length mismatch.", LogLevel.Warning);
            return;
        }

        using var _ = UndoManager.ScopedSuppress();

        var res = CueList.Delete(new CuePositionRangeEnumerable(dstPos, cues.Length), false, false);
        if (res.Length != cues.Length)
        {
            Log($"Couldn't move cues back, not all cues were deleted.", LogLevel.Warning);
            return;
        }

        CueList.Insert(positions, res);

        for (int i = 0; i < cues.Length; i++)
            cues[i].QID = qids[i];

        if (select)
            MultiSelect(cues);
    }

    /// <summary>
    /// Moves the given cue in the cue stack to the specified index.
    /// </summary>
    /// <param name="cue">The cue to move in the cue stack.</param>
    /// <param name="dstPos">The position within the cue stack to move the cue to.</param>
    /// <param name="select">Whether the moved cue should be reselected.</param>
    /// <param name="newQID">Optionally, the new QID to assign to the cue once it's moved. 
    /// Otherwise, this method will choose a new QID itself (recommended).</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns><see langword="true"/> if the cue was moved successfully.</returns>
    public bool MoveCue(CueViewModel cue, CuePosition dstPos, bool select = true, decimal? newQID = null, bool recordUndo = true)
    {
        // Find the src and dst indices
        dstPos = cues.ClampCuePos(dstPos, true);
        if (!FindCuePosition(cue, out var srcPos))
            return false;

        return MoveCue(srcPos, dstPos, select, newQID, recordUndo);
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
    /// <param name="srcPos">The cue to move in the cue stack.</param>
    /// <param name="dstPos">The position within the cue stack to move the cue to.</param>
    /// <param name="select">Whether the moved cue should be reselected.</param>
    /// <param name="newQID">Optionally, the new QID to assign to the cue once it's moved. 
    /// Otherwise, this method will choose a new QID itself (recommended).</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns><see langword="true"/> if the cue was moved successfully.</returns>
    public bool MoveCue(CuePosition srcPos, CuePosition dstPos, bool select = true, decimal? newQID = null, bool recordUndo = true)
    {
        var origDst = dstPos;
        var origSrc = srcPos;
        if (srcPos.group == dstPos.group)
        {
            if (dstPos.index > srcPos.index)
                dstPos -= 1; // The cue is being moved down the stack, account for the shift that will be created as a result of deleted the src cue
            else
                origSrc += 1; // The cue is being moved up the stack, account for the shift to the original position as a result of this move
        }

        // Find the src and dst indices
        dstPos = cues.ClampCuePos(dstPos, dstPos.group != null || srcPos.group != dstPos.group);
        if (!cues.BoundsCheck(srcPos, false))
            return false;

        // The cue doesn't need to be moved, don't do anything.
        if (srcPos == dstPos)
            return false;

        var cue = cues[srcPos];
        var oldQID = cue.QID;
        var oldFullQID = cue.FullQID;
        if (!DeleteCue(cue, false, false))
        {
            Log($"Couldn't move cue from index {srcPos} to {dstPos}! This is probably a bug in QPlayer. Please re-load your showfile to avoid corruption and file a bug report.", LogLevel.Warning);
            return false;
        }

        using (UndoManager.ScopedSuppress())
            cue.QID = newQID ?? ChooseQID(dstPos - 1, false);

        InsertCue(dstPos, cue, select, false);

        if (recordUndo)
            UndoManager.RegisterAction($"Moved cue Q{oldFullQID} --> Q{cue.FullQID}",
                () => MoveCue(dstPos, origSrc, select, oldQID, false),
                () => MoveCue(srcPos, origDst, select, null, false));

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

        var pos = SelectedCuePos + 1;
        if (beforeCurrent)
            pos--;
        if (afterLast)
            pos = CueList.CreateCuePosition(cues.Count);

        return CreateCue(type, pos, select, src);
    }

    /// <summary>
    /// Creates a new cue of the given type and inserts it into the cue stack at the given position.
    /// </summary>
    /// <param name="type">The type of cue to create.</param>
    /// <param name="pos">The index in the cue stac to insert the cue</param>
    /// <param name="select">Whether the newly created cue should be selected.</param>
    /// <param name="src">Optionally, a cue to copy properties from.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <returns>The view model instance of the newly created cue.</returns>
    public CueViewModel? CreateCue(string? type, CuePosition pos, bool select = true, CueViewModel? src = null, bool recordUndo = true)
    {
        pos = cues.ClampCuePos(pos, true);

        // No need to suppress undo here, these methods already suppress it.
        var qid = ChooseQID(pos - 1);
        var cue = CreateCueNoInsert(type, qid, src);
        if (cue == null)
            return null;

        InsertCue(pos, cue, select, false);

        if (recordUndo)
        {
            string action = src == null ? $"Created {cue.TypeDisplayName}" : $"Duplicated '{cue.Name}'";
            UndoManager.RegisterAction($"{action} ({cue.FullQID})",
                () => DeleteCue(pos, select, false),
                () => InsertCue(pos, cue, select, false));
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

    /*private IEnumerable<CueViewModel> EnumerateParents(CueViewModel cue)
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
    }*/

    /// <summary>
    /// Groups a number of cues into a target cue. If the target cue is a group cue, the new cues are appended 
    /// to the end of the group. If the target cue is not a group cue, then a new group cue is created 
    /// containing the target cue followed by the other cues.
    /// </summary>
    /// <param name="cues">An enumerable of cues already in the cue stack to group.</param>
    /// <param name="target">The cue to group into.</param>
    /// <param name="refreshSelection">Whether the current selection should be updated after the group cue is created.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void GroupCues(IEnumerable<CueViewModel> cues, CueViewModel target, bool refreshSelection = true, bool recordUndo = true)
    {
        if (target is not GroupCueViewModel group)
        {
            var toGroup = GetCuesToGroup().ToArray();
            if (!FindCuePosition(target, out var dstPos))
                return;
            GroupCues(toGroup, dstPos, recordUndo: recordUndo, needsSorting: true);
            return;
        }

        using var positions = CueList.GetPositionsSorted(cues, true);
        var positionsArr = positions.ToArray();
        var deleted = CueList.Delete(positionsArr, needsSorting: false, collapseChildren: false);
        if (deleted.Length == 0)
            return;
        CueList.Insert(group.Cues.CreateCuePosition(group.Cues.Count), deleted);
        if (refreshSelection)
            RefreshSelection();

        if (recordUndo)
        {
            UndoManager.RegisterAction($"Added {deleted.Length} cues to group",
                () =>
                {
                    var groupCuePositions = new CuePositionRangeEnumerable(
                        group.Cues.CreateCuePosition(group.Cues.Count - deleted.Length),
                        deleted.Length);
                    CueList.Delete(groupCuePositions, false, false);
                    CueList.Insert(positionsArr, deleted);
                },
                () => GroupCues(deleted, refreshSelection, false));
        }

        IEnumerable<CueViewModel> GetCuesToGroup()
        {
            yield return target;
            foreach (var cue in cues)
                if (cue != target)
                    yield return cue;
        }
    }

    /// <summary>
    /// Creates a new group cue containing the specified cues.
    /// </summary>
    /// <param name="cues">The cues to put in the group, the order within the cue stack of these cues is maintained.</param>
    /// <param name="refreshSelection">Whether the current selection should be updated after the group cue is created.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void GroupCues(IEnumerable<CueViewModel> cues, bool refreshSelection = true, bool recordUndo = true)
    {
        using var positionsTemp = CueList.GetPositionsSorted(cues, true);
        var positions = positionsTemp.ToArray();
        var srcCues = Cues.GetCues(positions).ToArray();

        if (srcCues.Length == 0)
            return;

        GroupCues(srcCues, positions[0], refreshSelection, recordUndo, false, positions);
    }

    /// <summary>
    /// Creates a new group cue containing the specified cues.
    /// </summary>
    /// <remarks>
    /// If <paramref name="needsSorting"/> is false, then the caller must ensure that all the cues in 
    /// <paramref name="cues"/> are in the visual cue list.
    /// </remarks>
    /// <param name="cues">The ordered indices of the cues to add to the group.</param>
    /// <param name="dstPos">The position at which to create the new group cue.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <param name="needsSorting">Whether the cues array needs sorting by visual index.</param>
    /// <param name="refreshSelection">Whether the current selection should be updated after the group cue is created.</param>
    public void GroupCues(CueViewModel[] cues, CuePosition dstPos, bool refreshSelection = true,
        bool recordUndo = true, bool needsSorting = true, CuePosition[]? cuePositions = null)
    {
        if (cues.Length == 0)
            return;

        if (needsSorting) // Expensive
        {
            // Sort the input positions
            using var positionsTemp = CueList.GetPositionsSorted(cues, true);
            cuePositions = positionsTemp.ToArray();
            // Sort the input cues using the sorted positions
            int i = 0;
            foreach (var cue in Cues.GetCues(cuePositions))
                cues[i++] = cue;
            if (cues.Length != cuePositions.Length)
                cues = cues[..cuePositions.Length];
        }
        cuePositions ??= [.. Cues.GetPositions(cues)];

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
        if (dstPos.group != parent)
        {
            while (dstPos.group != parent && dstPos.group != null)
            {
                if (!CueList.Find(dstPos.group, out dstPos))
                    return;
            }
            dstPos = Cues.ClampCuePos(dstPos - 1, true);
        }

        var groupQid = ChooseQID(dstPos, true);
        var group = (GroupCueViewModel)CreateCueNoInsert(nameof(GroupCue), groupQid)!;
        var groupList = group.Cues;

        CueList.Delete(cuePositions, false);
        groupList.Insert(groupList.CreateCuePosition(0), cues);
        InsertCue(dstPos, group, select: true, recordUndo: false);
        var qids = cues.Select(x => x.QID).ToArray();
        RenumberCues(group, false);

        UndoManager.UnSuppressRecording();

        if (refreshSelection)
            RefreshSelection();

        if (recordUndo)
        {
            UndoManager.RegisterAction($"Grouped {cues.Length} cues",
                () =>
                {
                    UndoManager.SuppressRecording();
                    CueList.Delete(dstPos);
                    CueList.Insert(cuePositions, cues);
                    int i = 0;
                    foreach (var cue in cues)
                        cue.QID = qids[i++];
                    UndoManager.UnSuppressRecording();
                    if (refreshSelection)
                        RefreshSelection();
                },
                () => GroupCues(cues, dstPos, refreshSelection, false, false, cuePositions));
        }
    }

    /// <summary>
    /// Ungroups the given cues by removing each cue in the given list and reinserting 
    /// it just before it's group cue. Empty group cues encountered in doing so are
    /// deleted.
    /// </summary>
    /// <param name="cues"></param>
    /// <param name="select">Whether the ungrouped cues should be selected.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    public void UngroupCues(IEnumerable<CueViewModel> cues, bool select = true, bool recordUndo = true)
    {
        UndoManager.BeginGroupRecording();
        int i = 0;
        foreach (var cue in cues)
        {
            UngroupCue(cue);
            i++;
        }
        UndoManager.EndGroupRecording($"Ungrouped {i} cues");
        if (select)
            MultiSelect(cues);

        bool UngroupCue(CueViewModel cue)
        {
            if (!FindCuePosition(cue, out var pos) || pos.group == null)
                return false;

            if (!FindCuePosition(pos.group, out var groupPos))
                return false;

            UndoManager.SuppressRecording();
            var oldQID = cue.QID;
            CueList.Delete(pos);

            bool deletedGroup = pos.group.Cues.IsEmpty;
            if (deletedGroup)
                CueList.Delete(groupPos);

            CueList.Insert(groupPos, cue);
            cue.QID = ChooseQID(groupPos, true);
            UndoManager.UnSuppressRecording();

            if (recordUndo)
            {
                UndoManager.RegisterAction($"Ungrouped {cue}",
                    () =>
                    {
                        CueList.Delete(groupPos);
                        if (deletedGroup)
                            CueList.Insert(groupPos, pos.group);
                        CueList.Insert(pos, cue);
                        cue.QID = oldQID;
                        if (select)
                            RefreshSelection();
                    },
                    () => UngroupCue(cue));
            }
            return true;
        }
        // TODO: Finish implementing the more optimal version
        /*using var positions = CueList.GetPositionsSorted(cues.Where(CueHasParent));
        var deletedPositions = positions.ToArray();
        var deleted = CueList.Delete(deletedPositions, false);

        // Get the list of parent group positions and delete any empty groups
        using var parentGroups = new TemporaryList<(CuePosition groupPos, int toInsert, GroupCueViewModel cue)>();
        GroupCueViewModel? lastGroup = null;
        int i = 0;
        foreach (var (cue, pos) in deleted.FastZip(deletedPositions))
        {
            i++;
            if (pos.group == lastGroup || pos.group == null)
                continue;

            lastGroup = pos.group;
            if (!CueList.Find(pos.group, out var groupPos))
                continue;

            parentGroups.Add((groupPos, i - 1, lastGroup));
            if (lastGroup.Cues.IsEmpty)
                CueList.Delete(groupPos);
        }

        var revDeleted = deleted.FastReverse();
        int lastStart = deleted.Length;
        foreach (var (groupPos, toInsert, group) in parentGroups.FastReverse())
        {
            // Reinsert the ungrouped cue just before it's old group.
            CueList.Insert(groupPos, deleted.AsSegment(toInsert, lastStart - toInsert));
            lastStart = toInsert;
        }

        if (recordUndo)
        {
            //var actionsArr = actions.ToArray();
            UndoManager.RegisterAction($"Ungrouped {deleted.Length} cues",
                () =>
                {
                    CueList.Delete(deleted);
                },
                () => UngroupCues(cues, false));

        }

        static bool CueHasParent(CueViewModel x) => x.HasParent();*/
    }

    /// <summary>
    /// Deletes this group cue and reinserts it's contents where the group used to be.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="refreshSelection">Whether the current selected cue should be refreshed to account for changes to the cue stack.</param>
    /// <param name="recordUndo">Whether an undo item should be recorded.</param>
    /// <param name="positions">Optionally, an array of positions to move the ungrouped cues to.</param>
    public void UngroupCues(GroupCueViewModel group, bool refreshSelection = true, bool recordUndo = true, CuePosition[]? positions = null)
    {
        if (!CueList.Find(group, out var pos))
            return;

        if (positions != null && group.Cues.Count != positions.Length)
        {
            Log($"Expected the same number of positions as cues to ungroup!", LogLevel.Warning);
            return;
        }

        UndoManager.SuppressRecording();

        CueList.Delete(pos);
        if (positions == null)
            CueList.Insert(pos, group.Cues);
        else
            CueList.Insert(positions, group.Cues);

        decimal[] qids = [];
        if (recordUndo)
            qids = group.Cues.Select(x => x.QID).ToArray();
        RenumberCues(pos, group.Cues.Count);

        UndoManager.UnSuppressRecording();

        if (refreshSelection)
        {
            if (group.Cues.Count > 0)
                SelectedCue = group.Cues[0];
            RefreshSelection();
        }

        if (recordUndo)
        {
            var ungroupedCues = group.Cues.ToArray();
            group.Cues.Clear();
            UndoManager.RegisterAction($"Ungrouped {ungroupedCues.Length} cues",
                () =>
                {
                    // Delete the ungrouped cues
                    if (positions == null)
                        CueList.Delete(new CuePositionRangeEnumerable(pos, ungroupedCues.Length), false);
                    else
                        CueList.Delete(positions);

                    // Put them back into the group cue
                    var groupCues = group.Cues;
                    groupCues.Clear();
                    groupCues.Insert(groupCues.CreateCuePosition(0), ungroupedCues);
                    foreach (var (cue, qid) in groupCues.FastZip(qids))
                        cue.QID = qid;

                    // Reinsert the group cue
                    CueList.Insert(pos, group);
                    // Lazy alternative, it wouldn't preserve the group cue's name, etc...
                    //GroupCues(ungroupedCues, pos, false, false);
                    if (refreshSelection)
                        SelectedCue = group;
                },
                () => UngroupCues((GroupCueViewModel)Cues[pos], refreshSelection, false, positions));
        }
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

    private decimal ChooseQID(CuePosition insertAfter, bool replace = false)
    {
        var list = insertAfter.group?.Cues ?? CueList;
        var rootList = list.Cues;

        var prevInd = replace ? insertAfter.index - 1 : insertAfter.index;
        var prev = prevInd >= 0 && prevInd < rootList.Count ? rootList[prevInd] : null;
        var nextInd = insertAfter.index + 1;
        var next = nextInd >= 0 && nextInd < rootList.Count ? rootList[nextInd] : null;

        var prevId = prev != null ? prev.QID : 0;//(insertAfter.group != null ? insertAfter.group.QID : 0);
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
            return ChooseQID(default(CuePosition), ignoreCurrent);
        if (insertAfterInd >= Cues.Count)
            return ChooseQID(CueList.CreateCuePosition(CueList.Count - 1), ignoreCurrent);
        CueList.Find(Cues[insertAfterInd], out var pos);
        return ChooseQID(pos, ignoreCurrent);
    }

    /// <summary>
    /// Renumbers a range of <b>visible</b> cues.
    /// </summary>
    /// <param name="start">The position of the first cue to renumber.</param>
    /// <param name="count">The total number of cues to renumber (including subcues).</param>
    /// <param name="startID">The QID to assign the first cue, specify <c>-1</c> to choose automatically.</param>
    /// <param name="increment">The increment between each QID, specify <c>-1</c> to choose automatically.</param>
    /// <returns>The number of cues which were renumbered.</returns>
    internal int RenumberCues(CuePosition start, int count, decimal startID = -1, decimal increment = -1)
    {
        if (count == 0)
            return 0;

        using var _ = UndoManager.ScopedGroup($"Renumbered {count} cues");

        var list = start.group?.Cues ?? CueList;
        CueViewModel? prev = null;

        // Find the cue before the range to be renumbered if it exists
        if (startID == -1 && start.index > 0 && list.Count > 1)
            prev = list[start.index - 1];

        //  Calculate an increment that alloows all cues to fit between the two bounding cues, if they exist
        if (increment == -1)
        {
            int nextInd = start.index + count;
            if (nextInd < list.Count)
            {
                var next = list[nextInd];
                var qidDelta = next.QID - (prev?.QID ?? 0);
                // 10^floor(log(qidDelta/count))
                increment = ((decimal)Math.Pow(10, Math.Floor(Math.Log10((double)qidDelta / count)))).Normalize();
            }
            else
            {
                // Default to increment by 1 if the range is not bounded by two cues.
                increment = 1;
            }
        }

        // Set the starting QID
        if (startID == -1)
        {
            if (prev != null)
                startID = prev.QID + increment;
            else
                startID = increment;
        }

        // Run through each cue in the sublist, recursively renumbering sublists until we reach the end of the range.
        var qid = startID;
        int j = start.index;
        int i = 0;
        for (; i < count; i++)
        {
            if (j >= list.Count)
                break;

            var cue = list[j];
            cue.QID = qid;
            if (cue is GroupCueViewModel group && !group.IsCollapsed)
                i += RenumberCues(group.Cues.CreateCuePosition(0), count - i);
            qid += increment;
            j++;
        }
        return i;
    }

    /// <summary>
    /// Renumbers the cues within a group such that they have sequential QIDs starting from 1.
    /// </summary>
    /// <param name="group"></param>
    /// <param name="recordUndo"></param>
    public void RenumberCues(GroupCueViewModel group, bool recordUndo = true)
    {
        using IDisposable _ = recordUndo ? UndoManager.ScopedSuppress() : UndoManager.ScopedGroup($"Renumbered {group.Cues.Count} cues");

        var qid = 1m;
        foreach (var cue in group.Cues.Cues)
        {
            cue.QID = qid++;
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
                return cues.Find(idString, out cue);
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

    /// <inheritdoc cref="FindCue(decimal, out CueViewModel?)"/>
    public bool FindCue(string id, [NotNullWhen(true)] out CueViewModel? cue) => cues.Find(id, out cue);

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
    /// Gets the position of a cue in the cue list.
    /// </summary>
    /// <param name="cue">The cue instance to search for.</param>
    /// <param name="pos">The found position of the cue.</param>
    /// <returns><see langword="true"/> if the cue was found succesfully.</returns>
    public bool FindCuePosition(CueViewModel? cue, out CuePosition pos)
    {
        if (cue == null)
        {
            pos = default;
            return false;
        }

        return CueList.Find(cue, out pos);
    }

    /// <summary>
    /// When the cue stack is updated, this method should be called if the cue at the currently selected index
    /// might have changed. Alternatively, a different cue can be selected instead using <see cref="SelectedCueInd"/>.
    /// </summary>
    internal void RefreshSelection(bool aggressive = false)
    {
        MultiSelect(selectedCueInd);
        if (aggressive)
        {
            foreach (var cue in cues)
                NotifyCueSelectionChanged(cue);
            OnPropertyChanged(nameof(SelectedCue));
            OnPropertyChanged(nameof(SelectedCuePos));
            OnPropertyChanged(nameof(SelectedCueInd));
            SelectedCue?.OnFocussed();
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
    /// Selects a cue by position, applying multiselection rules based on the current keyboard modifier keys.
    /// </summary>
    /// <param name="pos"></param>
    public void MultiSelect(CuePosition pos) => MultiSelect(cues.FindVisualIndex(pos));
    public void MultiSelect(CueViewModel? cue, SelectionMode mode) => MultiSelect(FindCueIndex(cue), mode);

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
        if (FindCuePosition(SelectedCue, out var pos))
            selectedCuePos = pos;
        else
            selectedCuePos = CuePosition.Invalid;
        // TODO: Currently we just notify all the cues for simplicity. The toNotify list is correct, but the UI requires
        // that directly adjacent cues are also notified to fix the selection outline.
        foreach (var cue in toNotify)
            NotifyCueSelectionChanged(cue);
        foreach (var cue in MultiSelection)
            NotifyCueSelectionChanged(cue);

        OnPropertyChanged(nameof(SelectedCue));
        OnPropertyChanged(nameof(SelectedCuePos));
        OnPropertyChanged(nameof(SelectedCueInd));
        SelectedCue?.OnFocussed();
    }

    private void HandleSelection(int prevSelected, ref int selected, SelectionMode mode, ref TemporaryList<CueViewModel> cuesToNotify)
    {
        int nCues = cues.Count;
        selected = Math.Clamp(selected, -1, nCues);

        if (selected < 0 || selected >= nCues)
        {
            cuesToNotify.AddRange(multiSelection);
            multiSelection.Clear();
            return;
        }

        var cue = cues[selected];
        var prev = prevSelected >= 0 && prevSelected < nCues ? cues[prevSelected] : null;

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
