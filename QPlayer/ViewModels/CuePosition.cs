using System;
using System.Collections;
using System.Collections.Generic;

namespace QPlayer.ViewModels;

/// <summary>
/// Represents the hierarchical position of a cue in the cue stack.
/// </summary>
/// <param name="index">The index of the cue in the stack (or sub-stack).</param>
/// <param name="group">The group cue this cue belongs to or <see langword="null"/> if this cue is part of the root cue list.</param>
public readonly struct CuePosition(int index, GroupCueViewModel? group) : IEquatable<CuePosition>
{
    public readonly int index = index;
    public readonly GroupCueViewModel? group = group;

    public static CuePosition Invalid => new(-1, null);

    public override bool Equals(object? obj) => obj is CuePosition position && Equals(position);
    public bool Equals(CuePosition other) => index == other.index && group == other.group;
    public override int GetHashCode() => HashCode.Combine(index, group);

    public static CuePosition operator +(CuePosition a, CuePosition b) => new(a.index + b.index, a.group);
    public static CuePosition operator +(CuePosition a, int b) => new(a.index + b, a.group);
    public static CuePosition operator -(CuePosition a, CuePosition b) => new(a.index - b.index, a.group);
    public static CuePosition operator -(CuePosition a, int b) => new(a.index - b, a.group);
    public static bool operator ==(CuePosition a, CuePosition b) => Equals(a, b);
    public static bool operator !=(CuePosition a, CuePosition b) => !Equals(a, b);
    public static CuePosition operator ++(CuePosition a) => a + 1;
    public static CuePosition operator --(CuePosition a) => a - 1;

    public override string ToString() => $"[{index}, {(group?.ToString() ?? "root")}]";
}

/*public readonly struct CuePositionComparer : IComparer<CuePosition>
{
    public readonly int Compare(CuePosition x, CuePosition y)
    {
        if (x.group == null && y.group != null)
            return 1;
        if (y.group == null && x.group != null)
            return -1;

        return x.index.CompareTo(y.index);
    }
}*/

internal readonly struct CuePositionRangeEnumerable(GroupCueViewModel? group, int startInd, int count) : IEnumerable<CuePosition>, IList<CuePosition>, IReadOnlyList<CuePosition>
{
    public CuePositionRangeEnumerable(CuePosition start, int count) : this(start.group, start.index, count) { }

    public CuePosition this[int index]
    {
        get => index >= 0 && index < count ? new(index + startInd, group) : throw new ArgumentOutOfRangeException(nameof(index));
        set => throw new InvalidOperationException();
    }

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
            return pos <= end;
        }

        public void Reset()
        {
            pos = -1;
        }
    }
}

