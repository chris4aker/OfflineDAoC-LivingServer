using System;
using DOL.Events;
using DOL.GS.Quests;
using NUnit.Framework;

namespace DOL.GS.Tests;

// GamePlayer.AddQuest's journal rules: 25 quests, and a quest the player is on isn't added twice (data quests by ID,
// scripted quests by class). From lometur/HearthDAoC.
[TestFixture]
public sealed class UT_QuestJournal
{
    private sealed class FakeDataQuest : DataQuest
    {
        private readonly int _id;
        public FakeDataQuest(int id) => _id = id;
        public override int ID => _id;
    }

    private abstract class Scripted : AbstractQuest
    {
        public override bool CheckQuestQualification(GamePlayer player) => true;
        public override void Notify(DOLEvent e, object sender, EventArgs args) { }
    }

    private sealed class ScriptedA : Scripted { }
    private sealed class ScriptedB : Scripted { }

    [Test]
    public void TheJournalHoldsTwentyFiveQuests()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestJournal.Capacity, Is.EqualTo(25));
            Assert.That(QuestJournal.IsFull(0), Is.False);
            Assert.That(QuestJournal.IsFull(24), Is.False);
            Assert.That(QuestJournal.IsFull(25), Is.True);
            Assert.That(QuestJournal.IsFull(26), Is.True);
        });
    }

    [Test]
    public void DataQuestsAreTheSameQuestOnlyWithTheSameId()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestJournal.Same(new FakeDataQuest(20171), new FakeDataQuest(20171)), Is.True);
            Assert.That(QuestJournal.Same(new FakeDataQuest(20171), new FakeDataQuest(20435)), Is.False);
        });
    }

    [Test]
    public void ScriptedQuestsAreTheSameQuestByTheirClass()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestJournal.Same(new ScriptedA(), new ScriptedA()), Is.True);
            Assert.That(QuestJournal.Same(new ScriptedA(), new ScriptedB()), Is.False);
            Assert.That(QuestJournal.Same(new ScriptedA(), new FakeDataQuest(1)), Is.False);
            Assert.That(QuestJournal.Same(new FakeDataQuest(1), new ScriptedA()), Is.False);
        });
    }

    // An older save with more than 25 active quests (owner 2026-10-30): none is aborted, the extras wait for a slot,
    // and login says how many.
    [Test]
    public void UpToTheCapacityNothingWaits()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestJournal.WaitingCount(0), Is.Zero);
            Assert.That(QuestJournal.WaitingCount(25), Is.Zero);
            Assert.That(QuestJournal.WaitingText(0), Is.Null);
            Assert.That(QuestJournal.WaitingText(25), Is.Null);
        });
    }

    [Test]
    public void TheQuestsPastTheCapacityWait()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestJournal.WaitingCount(26), Is.EqualTo(1));
            Assert.That(QuestJournal.WaitingCount(31), Is.EqualTo(6));
        });
    }

    [Test]
    public void LoginSaysHowManyWaitInOneLine()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestJournal.WaitingText(26), Is.EqualTo("Your journal shows 25 of your 26 quests; the other 1 is still active and will be listed as you finish or abandon some."));
            Assert.That(QuestJournal.WaitingText(28), Is.EqualTo("Your journal shows 25 of your 28 quests; the other 3 are still active and will be listed as you finish or abandon some."));
            Assert.That(QuestJournal.WaitingText(28), Does.Not.Contain("\n"));
        });
    }

    // A slot is reused only while no quest holds it: a quest that moved in from past the capacity keeps its slot even
    // though that slot was queued as free when its previous quest ended.
    [Test]
    public void TheFreeSlotIsTheLowestNoQuestHolds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestJournal.FreeIndex(null), Is.Zero);
            Assert.That(QuestJournal.FreeIndex(new byte[0]), Is.Zero);
            Assert.That(QuestJournal.FreeIndex(new byte[] { 0, 1, 2 }), Is.EqualTo(3));
            Assert.That(QuestJournal.FreeIndex(new byte[] { 0, 2, 3 }), Is.EqualTo(1));
            Assert.That(QuestJournal.FreeIndex(new byte[] { 1, 2 }), Is.Zero);
        });
    }

    [Test]
    public void AFullJournalsFreeSlotIsPastTheLastOne()
    {
        var used = new byte[QuestJournal.Capacity];
        for (byte i = 0; i < used.Length; i++) used[i] = i;
        Assert.That(QuestJournal.FreeIndex(used), Is.EqualTo((byte)QuestJournal.Capacity));
    }
}
