using NUnit.Framework;
using GuildKeeps = DOL.GS.AutonomousBotGuildKeeps;

namespace DOL.GS.Tests
{
    [TestFixture]
    public class UT_AutonomousBotGuildKeeps
    {
        private static GuildKeeps.ClaimCheck Ready() => new(SameRealm: true, AlreadyClaimed: false, InCombat: false, ClaimableType: true,
            PlayerPresent: false, GuildClaims: 0, ClaimLimit: 1, MembersPresent: 8, Needed: 8);

        [Test]
        public void CanClaim_WhenEveryRuleHolds()
        {
            Assert.That(GuildKeeps.CanClaim(Ready()), Is.True);
        }

        [Test]
        public void CanClaim_EachRuleBlocks()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GuildKeeps.CanClaim(Ready() with { SameRealm = false }), Is.False, "other realm's keep");
                Assert.That(GuildKeeps.CanClaim(Ready() with { AlreadyClaimed = true }), Is.False, "already claimed");
                Assert.That(GuildKeeps.CanClaim(Ready() with { InCombat = true }), Is.False, "under attack");
                Assert.That(GuildKeeps.CanClaim(Ready() with { ClaimableType = false }), Is.False, "relic, portal or battleground keep");
                Assert.That(GuildKeeps.CanClaim(Ready() with { PlayerPresent = true }), Is.False, "a real player gets the first chance");
                Assert.That(GuildKeeps.CanClaim(Ready() with { GuildClaims = 1 }), Is.False, "guild at the claim limit");
                Assert.That(GuildKeeps.CanClaim(Ready() with { ClaimLimit = 0 }), Is.False, "claiming disabled");
                Assert.That(GuildKeeps.CanClaim(Ready() with { MembersPresent = 7 }), Is.False, "not enough members inside");
            });
            Assert.That(GuildKeeps.CanClaim(Ready() with { GuildClaims = 2, ClaimLimit = 3 }), Is.True);
        }

        [TestCase(8, false, 8)]
        [TestCase(8, true, 4)]
        [TestCase(1, false, 2)]
        [TestCase(0, true, 2)]
        public void NeededMembers_HalfForTowers_NeverBelowTwo(int claimNum, bool tower, int expected)
        {
            Assert.That(GuildKeeps.NeededMembers(claimNum, tower), Is.EqualTo(expected));
        }

        [Test]
        public void ChooseClaimant_MostPresentThenRvrThenId()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GuildKeeps.ChooseClaimant([new("b", BotGuildCharter.Social, 5), new("a", BotGuildCharter.Rvr, 4)]), Is.EqualTo("b"));
                Assert.That(GuildKeeps.ChooseClaimant([new("b", BotGuildCharter.Social, 4), new("c", BotGuildCharter.Rvr, 4)]), Is.EqualTo("c"));
                Assert.That(GuildKeeps.ChooseClaimant([new("b", BotGuildCharter.Leveling, 4), new("a", BotGuildCharter.Social, 4)]), Is.EqualTo("a"));
                Assert.That(GuildKeeps.ChooseClaimant([new("a", BotGuildCharter.Rvr, 0)]), Is.Null);
                Assert.That(GuildKeeps.ChooseClaimant([]), Is.Null);
                Assert.That(GuildKeeps.ChooseClaimant(null), Is.Null);
            });
        }

        [Test]
        public void ClaimDefenseRank_HalfTheForceFromTheClaimingGuild()
        {
            Assert.Multiple(() =>
            {
                Assert.That(GuildKeeps.ClaimDefenseRank(true, "g", ["g", "g", "x", null]), Is.Zero);
                Assert.That(GuildKeeps.ClaimDefenseRank(true, "g", ["g", "x", "x", null]), Is.EqualTo(1));
                Assert.That(GuildKeeps.ClaimDefenseRank(true, null, ["g", "g"]), Is.EqualTo(1), "unclaimed keep");
                Assert.That(GuildKeeps.ClaimDefenseRank(false, "g", ["g", "g"]), Is.EqualTo(1), "off keeps today's order");
                Assert.That(GuildKeeps.ClaimDefenseRank(true, "g", []), Is.EqualTo(1));
            });
        }
    }
}
