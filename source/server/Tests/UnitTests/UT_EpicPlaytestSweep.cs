using System.Linq;
using System.Numerics;
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
            Assert.That(order.Length, Is.EqualTo(37), "21 raids, and the 16 open-world big bosses since 1.2");
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

        [TestCase("no_party", true)]
        [TestCase("no_seat", true)]
        [TestCase("raid_started", false)]
        [TestCase("raid_ended", false)]
        [TestCase("grouped", false)]
        public void ASeatIsTriedAgainOnlyWhileItMayStillComeFree(string why, bool expected) =>
            Assert.That(EpicPlaytestSweep.SeatWorthRetrying(why), Is.EqualTo(expected));

        [Test]
        public void AGuestNearTheBossStaysWhereItIsThoughItsPartyStandsBack() =>
            Assert.Multiple(() =>
            {
                Assert.That(EpicPlaytestSweep.GuestAction(true, true, true, 4_800f, 60_000L, 0L, nearFight: true), Is.Null);
                Assert.That(EpicPlaytestSweep.GuestAction(true, true, true, 4_800f, 60_000L, 0L, nearFight: false), Is.EqualTo("move"));
                Assert.That(EpicPlaytestSweep.GuestAction(false, true, true, 4_800f, 0L, 30_000L, nearFight: true), Is.EqualTo("raise"));
                Assert.That(EpicPlaytestSweep.IsAway(false, float.MaxValue, true), Is.False);
            });

        // (Region, X, Y, Alive, RaidLeader, OwnParty); the front is inside the dungeon (region 248) at 30,000 / 30,000.
        private static int GuestAnchor(params (ushort, int, int, bool, bool, bool)[] raiders) =>
            EpicPlaytestSweep.GuestAnchor(raiders, 248, 30_000, 30_000);

        [Test]
        public void TheGuestNeverFollowsARaiderReleasedToTheHub() =>
            Assert.Multiple(() =>
            {
                // summoners-albion, 2026-11-14: Ravooooora (own party) died, released to the hub in region 1; the GM followed.
                Assert.That(GuestAnchor(
                    (1, 560_000, 510_000, true, false, true),
                    (248, 30_400, 29_800, true, false, false),
                    (248, 31_000, 30_500, true, false, false)), Is.EqualTo(1));
                Assert.That(GuestAnchor((1, 560_000, 510_000, true, true, true)), Is.EqualTo(-1),
                    "only a raider at the hub, even the raid leader: the guest stays where it is");
            });

        [Test]
        public void TheGuestNeverFollowsADeadRaider() =>
            Assert.Multiple(() =>
            {
                Assert.That(GuestAnchor(
                    (248, 30_000, 30_000, false, true, true),
                    (248, 31_500, 30_200, true, false, false)), Is.EqualTo(1));
                Assert.That(GuestAnchor((248, 30_000, 30_000, false, true, true)), Is.EqualTo(-1));
            });

        [Test]
        public void AtTheFront_TheRaidLeaderFirst_ThenTheGuestsOwnParty() =>
            Assert.Multiple(() =>
            {
                Assert.That(GuestAnchor(
                    (248, 30_100, 30_100, true, false, true),
                    (248, 31_000, 31_000, true, true, false),
                    (248, 30_200, 30_000, true, false, false)), Is.EqualTo(1), "the raid leader at the front");
                Assert.That(GuestAnchor(
                    (248, 30_100, 30_100, true, false, false),
                    (248, 31_000, 31_000, true, false, true),
                    (248, 40_000, 40_000, true, true, false)), Is.EqualTo(1), "a raid leader away from the front is passed over");
            });

        [Test]
        public void NobodyAtTheFront_TheHeadOfTheMarch() =>
            Assert.That(GuestAnchor(
                (248, 50_000, 50_000, true, true, true),
                (248, 38_000, 36_000, true, false, false),
                (1, 30_000, 30_000, true, false, false)), Is.EqualTo(1));

        [Test]
        public void TheGuestLandsOnTheFloorBesideItsAnchor_OrOnTheAnchor() =>
            Assert.Multiple(() =>
            {
                var anchor = new Vector3(30_000, 30_000, 1_200);
                var beside = new Vector3(30_038, 30_041, 1_188);
                Assert.That(EpicPlaytestSweep.Landing(anchor, true, beside, true), Is.EqualTo(beside), "snapped to the anchor's floor");
                Assert.That(EpicPlaytestSweep.Landing(anchor, false, new Vector3(30_040, 30_040, 1_200), false), Is.EqualTo(anchor),
                    "no mesh under the point beside it: the anchor's exact spot, not the void");
                Assert.That(EpicPlaytestSweep.Landing(anchor, true, beside, false), Is.EqualTo(anchor), "floor the anchor can't walk to");
                Assert.That(EpicPlaytestSweep.Landing(anchor, true, new Vector3(30_040, 30_040, 1_080), true), Is.EqualTo(anchor),
                    "the floor below is another level");
            });
    }
}
