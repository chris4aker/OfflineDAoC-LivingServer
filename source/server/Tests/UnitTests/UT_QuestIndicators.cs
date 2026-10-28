using System;
using System.Collections.Generic;
using DOL.Events;
using DOL.GS.Quests;
using NUnit.Framework;
using S = DOL.GS.Quests.DataQuest.eStepType;

namespace DOL.GS.Tests;

// The quest indicator rules and the meaning-to-wire table: turn in (blue), then the current step (white), then a quest
// to take (yellow) whose minimum level is at most 15 below the player's. From lometur/HearthDAoC.
[TestFixture]
public sealed class UT_QuestIndicators
{
    private const ushort CaerGothwaite = 51;

    private static readonly JournalStep[] NoSteps = Array.Empty<JournalStep>();
    private static readonly QuestOffer[] NoOffers = Array.Empty<QuestOffer>();

    // Offers that fail the test when read: the journal decided first.
    private static IEnumerable<QuestOffer> Unread()
    {
        Assert.Fail("the offers were read");
        yield break;
    }

    private static eQuestIndicator At(string npc, int level, JournalStep[] steps = null, bool rewardDone = false,
        IEnumerable<QuestOffer> offers = null, ushort region = CaerGothwaite) =>
        QuestIndicators.For(npc, region, level, steps ?? NoSteps, rewardDone, offers ?? NoOffers);

    private static JournalStep Step(S type, string target, ushort region = CaerGothwaite) => new(type, target, region);

    [Test]
    public void AQuestToTakeIsAvailable()
    {
        // A new level-7 Infiltrator: Elaru offers "Strange Beings" (level 7).
        Assert.That(At("Elaru", 7, offers: new[] { new QuestOffer(7, true) }), Is.EqualTo(eQuestIndicator.Available));
        Assert.That(At("Elaru", 6, offers: new[] { new QuestOffer(7, false) }), Is.EqualTo(eQuestIndicator.None));
    }

    [TestCase(22, ExpectedResult = eQuestIndicator.Available)] // 15 levels above the quest's minimum
    [TestCase(23, ExpectedResult = eQuestIndicator.None)] // 16 above
    [TestCase(30, ExpectedResult = eQuestIndicator.None)] // the spec's level-30 passer-by
    public eQuestIndicator TheFifteenLevelRule(int playerLevel) =>
        At("Elaru", playerLevel, offers: new[] { new QuestOffer(7, true) });

    [Test]
    public void AQuestWithoutAMinimumLevelHasNoLevelRule()
    {
        Assert.That(At("Elaru", 50, offers: new[] { new QuestOffer(null, true) }), Is.EqualTo(eQuestIndicator.Available));
    }

    [Test]
    public void OneOfferInRangeIsEnough()
    {
        Assert.That(At("Elaru", 30, offers: new[] { new QuestOffer(7, true), new QuestOffer(20, true) }),
            Is.EqualTo(eQuestIndicator.Available));
    }

    [TestCase(S.Interact)]
    [TestCase(S.Whisper)]
    [TestCase(S.Deliver)]
    [TestCase(S.Collect)]
    public void ACurrentStepAtTheNpcIsPending(S type)
    {
        Assert.That(At("Tage", 7, new[] { Step(type, "Tage") }, offers: Unread()), Is.EqualTo(eQuestIndicator.Pending));
    }

    [TestCase(S.InteractFinish)]
    [TestCase(S.WhisperFinish)]
    [TestCase(S.DeliverFinish)]
    [TestCase(S.CollectFinish)]
    public void ALastStepAtTheNpcIsFinish(S type)
    {
        Assert.That(At("Adam Glaze", 9, new[] { Step(type, "Adam Glaze") }, offers: Unread()),
            Is.EqualTo(eQuestIndicator.Finish));
    }

    [TestCase(S.Kill)]
    [TestCase(S.KillFinish)]
    [TestCase(S.Search)]
    [TestCase(S.SearchFinish)]
    [TestCase(S.Unknown)]
    public void KillAndSearchTargetsGetNothing(S type)
    {
        // "Cleric Mulgrut" (level 5) ends on killing Mulgrut Maggot: no blue knot on the monster.
        Assert.That(At("Mulgrut Maggot", 5, new[] { Step(type, "Mulgrut Maggot", 1) }, region: 1),
            Is.EqualTo(eQuestIndicator.None));
    }

    [Test]
    public void ARewardQuestWithEveryGoalDoneIsFinish()
    {
        Assert.That(At("Bounty Master", 20, rewardDone: true, offers: Unread()), Is.EqualTo(eQuestIndicator.Finish));
    }

    [Test]
    public void FinishThenPendingThenAvailable()
    {
        var available = new[] { new QuestOffer(7, true) };
        Assert.That(At("Elaru", 9, new[] { Step(S.Interact, "Elaru"), Step(S.InteractFinish, "Elaru") }, offers: available),
            Is.EqualTo(eQuestIndicator.Finish));
        Assert.That(At("Elaru", 9, new[] { Step(S.InteractFinish, "Elaru"), Step(S.Interact, "Elaru") }, offers: available),
            Is.EqualTo(eQuestIndicator.Finish));
        Assert.That(At("Elaru", 9, new[] { Step(S.Interact, "Elaru") }, offers: available), Is.EqualTo(eQuestIndicator.Pending));
        Assert.That(At("Elaru", 9, new[] { Step(S.Kill, "Elaru") }, offers: available), Is.EqualTo(eQuestIndicator.Available));
    }

    [Test]
    public void StepsElsewhereDoNotCount()
    {
        Assert.That(At("Elaru", 9, new[] { Step(S.Interact, "Tage") }), Is.EqualTo(eQuestIndicator.None));
        Assert.That(At("Tage", 9, new[] { Step(S.Interact, "Tage", 1) }), Is.EqualTo(eQuestIndicator.None));
        Assert.That(At("Tage", 9, new[] { Step(S.Interact, "") }), Is.EqualTo(eQuestIndicator.None));
    }

    [Test]
    public void NamesCompareWithoutCaseAndRegionZeroIsAnyRegion()
    {
        Assert.That(At("Tage", 9, new[] { Step(S.Interact, "tage") }), Is.EqualTo(eQuestIndicator.Pending));
        Assert.That(At("Tage", 9, new[] { Step(S.Interact, "TAGE", 0) }), Is.EqualTo(eQuestIndicator.Pending));
        Assert.That(At("Tage", 9, new[] { Step(S.Interact, "Tage ") }), Is.EqualTo(eQuestIndicator.None));
    }

    [Test]
    public void TheTableGivesTheKnots()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestIndicators.Wire(eQuestIndicator.Available), Is.EqualTo(eQuestIndicator.Lore)); // 8, yellow knot
            Assert.That(QuestIndicators.Wire(eQuestIndicator.Pending), Is.EqualTo(eQuestIndicator.Pending)); // 16, white knot
            Assert.That(QuestIndicators.Wire(eQuestIndicator.Finish), Is.EqualTo(eQuestIndicator.Finish)); // 2, blue knot
            Assert.That(QuestIndicators.Wire(eQuestIndicator.None), Is.EqualTo(eQuestIndicator.None));
            // Atlas master trainers and the buff merchant say "lore": nothing now.
            Assert.That(QuestIndicators.Wire(eQuestIndicator.Lore), Is.EqualTo(eQuestIndicator.None));
            Assert.That(QuestIndicators.Wire(eQuestIndicator.Lesson), Is.EqualTo(eQuestIndicator.None));
            Assert.That(QuestIndicators.Wire((eQuestIndicator)0x20), Is.EqualTo(eQuestIndicator.None));
        });
    }

    [Test]
    public void AQuestEffectGoesOnlyToAnNpcInTheWorldInThePlayersRegion()
    {
        // The packet carries only the object ID: in another region, or after the NPC left the world, that ID is another
        // object on the client (e.g. abandoning a quest whose giver is in Camelot, from the mainland).
        Assert.Multiple(() =>
        {
            Assert.That(QuestIndicators.SendsTo(npcInWorld: true, npcRegion: 51, playerRegion: 51), Is.True);
            Assert.That(QuestIndicators.SendsTo(npcInWorld: true, npcRegion: 10, playerRegion: 1), Is.False);
            Assert.That(QuestIndicators.SendsTo(npcInWorld: false, npcRegion: 51, playerRegion: 51), Is.False);
        });
    }

    [Test]
    public void AScriptedQuestIsOfferedWhileTheTimesDoneStayUnderItsLimit()
    {
        // GameNPC.CanShowOneQuest's test: finished, plus the one being done, under MaxQuestCount.
        Assert.Multiple(() =>
        {
            Assert.That(QuestIndicators.ScriptedOffer(48, qualifies: true, finished: 0, doing: false, maxCount: 1),
                Is.EqualTo(new QuestOffer(48, true)));
            Assert.That(QuestIndicators.ScriptedOffer(48, true, finished: 1, doing: false, maxCount: 1).Qualifies, Is.False);
            Assert.That(QuestIndicators.ScriptedOffer(48, true, finished: 0, doing: true, maxCount: 1).Qualifies, Is.False);
            Assert.That(QuestIndicators.ScriptedOffer(null, true, finished: 1, doing: true, maxCount: 3).Qualifies, Is.True);
            Assert.That(QuestIndicators.ScriptedOffer(48, qualifies: false, finished: 0, doing: false, maxCount: 1).Qualifies,
                Is.False);
        });
    }

    [Test]
    public void ADataQuestWithoutAMarkerIsNotAskedToQualify()
    {
        // ShowIndicator first: a Collection quest's qualification reads the database.
        Assert.That(QuestIndicators.DataOffer(false, 7, () => { Assert.Fail("asked to qualify"); return true; }, out _),
            Is.False);
        Assert.That(QuestIndicators.DataOffer(true, 7, () => true, out QuestOffer offer), Is.True);
        Assert.That(offer, Is.EqualTo(new QuestOffer(7, true)));
        Assert.That(QuestIndicators.DataOffer(true, 7, () => false, out offer), Is.True);
        Assert.That(offer, Is.EqualTo(new QuestOffer(7, false)));
    }

    private static IEnumerable<bool> Unread(string why)
    {
        Assert.Fail(why);
        yield break;
    }

    [Test]
    public void ARewardQuestTurnsInOnlyAtItsOwnGiverWithEveryGoalDone()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestIndicators.RewardDone(ownGiver: false, Unread("another giver's goals were read")), Is.False);
            Assert.That(QuestIndicators.RewardDone(ownGiver: true, new[] { true, true }), Is.True);
            Assert.That(QuestIndicators.RewardDone(ownGiver: true, new[] { true, false }), Is.False);
        });
    }

    // Scripted quests: Level counts as the minimum level only when the class declares it.
    private abstract class Stub : AbstractQuest
    {
        public override bool CheckQuestQualification(GamePlayer player) => true;
        public override void Notify(DOLEvent e, object sender, EventArgs args) { }
    }

    private sealed class NoLevel : Stub { }

    private class Level48 : Stub
    {
        public override int Level => 48;
    }

    private sealed class InheritsLevel : Level48 { }

    [Test]
    public void AScriptedQuestsLevelCountsOnlyWhenItsClassDeclaresOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(new NoLevel().Level, Is.EqualTo(1));
            Assert.That(QuestIndicators.ScriptedMinLevel(new NoLevel()), Is.Null);
            Assert.That(QuestIndicators.ScriptedMinLevel(new Level48()), Is.EqualTo(48));
            Assert.That(QuestIndicators.ScriptedMinLevel(new InheritsLevel()), Is.EqualTo(48));
        });
    }

    // Atlas's daily, weekly and monthly quests declare a Level (TeamBuildingAlb 1, PlayerKillWeeklyQuestAlb 1, ...) but
    // follow the player's level: no level rule (owner, 2026-10-11).
    private sealed class Daily : Quests.DailyQuest
    {
        public override string QuestPropertyKey { get; set; } = "test";
        public override int Level => 1;
        public override void LoadQuestParameters() { }
        public override void SaveQuestParameters() { }
        public override void Notify(DOLEvent e, object sender, EventArgs args) { }
    }

    private sealed class Weekly : Quests.WeeklyQuest
    {
        public override string QuestPropertyKey { get; set; } = "test";
        public override int Level => 1;
        public override void LoadQuestParameters() { }
        public override void SaveQuestParameters() { }
        public override void Notify(DOLEvent e, object sender, EventArgs args) { }
    }

    private sealed class Monthly : Quests.MonthlyQuest
    {
        public override string QuestPropertyKey { get; set; } = "test";
        public override int Level => 45;
        public override void LoadQuestParameters() { }
        public override void SaveQuestParameters() { }
        public override void Notify(DOLEvent e, object sender, EventArgs args) { }
    }

    [Test]
    public void DailyWeeklyAndMonthlyQuestsHaveNoLevelRule()
    {
        Assert.Multiple(() =>
        {
            Assert.That(QuestIndicators.ScriptedMinLevel(new Daily()), Is.Null);
            Assert.That(QuestIndicators.ScriptedMinLevel(new Weekly()), Is.Null);
            Assert.That(QuestIndicators.ScriptedMinLevel(new Monthly()), Is.Null);
            // So a level-50 player still sees a level-1 daily's giver lit.
            Assert.That(At("Hector", 50, offers: new[] { new QuestOffer(QuestIndicators.ScriptedMinLevel(new Daily()), true) }),
                Is.EqualTo(eQuestIndicator.Available));
        });
    }
}
