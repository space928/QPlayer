using QPlayer.Models;
using QPlayer.Utilities;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Text;
using TUnit.Assertions;
using TUnit.Assertions.Should;
using TUnit.Assertions.Should.Extensions;
using TUnit.Core.Helpers;

namespace QPlayer.Tests;

public class CueListTests
{
    static readonly MainViewModel mainVM = new();

    private const int defaultCount = 10;
    private static CueViewModel MakeCue() => new DummyCueViewModel(mainVM);
    private static GroupCueViewModel MakeGroupCue() => new(mainVM);
    private static GroupCueViewModel MakeGroupCue(CueList owner) => new(mainVM, owner);

    static CueListTests()
    {
        // Hack to prevent the undo manager from asserting when being used across threads.
        for (int i = 0; i < 16; i++)
            UndoManager.SuppressRecording();
    }

    private static CueList MakeCueList()
    {
        CueList cl = new();
        List<Cue> model = [];
        cl.Bind(model);
        for (int i = 0; i < defaultCount; i++)
        {
            var cue = MakeCue();
            cue.QID = i;
            cl.Insert(i, cue);
        }
        return cl;
    }

    private static (CueList, GroupCueViewModel) MakeGroupCueList()
    {
        CueList cl = new();
        List<Cue> model = [];
        cl.Bind(model);
        int i = 0;
        // Normal cues
        for (; i < defaultCount - 5; i++)
        {
            var cue = MakeCue();
            cue.QID = i;
            cl.Insert(i, cue);
        }

        // A group
        var group = MakeGroupCue(cl);
        group.QID = i;
        cl.Insert(i, group);
        i++;

        // Fill group
        int j = 0;
        for (; i < defaultCount - 1; i++)
        {
            var cue = MakeCue();
            cue.QID = i;
            cl.Insert(new AbstractCueList.CuePosition(j++, group), cue);
        }

        // Normal cues
        for (; i < defaultCount; i++)
        {
            var cue = MakeCue();
            cue.QID = i;
            cl.Insert(i, cue);
        }

        return (cl, group);
    }

    private static decimal[] ToQIDs(IEnumerable<CueViewModel> cues) => cues.Select(x => x.QID).ToArray();

    [Test]
    public async Task TestMisc()
    {
        var cl = MakeCueList();

        await cl.Count.Should().BeEqualTo(defaultCount);
        await cl.boundModel.Should().NotBeNull();
        await cl.boundModel!.Count.Should().NotBeZero(); // TODO: more tests for bound model sync

        cl.Clear();

        await cl.Count.Should().BeZero();
        await cl.boundModel!.Count.Should().BeZero();
    }

    [Test]
    public async Task TestGetters()
    {
        var (cl, group) = MakeGroupCueList();

        var first = cl[0];
        await first.QID.Should().BeEqualTo(0);

        await cl.Contains(first).Should().BeTrue();
        await cl.Contains(MakeCue()).Should().BeFalse();

        var items = ToQIDs(cl.EnumerateAll());
        await items.Should().HaveCount(defaultCount);
        await items.Should().BeInOrder();

        items = ToQIDs(cl.EnumerateAllFrom(5));
        await items.Should().HaveCount(defaultCount - 5);
        await items.Should().BeInOrder();

        items = ToQIDs(cl.EnumerateVisible());
        await items.Should().HaveCount(defaultCount);
        await items.Should().BeInOrder();

        group.IsCollapsed = true;
        items = ToQIDs(cl.EnumerateVisible());
        await items.Should().HaveCount(defaultCount - group.Cues.Count);
        await items.Should().BeInOrder();
        group.IsCollapsed = false;

        // TODO: These tests all test success conditions and not the failure conditions
        decimal qid = 9;
        await cl.Find(qid, out AbstractCueList.CuePosition pos).Should().BeTrue();
        await cl.FindVisualIndex(pos, out int visPos).Should().BeTrue();

        await visPos.Should().BeEqualTo((int)qid);
        await cl[pos].Should().BeEqualTo(cl[visPos]);

        await cl.Find(qid, out CueViewModel? cue).Should().BeTrue();
        await cue.Should().BeEqualTo(cl[pos]);
        await cl.Find(visPos, out var pos2).Should().BeTrue();
        await pos2.Should().BeEqualTo(pos);
        await cl.Find(cue!, out var pos3).Should().BeTrue();
        await pos3.Should().BeEqualTo(pos);

        await cl.FindVisualIndex(cue!, out var visPos2).Should().BeTrue();
        await visPos2.Should().BeEqualTo(visPos);
    }

    [Test]
    public async Task TestDelete()
    {
        var (cl, _) = MakeGroupCueList();
        var startCount = cl.TotalCount;

        await Assert.That(cl.Delete(1)?.QID).IsNotNull().And.IsEqualTo(1);
        await cl.TotalCount.Should().BeEqualTo(startCount - 1);
        (cl, _) = MakeGroupCueList();
        await Assert.That(cl.Delete(new AbstractCueList.CuePosition(1, null))?.QID).IsNotNull().And.IsEqualTo(1);
        await cl.TotalCount.Should().BeEqualTo(startCount - 1);
        (cl, _) = MakeGroupCueList();
        await Assert.That(cl.Delete(cl[1])).IsTrue();
        await cl.TotalCount.Should().BeEqualTo(startCount - 1);
        (cl, _) = MakeGroupCueList();
        await Assert.That(cl.Delete(MakeCue())).IsFalse(); // Should fail on a cue that isn't in the list
        await cl.TotalCount.Should().BeEqualTo(startCount);

        (cl, _) = MakeGroupCueList();
        await Assert.That(
            ToQIDs(
                cl.Delete(cl.Skip(1).Take(3))
                ))
            .Count().IsEqualTo(3)
            .And.IsInOrder()
            .And.IsEquivalentTo([(decimal)1, 2, 3]);
        await cl.TotalCount.Should().BeEqualTo(startCount - 3);
    }

    [Test]
    public async Task TestDelete_Single_Events()
    {
        var (cl, group) = MakeGroupCueList();

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        // Delete by vispos
        var deleted1 = cl.Delete(1);
        await deleted1.Should().NotBeNull();
        await deleted1!.QID.Should().BeEqualTo(1);

        var event1 = events.Last();
        await event1.Action.Should().BeEqualTo(NotifyCollectionChangedAction.Remove);
        await event1.OldStartingIndex.Should().BeEqualTo(1);
        await event1.OldItems![0].Should().BeEqualTo(deleted1);

        await cl.Find(deleted1.QID, out CueViewModel? _).Should().BeFalse();

        // Delete by cuepos
        var deleted2 = cl.Delete(new AbstractCueList.CuePosition(1, null));
        await deleted1.Should().NotBeNull();
        await deleted2!.QID.Should().BeEqualTo(2);

        var event2 = events.Last();
        await event2.Action.Should().BeEqualTo(NotifyCollectionChangedAction.Remove);
        await event2.OldStartingIndex.Should().BeEqualTo(1);
        await event2.OldItems![0].Should().BeEqualTo(deleted2);

        await cl.Find(deleted2.QID, out CueViewModel? _).Should().BeFalse();
    }

    [Test]
    public async Task TestDelete_Multiple_Events()
    {
        var (cl, group) = MakeGroupCueList();
        var cuesToDelete = cl.Skip(1).Take(3).ToList();

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        var deletedCues = cl.Delete(cuesToDelete);

        await ToQIDs(deletedCues).Should().BeEquivalentTo([(decimal)1, 2, 3]);
        await ToQIDs(cl.EnumerateAll()).Should().BeEquivalentTo([0m, 4, 5, 6, 7, 8, 9]);

        // Delete() for an enumerable of cues is expected to fire a single reset
        await events.Count.Should().BeEqualTo(1);
        var resetEvent = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Reset);
        await resetEvent.Should().NotBeNull();

        await deletedCues.Should().All(x=> !cl.Find(x.QID, out CueViewModel? _));
    }

    [Test]
    public async Task TestDelete_Group()
    {
        var (cl, group) = MakeGroupCueList();
        var startCount = cl.TotalCount;

        // Ensure we know exactly where the group is
        int groupVisPos = cl.FindVisualIndex(group);
        await cl[groupVisPos].Should().BeEqualTo(group);

        var groupCues = new OneEnumerable<CueViewModel>(group).Concat(group.Cues).ToArray();

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        bool deleted = cl.Delete(group);
        await deleted.Should().BeTrue();

        // We should have a single Remove event with the group's cues
        var removeEvent = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Remove);
        await removeEvent.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await removeEvent!.OldStartingIndex.Should().BeEqualTo(groupVisPos);
        await removeEvent.OldItems!.Count.Should().BeEqualTo(groupCues.Length);
        await removeEvent.OldItems[0].Should().BeEqualTo(group);
        await ToQIDs(removeEvent.OldItems.Cast<CueViewModel>()).Should().BeEquivalentTo(ToQIDs(groupCues));

        await cl.TotalCount.Should().BeEqualTo(startCount - removeEvent.OldItems!.Count);
        await groupCues.Should().All(x => !cl.Find(x.QID, out CueViewModel? _));
    }

    [Test]
    public async Task TestDelete_Group_Collapsed()
    {
        var (cl, group) = MakeGroupCueList();
        var startCount = cl.TotalCount;

        // Ensure we know exactly where the group is
        int groupVisPos = cl.FindVisualIndex(group);
        await cl[groupVisPos].Should().BeEqualTo(group);

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        var groupContents = group.Cues.ToArray();
        var groupCues = new OneEnumerable<CueViewModel>(group).Concat(groupContents).ToArray();

        // Collapse the group
        group.IsCollapsed = true;

        // Collapsing visually removes the children starting from the group's index + 1
        var removeEvent1 = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Remove);
        await removeEvent1.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await removeEvent1!.OldStartingIndex.Should().BeEqualTo(groupVisPos + 1);
        await removeEvent1.OldItems!.Count.Should().BeEqualTo(groupContents.Length);

        await cl.Count.Should().BeEqualTo(startCount - groupContents.Length);
        await cl.TotalCount.Should().BeEqualTo(startCount);

        // Delete the collapsed group
        events.Clear();

        bool deleted = cl.Delete(group);
        await deleted.Should().BeTrue();

        // The delete event should ONLY contain the group cue now, as children are already hidden
        var removeEvent2 = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Remove);
        await removeEvent2.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await removeEvent2!.OldStartingIndex.Should().BeEqualTo(groupVisPos);
        await removeEvent2.OldItems!.Count.Should().BeEqualTo(1);
        await removeEvent2.OldItems[0].Should().BeEqualTo(group);

        await cl.Count.Should().BeEqualTo(startCount - groupCues.Length);
        await cl.TotalCount.Should().BeEqualTo(startCount - groupCues.Length);
    }

    [Test]
    public async Task TestInsert_Single_Events()
    {
        var (cl, group) = MakeGroupCueList();
        int startCount = cl.TotalCount;
        int targetIndex = 2;

        var singleCue = MakeCue();
        singleCue.QID = 99;

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        // Insert
        int insertedVisPos = cl.Insert(targetIndex, singleCue);

        // Check state
        await insertedVisPos.Should().BeEqualTo(targetIndex);
        await cl[targetIndex].QID.Should().BeEqualTo(99);
        await cl.TotalCount.Should().BeEqualTo(startCount + 1);

        // Check events
        var addEvent = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Add);
        await addEvent.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await addEvent!.NewStartingIndex.Should().BeEqualTo(targetIndex);
        await addEvent.NewItems!.Count.Should().BeEqualTo(1);
        await addEvent.NewItems[0].Should().BeEqualTo(singleCue);

        await cl.Find(singleCue.QID, out CueViewModel? _).Should().BeTrue();
    }

    [Test]
    public async Task TestInsert_Multiple_Events()
    {
        var (cl, group) = MakeGroupCueList();
        int startCount = cl.TotalCount;
        int targetIndex = 5;

        var newCue1 = MakeCue(); newCue1.QID = 101;
        var newCue2 = MakeCue(); newCue2.QID = 102;
        var newCues = new[] { newCue1, newCue2 };

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        // Insert multiple
        int firstInsertedVisPos = cl.Insert(targetIndex, newCues);

        // Check state
        await firstInsertedVisPos.Should().BeEqualTo(targetIndex);
        await cl[targetIndex].QID.Should().BeEqualTo(101);
        await cl[targetIndex + 1].QID.Should().BeEqualTo(102);
        await cl.TotalCount.Should().BeEqualTo(startCount + 2);

        // Check events
        var addEvent = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Add);
        await addEvent.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await addEvent!.NewStartingIndex.Should().BeEqualTo(targetIndex);
        await addEvent.NewItems!.Count.Should().BeEqualTo(2);
        await addEvent.NewItems[0].Should().BeEqualTo(newCue1);
        await addEvent.NewItems[1].Should().BeEqualTo(newCue2);
    }

    [Test]
    public async Task TestInsert_MultipleInds_Events()
    {
        var (cl, group) = MakeGroupCueList();
        int startCount = cl.TotalCount;

        var newCue1 = MakeCue(); newCue1.QID = 101;
        var newCue2 = MakeCue(); newCue2.QID = 102;
        var newCues = new[] { newCue1, newCue2 };

        int[] targetIndices = [1, 4];

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        // Insert multiple
        int[] insertedVisPos = cl.Insert(targetIndices, newCues);

        // Check state
        await insertedVisPos.Should().HaveCount(2);
        await cl.TotalCount.Should().BeEqualTo(startCount + 2);

        // They should both have been inserted in the correct order
        await cl[insertedVisPos[0]].QID.Should().BeEqualTo(101);
        await cl[insertedVisPos[1]].QID.Should().BeEqualTo(102);

        // Inserting multiple items at different indexes should trigger a reset event
        var resetEvent = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Reset);
        await resetEvent.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await events.Any(e => e.Action == NotifyCollectionChangedAction.Add).Should().BeFalse();

        await newCues.Should().All(x => cl.Find(x.QID, out CueViewModel? _));
    }

    [Test]
    public async Task TestInsert_Group_Collapsed()
    {
        var (cl, group) = MakeGroupCueList();
        var startCount = cl.TotalCount;

        // Ensure we know exactly where the group is
        int groupVisPos = cl.FindVisualIndex(group);
        await cl[groupVisPos].Should().BeEqualTo(group);

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        var groupContents = group.Cues.ToArray();
        var groupCues = new OneEnumerable<CueViewModel>(group).Concat(groupContents).ToArray();

        // Collapse the group
        group.IsCollapsed = true;

        // Delete the collapsed group
        bool deleted = cl.Delete(group);
        await deleted.Should().BeTrue();

        // These events are checked by another test
        events.Clear();

        // Now reinsert the group and check that we get all the right insert messages
        cl.Insert(2, group);

        // The insert event should ONLY contain the group cue now, as children are already hidden
        var addEvent1 = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Add);
        await addEvent1.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await addEvent1!.NewStartingIndex.Should().BeEqualTo(2);
        await addEvent1.NewItems!.Count.Should().BeEqualTo(1);
        await addEvent1.NewItems[0].Should().BeEqualTo(group);

        await cl.Count.Should().BeEqualTo(startCount - groupContents.Length);
        await cl.TotalCount.Should().BeEqualTo(startCount);

        await groupCues.Should().All(x => cl.Find(x.QID, out CueViewModel? _));

        events.Clear();

        // Uncollapse the group and check we get the right events
        group.IsCollapsed = false;

        var addEvent2 = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Add);
        await addEvent2.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await addEvent2!.NewStartingIndex.Should().BeEqualTo(3);
        await addEvent2.NewItems!.Count.Should().BeEqualTo(groupContents.Length);
        await addEvent2.NewItems[0].Should().BeEqualTo(groupContents[0]);

        await cl.Count.Should().BeEqualTo(startCount);
        await cl.TotalCount.Should().BeEqualTo(startCount);
    }

    [Test]
    public async Task TestInsert_Group_Nested()
    {
        /*var (cl, group) = MakeGroupCueList();
        var startCount = cl.TotalCount;

        // Ensure we know exactly where the group is
        int groupVisPos = cl.FindVisualIndex(group);
        await cl[groupVisPos].Should().BeEqualTo(group);

        List<NotifyCollectionChangedEventArgs> events = [];
        cl.CollectionChanged += (s, e) => events.Add(e);

        var newGroup = MakeGroupCue(cl);
        newGroup.Cues.Insert(0, MakeCue());
        newGroup.Cues.Insert(1, MakeCue());
        newGroup.Cues[0].QID = 101;
        newGroup.Cues[0].QID = 102;

        var groupContents = group.Cues.ToArray();
        var groupCues = new OneEnumerable<CueViewModel>(group).Concat(groupContents).ToArray();

        // Collapse the group
        group.IsCollapsed = true;

        events.Clear();

        // Now insert the new group and check that we get all the right insert messages
        cl.Insert(2, group);

        // The insert event should ONLY contain the group cue now, as children are already hidden
        var addEvent1 = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Add);
        await addEvent1.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await addEvent1!.NewStartingIndex.Should().BeEqualTo(2);
        await addEvent1.NewItems!.Count.Should().BeEqualTo(1);
        await addEvent1.NewItems[0].Should().BeEqualTo(group);

        await cl.Count.Should().BeEqualTo(startCount - groupContents.Length);
        await cl.TotalCount.Should().BeEqualTo(startCount);

        await groupCues.Should().All(x => cl.Find(x.QID, out CueViewModel? _));

        events.Clear();

        // Uncollapse the group and check we get the right events
        group.IsCollapsed = false;

        var addEvent2 = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Add);
        await addEvent2.Should().NotBeNull();
        await events.Count.Should().BeEqualTo(1);
        await addEvent2!.NewStartingIndex.Should().BeEqualTo(3);
        await addEvent2.NewItems!.Count.Should().BeEqualTo(groupContents.Length);
        await addEvent2.NewItems[0].Should().BeEqualTo(groupContents[0]);

        await cl.Count.Should().BeEqualTo(startCount);
        await cl.TotalCount.Should().BeEqualTo(startCount);*/
    }

    [Test]
    public async Task TestCuePositionComparer()
    {
        var comparer = new CueList.CuePositionComparer();
        var group = MakeGroupCue();

        var posNullGroup = new AbstractCueList.CuePosition(0, null);
        var posWithGroup = new AbstractCueList.CuePosition(0, group);
        var posNullGroupHigherIndex = new AbstractCueList.CuePosition(1, null);

        // x.group == null && y.group != null should return 1
        await comparer.Compare(posNullGroup, posWithGroup).Should().BeGreaterThan(0);

        // y.group == null && x.group != null should return -1
        await comparer.Compare(posWithGroup, posNullGroup).Should().BeLessThan(0);

        await comparer.Compare(posNullGroup, posNullGroupHigherIndex).Should().BeLessThan(0);
        await comparer.Compare(posNullGroupHigherIndex, posNullGroup).Should().BeGreaterThan(0);
    }

    [Test]
    public async Task TestClear_Events()
    {
        var cl = MakeCueList();

        var events = new List<NotifyCollectionChangedEventArgs>();
        cl.CollectionChanged += (s, e) => events.Add(e);

        cl.Clear();

        var resetEvent = events.FirstOrDefault(e => e.Action == NotifyCollectionChangedAction.Reset);
        await resetEvent.Should().NotBeNull();
        await events.Any(e => e.Action == NotifyCollectionChangedAction.Remove).Should().BeFalse();
    }
}
