using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The next bot tactics for the two bosses the raids never killed in the play tests (owner 2026-11-10: "try the next bot
    /// tactic first", before any boss number changes).
    /// Grand Summoner Govannon: a realm that knows him forms up (ring, taunt, casters' and healers' distance) before he turns
    /// into the demon at 50%, because up to 38 raiders died in the minute after the change (duo22-26).
    /// Spindler Broodmother: her tanks taunt her and she is fought only near her spawn, because she walked 2,000-4,000 after a
    /// healer party and the raid lost her (duo3, duo25).
    /// </summary>
    [TestFixture]
    public class UT_FormUpBeforeTheChange
    {
        private static EpicEncounterTactics.Tactic Row(string boss) => EpicEncounterTactics.Catalog.Single(t => t.Boss == boss);

        [Test]
        public void ARealmThatKnowsHimFormsUpBeforeTheChange()
        {
            var gov = Row("GrandSummonerGovannon");
            Assert.That(gov.SpreadWhileModel, Is.EqualTo(636));
            Assert.That(gov.SpreadFromHealth, Is.InRange(52, 65), "above the change at 50%, below the 80% summons");
            double knows = EpicEncounterTactics.FormUpFamiliarity + 0.01, fresh = 0;
            // The demon: always, as before.
            Assert.That(EpicEncounterTactics.SpreadOn(gov, 636, 100, fresh), Is.True);
            // His first form: only from the form-up health down, and only for a realm that has fought him.
            Assert.That(EpicEncounterTactics.SpreadOn(gov, 0, gov.SpreadFromHealth, knows), Is.True);
            Assert.That(EpicEncounterTactics.SpreadOn(gov, 0, 51, knows), Is.True);
            Assert.That(EpicEncounterTactics.SpreadOn(gov, 0, gov.SpreadFromHealth + 1, knows), Is.False, "not for the whole first form");
            Assert.That(EpicEncounterTactics.SpreadOn(gov, 0, 51, fresh), Is.False, "a realm that never fought him learns it the hard way");
        }

        [Test]
        public void ThreeLostRaidsAreEnoughToKnowHim()
        {
            Assert.That(RaidDiscipline.Familiarity(3, 0), Is.GreaterThanOrEqualTo(EpicEncounterTactics.FormUpFamiliarity));
            Assert.That(RaidDiscipline.Familiarity(2, 0), Is.LessThan(EpicEncounterTactics.FormUpFamiliarity));
            Assert.That(RaidDiscipline.Familiarity(1, 1), Is.GreaterThanOrEqualTo(EpicEncounterTactics.FormUpFamiliarity), "one win too");
        }

        [Test]
        public void ASpreadWithoutAModelOrAFormUpIsUnchanged()
        {
            var lady = Row("BlackLady");
            Assert.That((lady.SpreadRadius, lady.SpreadWhileModel, lady.SpreadFromHealth), Is.EqualTo((350, 0, 0)));
            Assert.That(EpicEncounterTactics.SpreadOn(lady, 123, 100, 0), Is.True, "no model named: always on");
            Assert.That(EpicEncounterTactics.SpreadOn(Row("SpindlerBroodmother"), 0, 10, 1), Is.False, "no spread at all");
            Assert.That(EpicEncounterTactics.Catalog.Count(t => t.SpreadFromHealth > 0), Is.EqualTo(1), "only Govannon forms up early");
        }

        [Test]
        public void SpindlerIsHeldAtHerSpot()
        {
            var spindler = Row("SpindlerBroodmother");
            Assert.That(spindler.TanksTaunt, Is.True, "her tanks taunt her off the healers");
            Assert.That(spindler.SpreadRadius, Is.EqualTo(0), "the taunt needs no spread");
            Assert.That(spindler.FightAtHomeRadius, Is.EqualTo(1000));
            Assert.That(EpicEncounterTactics.OutsideHome(spindler.FightAtHomeRadius, 2_220), Is.True, "duo25: she walked 2,220");
            Assert.That(EpicEncounterTactics.OutsideHome(spindler.FightAtHomeRadius, 900), Is.False);
            // Her newborn parties and the area spells on her spot are unchanged.
            Assert.That((spindler.AddParties, spindler.AreaOnClump), Is.EqualTo((3, true)));
        }
    }
}
