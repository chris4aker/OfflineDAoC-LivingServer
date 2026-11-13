using NUnit.Framework;
using Tactics = DOL.GS.EpicEncounterTactics;

namespace DOL.GS.Tests
{
    /// <summary>
    /// "Clear first" (1.2): monsters that stand around a boss and come into its fight are killed before it (Spindler
    /// Broodmother's adult spindlers; play test duo28's damage ledger). The pure rule and the catalog row; the route's use of
    /// it is covered by play tests only.
    /// </summary>
    [TestFixture]
    public class UT_ClearFirst
    {
        [Test]
        public void TheBroodmothersSpindlersAreClearedFirst()
        {
            Tactics.Tactic tactic = Tactics.For("SpindlerBroodmother");
            Assert.That(tactic, Is.Not.Null);
            Assert.That(tactic.ClearFirst, Is.EqualTo(new[] { "spindler" }));
            // Seven stand 1,466 to 2,532 from her spawn in the 0.37 world; the next ones are 3,128 and more away.
            Assert.That(Tactics.IsClearFirst(tactic, "spindler", 1_466), Is.True);
            Assert.That(Tactics.IsClearFirst(tactic, "spindler", 2_532), Is.True);
            Assert.That(Tactics.IsClearFirst(tactic, "spindler", 3_128), Is.False, "the far ones are not her fight");
            Assert.That(Tactics.IsClearFirst(tactic, "Newly-born spindler", 100), Is.False, "her newborns are fought in the fight");
            Assert.That(Tactics.IsClearFirst(tactic, "Spindler Broodmother", 0), Is.False);
        }

        [Test]
        public void ABossWithoutTheRuleClearsNothingFirst()
        {
            Assert.That(Tactics.IsClearFirst(null, "spindler", 10), Is.False);
            Assert.That(Tactics.IsClearFirst(Tactics.For("Xaga"), "spindler", 10), Is.False);
            Assert.That(Tactics.IsClearFirst(Tactics.For("SpindlerBroodmother"), null, 10), Is.False);
        }
    }
}
