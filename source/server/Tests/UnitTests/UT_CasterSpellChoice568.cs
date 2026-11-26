using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #568: caster spell choice like a veteran (C1-C8). Pure rules only; tests, not an in-game check.
    /// </summary>
    [TestFixture]
    public class UT_CasterSpellChoice568
    {
        // ---- C1: the mezzed add (DividedRealms 9e18aa53 spec) -------------------------------------------------------

        [Test]
        public void C1_AGroupmatesFightCountsAsTheFight() => Assert.Multiple(() =>
        {
            Assert.That(BotBrain.UncontrolledFightRule(true, false), Is.True, "its own list has a free monster");
            Assert.That(BotBrain.UncontrolledFightRule(false, true), Is.True,
                "only the mezzed add on its own list, but a groupmate fights another monster");
            Assert.That(BotBrain.UncontrolledFightRule(false, false), Is.False, "everything left is controlled");
            // the companion leaves the mezzed add alone while the group fights elsewhere ...
            Assert.That(BotBrain.PassOverControlled(true, BotBrain.UncontrolledFightRule(false, true)), Is.True);
            // ... and breaks it only when nothing else is left
            Assert.That(BotBrain.PassOverControlled(true, BotBrain.UncontrolledFightRule(false, false)), Is.False);
            Assert.That(BotBrain.PassOverControlled(false, BotBrain.UncontrolledFightRule(false, true)), Is.False);
        });

        // ---- C8: "Attack spells: None" keeps the self bladeturn up --------------------------------------------------

        [Test]
        public void C8_AttackSpellsNoneRecastsTheBladeturnInAFight() => Assert.Multiple(() =>
        {
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(true, CompanionSpellOptions165.AttackNone), Is.True,
                "a Runemaster set to None");
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(true, 0), Is.False, "Automatic");
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(true, CompanionSpellOptions165.AttackAreaOnly), Is.False);
            Assert.That(CompanionSpellOptions165.KeepsBladeturnInFight(false, CompanionSpellOptions165.AttackNone), Is.False,
                "gamebots never");
            Assert.That(BotBrain.SelfBladeturnAggroWaits(false, true), Is.True, "the ordinary upkeep waits for the fight's end");
            Assert.That(BotBrain.SelfBladeturnAggroWaits(true, true), Is.False, "None: aggro is no reason to wait");
            Assert.That(BotBrain.SelfBladeturnSafe(true, BotBrain.SelfBladeturnAggroWaits(true, true), false, false, false, 3_000),
                Is.True, "in a fight, nothing hit it for 3 s");
            Assert.That(BotBrain.SelfBladeturnSafe(true, BotBrain.SelfBladeturnAggroWaits(true, true), false, false, false, 1_000),
                Is.False, "still never while being hit");
        });
    }
}
