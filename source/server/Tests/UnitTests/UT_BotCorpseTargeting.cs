using DOL.GS;
using NUnit.Framework;
using Corpse = DOL.GS.BotCorpseTargeting.Corpse;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.1: a player's resurrection reaches a dead bot the original client can't select (owner: dead bots "aren't able
    /// to be targeted or have their corpse selected so the player can rez"). With nothing dead selected the spell goes to
    /// a dead bot in range: the one named with /target, else a groupmate, else the nearest (BotCorpseTargeting).
    /// </summary>
    [TestFixture]
    public class UT_BotCorpseTargeting
    {
        [Test]
        public void OnlyACastWithNothingDeadSelectedLooksForACorpse()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotCorpseTargeting.LooksForCorpse(false, false), Is.True, "no target");
                Assert.That(BotCorpseTargeting.LooksForCorpse(true, true), Is.True, "a living target (or the caster)");
                Assert.That(BotCorpseTargeting.LooksForCorpse(true, false), Is.False, "a dead target the client selected stands");
            });
        }

        [Test]
        public void TheNearestCorpseInRangeIsChosen()
        {
            Corpse[] corpses = [new(false, false, 900), new(false, false, 300), new(false, false, 1400)];
            Assert.That(BotCorpseTargeting.Choose(corpses, 1500), Is.EqualTo(1));
        }

        [Test]
        public void AGroupmateComesBeforeANearerStranger()
        {
            Corpse[] corpses = [new(false, false, 100), new(false, true, 1200)];
            Assert.That(BotCorpseTargeting.Choose(corpses, 1500), Is.EqualTo(1));
        }

        [Test]
        public void TheOneNamedWithTargetComesFirst()
        {
            Corpse[] corpses = [new(false, true, 100), new(true, false, 1400), new(false, false, 50)];
            Assert.That(BotCorpseTargeting.Choose(corpses, 1500), Is.EqualTo(1));
        }

        [Test]
        public void ACorpseOutOfRangeIsNeverChosen()
        {
            Corpse[] corpses = [new(true, true, 1501), new(false, false, 1500)];
            Assert.Multiple(() =>
            {
                Assert.That(BotCorpseTargeting.Choose(corpses, 1500), Is.EqualTo(1), "the edge of the range counts");
                Assert.That(BotCorpseTargeting.Choose(corpses, 1000), Is.EqualTo(-1));
                Assert.That(BotCorpseTargeting.Choose(System.Array.Empty<Corpse>(), 1500), Is.EqualTo(-1));
                Assert.That(BotCorpseTargeting.Choose(null, 1500), Is.EqualTo(-1));
            });
        }

        [Test]
        public void NothingToDoWithoutACasterOrARange()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotCorpseTargeting.TryRedirect(null, null, 1500, true, out GameLiving none), Is.False);
                Assert.That(none, Is.Null);
                Assert.That(BotCorpseTargeting.IsRaisable(null, null), Is.False);
                Assert.That(BotCorpseTargeting.FindByName(null, "Anyone", 1024), Is.Null);
            });
        }
    }
}
