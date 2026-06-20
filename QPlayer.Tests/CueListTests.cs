using QPlayer.Models;
using QPlayer.ViewModels;
using System;
using System.Collections.Generic;
using System.Text;
using TUnit.Assertions;
using TUnit.Assertions.Should;
using TUnit.Assertions.Should.Extensions;

namespace QPlayer.Tests;

public class CueListTests
{
    static readonly MainViewModel mainVM = new();

    private const int defaultCount = 10;
    private static CueViewModel MakeCue() => new DummyCueViewModel(mainVM);
    private static GroupCueViewModel MakeGroupCue() => new(mainVM);

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
        var group = MakeGroupCue();
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
        var (cl, group) = MakeGroupCueList();

        await Assert.That(cl.Delete(1)?.QID).IsNotNull().And.IsEqualTo(1);
        (cl, group) = MakeGroupCueList();
        await Assert.That(cl.Delete(new AbstractCueList.CuePosition(1, null))?.QID).IsNotNull().And.IsEqualTo(1);
        (cl, group) = MakeGroupCueList();
        await Assert.That(cl.Delete(cl[1])).IsTrue();
        (cl, group) = MakeGroupCueList();
        await Assert.That(cl.Delete(MakeCue())).IsFalse();

        (cl, group) = MakeGroupCueList();
        await Assert.That(
            ToQIDs(
                cl.Delete(cl.Skip(1).Take(3))
                ))
            .Count().IsEqualTo(3)
            .And.IsInOrder()
            .And.IsEquivalentTo([(decimal)1,2,3]);
    }

    [Test]
    public async Task TestInsert()
    {
        var (cl, group) = MakeGroupCueList();

        await cl.Count.Should().BeEqualTo(defaultCount);

        //cl.Insert(,)
    }
}
