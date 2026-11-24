using System;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// 1.2.3 camp choice and deaths (#535, #536, #537), from the 1.2.2 logs of 11/23 and 11/24: solo camps grey on
    /// arrival, group camps purple to the party, and deaths blamed on the camp instead of the killer.
    /// </summary>
    [TestFixture]
    public class UT_CampChoiceAndDeaths123
    {
        // #535 -----------------------------------------------------------------------------------------------

        [TestCase(4, 1, 10, ExpectedResult = false, TestName = "535 level 4 at skeletal pawns: one green among greys")]
        [TestCase(8, 4, 10, ExpectedResult = false, TestName = "535 less than half qualifies")]
        [TestCase(12, 5, 10, ExpectedResult = true, TestName = "535 half of the camp")]
        [TestCase(12, 2, 3, ExpectedResult = false, TestName = "535 two of three")]
        [TestCase(12, 2, 2, ExpectedResult = true, TestName = "535 a small camp that is all green or better")]
        [TestCase(12, 1, 2, ExpectedResult = false, TestName = "535 half of a two-spawn camp is not enough")]
        [TestCase(50, 2, 2, ExpectedResult = false, TestName = "535 level 50 keeps three spawns")]
        [TestCase(50, 5, 10, ExpectedResult = true, TestName = "535 level 50 half of the camp")]
        public bool SoloCampsNeedHalfTheirSpawnsWorthExperienceAtEveryLevel(int botLevel, int qualifying, int total) =>
            AutonomousSoloCampFloor.CampQualifies(botLevel, qualifying, total);

        [TestCase(4, 1, ExpectedResult = true)]
        [TestCase(4, 2, ExpectedResult = false)]
        [TestCase(8, 5, ExpectedResult = true, TestName = "535 orchard nipper 5 is grey to a level 8")]
        [TestCase(8, 6, ExpectedResult = false)]
        [TestCase(50, 35, ExpectedResult = true)]
        [TestCase(50, 36, ExpectedResult = false)]
        public bool GreyIsTheConTable(int botLevel, int monsterLevel) => AutonomousSoloCampFloor.IsGrey(botLevel, monsterLevel);

        // #536 -----------------------------------------------------------------------------------------------

        [Test]
        public void AnEvenLevel12PartyHuntsYellowToOrange()
        {
            var band = AutonomousGroupCampBand.For(12, 12, 12, 6, 5, 0);
            Assert.Multiple(() =>
            {
                Assert.That(band.Minimum, Is.EqualTo(11), "yellow to 12");
                Assert.That(band.Maximum, Is.EqualTo(14), "orange to 12, below the raw +6");
                Assert.That(band.Allows(15), Is.False, "red to 12 (+3 was the old floor)");
                Assert.That(band.Allows(17), Is.False, "purple to 12");
                Assert.That(AutonomousGroupCampBand.SelectLevel(new[] { 10, 13, 14, 15, 17 }, band), Is.EqualTo(14));
            });
        }

        [Test]
        public void ASpreadPartyIsSetByItsLowestMember()
        {
            // Grimsoul, level 9 in a 5-man averaging 12, died to a level 19 morvalt krigsman (purple)
            var band = AutonomousGroupCampBand.For(9, 14, 12, 6, 5, 0);
            Assert.Multiple(() =>
            {
                Assert.That(band.Minimum, Is.EqualTo(9));
                Assert.That(band.Maximum, Is.EqualTo(10), "orange to the level 9");
                Assert.That(band.Allows(9), Is.True, "green, not grey, to the level 14");
                Assert.That(band.Allows(19), Is.False);
                Assert.That(band.Allows(15), Is.False, "the old +3 floor over the average");
            });
        }

        [Test]
        public void APartyTooSpreadForAnyCampFindsNone()
        {
            // yellow-orange to a level 6 is 6-7, grey to a level 14
            var band = AutonomousGroupCampBand.For(6, 14, 10, 6, 5, 0);
            Assert.That(AutonomousGroupCampBand.SelectLevel(new[] { 6, 7, 9, 12 }, band), Is.Zero);
        }

        [Test]
        public void OnlyAFullEightTakesRedAndLevel50KeepsItsTop()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousGroupCampBand.For(50, 50, 50, 10, 8, 0).Maximum, Is.EqualTo(60), "red 56-60 for eight at 50, as +10");
                Assert.That(AutonomousGroupCampBand.For(50, 50, 50, 6, 6, 0).Maximum, Is.EqualTo(55), "orange 51-55 for six");
                Assert.That(AutonomousGroupCampBand.For(12, 12, 12, 10, 8, 0).Maximum, Is.EqualTo(16), "red to 12 for eight");
                Assert.That(AutonomousGroupCampBand.For(12, 12, 12, 10, 7, 0).Maximum, Is.EqualTo(14), "orange for seven");
            });
        }

        [Test]
        public void AWipeLowersTheBandOneStep()
        {
            var band = AutonomousGroupCampBand.For(12, 12, 12, 2, 5, 1);
            Assert.Multiple(() =>
            {
                Assert.That(band.Maximum, Is.EqualTo(12), "yellow to 12");
                Assert.That(band.Minimum, Is.EqualTo(9), "blue to 12");
                Assert.That(AutonomousGroupCampBand.For(12, 12, 12, -4, 5, 3).Maximum, Is.GreaterThanOrEqualTo(9),
                    "a raw bonus below zero never empties the band");
            });
        }

        // #537 -----------------------------------------------------------------------------------------------

        [Test]
        public void DangerSpotsAreMarkedByConAndKeepTheKiller()
        {
            AutonomousPveBossDanger.Clear();
            var now = new DateTime(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
            try
            {
                Assert.Multiple(() =>
                {
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 26, 14), Is.True, "howling maiden 26 on a level 14");
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 24, 17), Is.True, "cornish hen 24 on a level 17 (purple, +7)");
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 15, 12), Is.True, "red to a bot alone");
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 15, 12, victimAlone: false), Is.False, "red camp a party chose");
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 17, 12, victimAlone: false), Is.True, "purple to a party member");
                    Assert.That(AutonomousPveBossDanger.Marks("monster", 13, 12), Is.False, "orange");
                    Assert.That(AutonomousPveBossDanger.Marks("player", 50, 12), Is.False, "RvR is not a PvE danger");
                    Assert.That(AutonomousPveBossDanger.Warns(24, 14), Is.True);
                    Assert.That(AutonomousPveBossDanger.Warns(24, 24), Is.False, "an ordinary camp at its own level");

                    Assert.That(AutonomousPveBossDanger.Record(200, 514000, 380000, 26, now, "howling maiden"), Is.True, "a new spot");
                    Assert.That(AutonomousPveBossDanger.Record(200, 514600, 380400, 24, now.AddMinutes(5), "howling maiden"), Is.False,
                        "the same place refreshes the spot");
                    Assert.That(AutonomousPveBossDanger.Count, Is.EqualTo(1));
                    Assert.That(AutonomousPveBossDanger.IsNear(200, 514000, 382000, 12, now.AddHours(6)), Is.True,
                        "refreshed: still remembered six hours after the first death");
                    Assert.That(AutonomousPveBossDanger.IsNear(200, 514000, 380000, 20, now), Is.True, "24 is still red to a level 20");
                    Assert.That(AutonomousPveBossDanger.Record(200, 530000, 380000, 26, now, "howling maiden"), Is.True, "another place");
                    Assert.That(AutonomousPveBossDanger.Count, Is.EqualTo(2));
                });
            }
            finally { AutonomousPveBossDanger.Clear(); }
        }

        [Test]
        public void ADeathOnTheWayBlamesTheKillerNotTheCamp()
        {
            var maiden = new AutonomousDeathBlame.Killer("howling maiden", 25, "monster", 200, 514000, 380000);
            var nipper = new AutonomousDeathBlame.Killer("Orchard Nipper", 9, "monster", 200, 1, 1);
            var add = new AutonomousDeathBlame.Killer("hill toad", 12, "monster", 200, 1, 1);
            var player = new AutonomousDeathBlame.Killer("Someone", 50, "player", 200, 1, 1);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousDeathBlame.IsDangerOnTheWay(maiden, "orchard nipper", 12), Is.True);
                Assert.That(AutonomousDeathBlame.IsDangerOnTheWay(maiden, null, 12), Is.True, "while searching for a camp");
                Assert.That(AutonomousDeathBlame.SpeciesToBench(maiden, 200, "orchard nipper"), Is.Null,
                    "the camp's monster is not benched for the maiden's kill");
                Assert.That(AutonomousDeathBlame.BlamesCampMonster(maiden, "orchard nipper"), Is.False);

                Assert.That(AutonomousDeathBlame.SpeciesToBench(nipper, 200, "orchard nipper"),
                    Is.EqualTo(AutonomousCampDangerBench.SpeciesKey(200, "orchard nipper")), "the camp's own monster");
                Assert.That(AutonomousDeathBlame.IsDangerOnTheWay(nipper, "orchard nipper", 8), Is.False, "its own camp target");
                Assert.That(AutonomousDeathBlame.SpeciesToBench(null, 200, "orchard nipper"),
                    Is.EqualTo(AutonomousCampDangerBench.SpeciesKey(200, "orchard nipper")), "an unknown killer: as before");

                Assert.That(AutonomousDeathBlame.IsDangerOnTheWay(add, "orchard nipper", 12), Is.False, "a yellow add");
                Assert.That(AutonomousDeathBlame.SpeciesToBench(add, 200, "orchard nipper"), Is.Null);
                Assert.That(AutonomousDeathBlame.IsDangerOnTheWay(player, "orchard nipper", 12), Is.False);
                Assert.That(AutonomousDeathBlame.SpeciesToBench(player, 200, "orchard nipper"), Is.Null);
            });
        }
    }
}
