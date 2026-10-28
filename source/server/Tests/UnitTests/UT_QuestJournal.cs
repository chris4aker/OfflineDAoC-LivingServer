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
}
