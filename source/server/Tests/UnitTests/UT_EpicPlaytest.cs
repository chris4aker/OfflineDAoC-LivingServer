using System.Linq;
using System.Numerics;
using DOL.GS;
using DOL.GS.Commands;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>The play test's raid starter (/epictest) and the console's command client (1.2 play test).</summary>
    [TestFixture]
    public class UT_EpicPlaytest
    {
        [TestCase(null, 100, 100)]
        [TestCase("era", 100, 100)]
        [TestCase("start", 100, 125)]
        [TestCase("start", 70, 88)]
        [TestCase("64", 100, 64)]
        [TestCase("4", 100, 0)]
        [TestCase("lots", 100, 0)]
        [TestCase("999", 100, 300)]
        public void SizeIsEraStartOrANumber(string word, int period, int expected) =>
            Assert.That(EpicPlaytestCommandHandler.SizeFor(word, period), Is.EqualTo(expected));

        [TestCase(100, 80)]
        [TestCase(64, 52)]
        [TestCase(70, 56)]
        [TestCase(8, 8)]
        public void APlaytestRaidGoesInWithEightyPercentStaged(int size, int required) =>
            Assert.That(AutonomousRealmRaid.PlaytestRequired(size), Is.EqualTo(required));

        [Test]
        public void HubSpotsAreDistinctAndNearTheHub()
        {
            var center = new Vector3(426905, 416817, 5712);
            Vector3[] spots = Enumerable.Range(0, 130).Select(i => EpicPlaytestCommandHandler.HubSpot(center, i, 130)).ToArray();
            Assert.That(spots.Distinct().Count(), Is.EqualTo(130));
            Assert.That(spots.All(s => Vector2.Distance(new(s.X, s.Y), new(center.X, center.Y)) is >= 59 and <= 241), Is.True);
        }

        [Test]
        public void FocusMatchesNameOrClassName()
        {
            string[] focus = ["Funus", "GrandSummonerGovannon"];
            Assert.That(RealmRaidDungeonRoute.InFocus(focus, "Funus", "Funus"), Is.True);
            Assert.That(RealmRaidDungeonRoute.InFocus(focus, "Grand Summoner Govannon", "GrandSummonerGovannon"), Is.True);
            Assert.That(RealmRaidDungeonRoute.InFocus(focus, "Fames", "Fames"), Is.False);
            Assert.That(RealmRaidDungeonRoute.InFocus(null, "Funus", "Funus"), Is.False, "no focus: every real raid");
        }

        [Test]
        public void TheConsoleRunsCommandsAsAnAdminWithoutAPlayer()
        {
            GameClient console = ConsoleCommandClient.Create();
            Assert.That(console.Account, Is.Not.Null);
            Assert.That(console.Account.PrivLevel, Is.EqualTo((uint)ePrivLevel.Admin));
            Assert.That(console.Account.Name, Is.EqualTo(ConsoleCommandClient.AccountName));
            Assert.That(console.Player, Is.Null);
        }
    }
}
