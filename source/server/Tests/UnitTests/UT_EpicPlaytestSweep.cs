using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The play test's sweep (/epictest sweep, owner 2026-11-13): every raid once, Darkness Falls last, and the guest kept
    /// with their party by the server (tag-along) when nobody moves their keys.
    /// </summary>
    [TestFixture]
    public class UT_EpicPlaytestSweep
    {
        [Test]
        public void All_IsEveryRaid_DarknessFallsLast()
        {
            string[] known = AutonomousRealmRaid.Definitions.Select(d => d.Id).ToArray();
            string[] order = EpicPlaytestSweep.Parse("all", known, out string[] unknown);
            Assert.That(unknown, Is.Empty);
            Assert.That(order, Is.EquivalentTo(known));
            Assert.That(order.Length, Is.EqualTo(21));
            Assert.That(order.TakeLast(3), Is.All.StartsWith("darkness-"));
            Assert.That(order.SkipLast(3), Has.None.StartsWith("darkness-"));
            Assert.That(order.SkipLast(3), Is.EqualTo(known.Where(id => !id.StartsWith("darkness-"))), "the definitions' own order otherwise");
        }

        [Test]
        public void AList_KeepsKnownIds_NamesUnknownOnes()
        {
            string[] known = ["dragon-albion", "darkness-midgard", "epic-hibernia"];
            string[] order = EpicPlaytestSweep.Parse("darkness-midgard, dragon-albion,nope,dragon-albion", known, out string[] unknown);
            Assert.That(order, Is.EqualTo(new[] { "dragon-albion", "darkness-midgard" }));
            Assert.That(unknown, Is.EqualTo(new[] { "nope" }));
        }

        [Test]
        public void Anchor_IsTheParty_NotALeaderReleasedAtItsBind()
        {
            var members = new (ushort, int, int, bool)[]
            {
                (1, 450_000, 700_000, true),   // the leader, released at its bind point
                (1, 391_000, 756_000, false),
                (1, 391_500, 755_500, false),
                (1, 392_000, 756_200, false),
            };
            Assert.That(EpicPlaytestSweep.Anchor(members), Is.Not.EqualTo(0));
            members[0] = (1, 391_200, 756_100, true);
            Assert.That(EpicPlaytestSweep.Anchor(members), Is.EqualTo(0), "together, the leader comes first");
        }

        [TestCase(true, true, true, 500f, 60_000L, 0L, null, TestName = "near the party: nothing")]
        [TestCase(true, true, true, 5_000f, 10_000L, 0L, null, TestName = "thrown, not yet 20 s: the mechanic plays out")]
        [TestCase(true, true, true, 5_000f, 20_000L, 0L, "move", TestName = "away 20 s: back to the party")]
        [TestCase(true, true, false, float.MaxValue, 20_000L, 0L, "move", TestName = "other region (a teleport): back")]
        [TestCase(false, true, true, 0f, 0L, 29_000L, null, TestName = "dead under 30 s: left as it is")]
        [TestCase(false, true, true, 0f, 0L, 30_000L, "raise", TestName = "dead 30 s: raised at the party")]
        [TestCase(true, false, false, float.MaxValue, 99_000L, 0L, null, TestName = "not in a party: never moved")]
        public void GuestAction(bool alive, bool inGroup, bool sameRegion, float distance, long away, long dead, string expected) =>
            Assert.That(EpicPlaytestSweep.GuestAction(alive, inGroup, sameRegion, distance, away, dead), Is.EqualTo(expected));

        [Test]
        public void AGuestNearTheBossStaysWhereItIsThoughItsPartyStandsBack() =>
            Assert.Multiple(() =>
            {
                Assert.That(EpicPlaytestSweep.GuestAction(true, true, true, 4_800f, 60_000L, 0L, nearFight: true), Is.Null);
                Assert.That(EpicPlaytestSweep.GuestAction(true, true, true, 4_800f, 60_000L, 0L, nearFight: false), Is.EqualTo("move"));
                Assert.That(EpicPlaytestSweep.GuestAction(false, true, true, 4_800f, 0L, 30_000L, nearFight: true), Is.EqualTo("raise"));
                Assert.That(EpicPlaytestSweep.IsAway(false, float.MaxValue, true), Is.False);
            });
    }
}
