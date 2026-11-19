using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.BotLedGroups;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot-led groups (docs/BOT-LED-GROUPS.md, B1): when the leader of a gamebot party may invite the real player. Every
    /// "never" state, the level band, the place and duty of the party, the cadence (10-minute start, 45-minute gap, the
    /// shared tell budget, the /lfg fast path), the decline memory (30 minutes, 6 hours per leader, three a session) and
    /// the box already waiting.
    /// </summary>
    [TestFixture]
    public class UT_BotLedInvitePolicy
    {
        private const long Minute = 60_000;
        private static readonly PlayerState Free = new(Alive: true, InCombat: false, Riding: false, OnBoat: false, DialogOpen: false,
            Trading: false, Grouped: false, HasCompanions: false, Afk: false, InFrontier: false, InBattleground: false, InRaid: false,
            Linkdead: false);

        [Test]
        public void AFreePlayerMayBeInvited() => Assert.That(StateBlocks(Free), Is.Null);

        [Test]
        public void EveryNeverStateBlocksTheInvite()
        {
            var states = new Dictionary<string, PlayerState>
            {
                ["dead"] = Free with { Alive = false },
                ["in_combat"] = Free with { InCombat = true },
                ["riding"] = Free with { Riding = true },
                ["on_boat"] = Free with { OnBoat = true },
                ["dialog_open"] = Free with { DialogOpen = true },
                ["trading"] = Free with { Trading = true },
                ["grouped"] = Free with { Grouped = true },
                ["companions_out"] = Free with { HasCompanions = true },
                ["afk"] = Free with { Afk = true },
                ["in_frontier"] = Free with { InFrontier = true },
                ["in_battleground"] = Free with { InBattleground = true },
                ["in_raid"] = Free with { InRaid = true },
                ["linkdead"] = Free with { Linkdead = true },
            };
            Assert.Multiple(() =>
            {
                foreach ((string reason, PlayerState state) in states)
                    Assert.That(StateBlocks(state), Is.EqualTo(reason), reason);
            });
        }

        [TestCase(20, 20, true)]
        [TestCase(20, 23, true)]
        [TestCase(20, 17, true)]
        [TestCase(20, 24, false)]
        [TestCase(20, 16, false)]
        public void TheGroupIsWithinThreeLevelsOnAverage(int player, int group, bool fits) =>
            Assert.That(InLevelBand(player, group), Is.EqualTo(fits));

        private static Candidate Party(long id = 1, int level = 20, int members = 6, bool healer = true, double distance = 1000) =>
            new(id, "Leader" + id, eRealm.Albion, level, members, 8, healer, Hunting: true, OnRealmDuty: false, Reachable: true,
                HasPlayer: false, Distance: distance);

        [Test]
        public void APartyNeedsTheRealmRoomTheBandAPlaceOnFootAndNoDuty()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CandidateBlocks(Party(), eRealm.Albion, 20), Is.Null);
                Assert.That(CandidateBlocks(Party(), eRealm.Midgard, 20), Is.EqualTo("other_realm"));
                Assert.That(CandidateBlocks(Party(members: 8), eRealm.Albion, 20), Is.EqualTo("full"));
                Assert.That(CandidateBlocks(Party(level: 25), eRealm.Albion, 20), Is.EqualTo("level_band"));
                Assert.That(CandidateBlocks(Party() with { Reachable = false }, eRealm.Albion, 20), Is.EqualTo("unreachable"));
                Assert.That(CandidateBlocks(Party() with { OnRealmDuty = true }, eRealm.Albion, 20), Is.EqualTo("on_duty"));
                Assert.That(CandidateBlocks(Party() with { Hunting = false }, eRealm.Albion, 20), Is.EqualTo("not_hunting"));
                Assert.That(CandidateBlocks(Party() with { HasPlayer = true }, eRealm.Albion, 20), Is.EqualTo("has_player"));
            });
        }

        [Test]
        public void TheClosestLevelAsksFirstAndAPartyShortOfAHealerAsksAHealerFirst()
        {
            Candidate near = Party(1, level: 22), exact = Party(2, level: 20), noHealer = Party(3, level: 21, healer: false);
            Assert.Multiple(() =>
            {
                Assert.That(PickInvitingLeader([near, exact], eRealm.Albion, 20, false, null, 0)?.LeaderId, Is.EqualTo(2));
                Assert.That(PickInvitingLeader([near, exact, noHealer], eRealm.Albion, 20, playerIsHealer: true, null, 0)?.LeaderId, Is.EqualTo(3));
                Assert.That(PickInvitingLeader([near, noHealer], eRealm.Albion, 20, playerIsHealer: false, null, 0)?.LeaderId, Is.EqualTo(1));
                Assert.That(PickInvitingLeader([Party(members: 8)], eRealm.Albion, 20, false, null, 0), Is.Null);
            });
        }

        [Test]
        public void AtTheSameLevelTheNearerLeaderAsks() =>
            Assert.That(PickInvitingLeader([Party(1, distance: 40_000), Party(2, distance: 2_000)], eRealm.Albion, 20, false, null, 0)?.LeaderId,
                Is.EqualTo(2));

        private static InviteMemory OnlineFor(long minutes, long now) => new(now - minutes * Minute);

        [Test]
        public void NoUnaskedInviteInTheFirstTenMinutesOnline()
        {
            long now = 100 * Minute;
            Assert.Multiple(() =>
            {
                Assert.That(CadenceBlocks(OnlineFor(9, now), now, true, long.MaxValue), Is.EqualTo("just_logged_in"));
                Assert.That(CadenceBlocks(OnlineFor(10, now), now, true, long.MaxValue), Is.Null);
            });
        }

        [Test]
        public void UnaskedInvitesComeAtMostEvery45Minutes()
        {
            long now = 500 * Minute;
            InviteMemory memory = OnlineFor(200, now);
            memory.LastUnaskedAt = now - 44 * Minute;
            Assert.That(CadenceBlocks(memory, now, true, long.MaxValue), Is.EqualTo("invited_recently"));
            memory.LastUnaskedAt = now - 45 * Minute;
            Assert.That(CadenceBlocks(memory, now, true, long.MaxValue), Is.Null);
        }

        [Test]
        public void AnUnaskedInviteSharesTheBotTellBudget()
        {
            long now = 500 * Minute;
            InviteMemory memory = OnlineFor(200, now);
            Assert.Multiple(() =>
            {
                Assert.That(CadenceBlocks(memory, now, true, AutonomousLivingServer.TellGapMinMs - 1), Is.EqualTo("bot_told_recently"));
                Assert.That(CadenceBlocks(memory, now, true, AutonomousLivingServer.TellGapMinMs), Is.Null);
            });
        }

        [Test]
        public void AskingGetsAnInviteWithin15SecondsEvenWithInvitesOffOrJustLoggedIn()
        {
            long now = 500 * Minute;
            InviteMemory memory = OnlineFor(1, now);
            memory.LastUnaskedAt = now - Minute;
            memory.Declined(7, now - Minute);
            memory.Declined(8, now - Minute);
            memory.Declined(9, now - Minute);
            memory.Asked(now - 5_000);
            Assert.Multiple(() =>
            {
                Assert.That(memory.IsAsking(now), Is.True);
                Assert.That(CadenceBlocks(memory, now, invitesOn: false, 0), Is.Null,
                    "asked: no 10-minute start, 45-minute gap, 30-minute quiet or three-noes rule");
                Assert.That(CadenceBlocks(memory, now - 5_000 + AskedLatestStartMs + 1, invitesOn: false, 0), Is.EqualTo("invites_off"),
                    "too late for a box within 15 seconds: back to the unasked rules");
            });
        }

        [Test]
        public void AnAskedInvitesAnswerAndBoxComeWithin15Seconds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AskedWindowMs, Is.EqualTo(15_000));
                Assert.That(AskedTellDelayMinMs, Is.GreaterThan(0), "the leader answers first");
                Assert.That(AskedBoxDelayMinMs, Is.GreaterThan(0), "then the box");
                Assert.That(AskedLatestStartMs + AskedLongestDelayMs, Is.LessThanOrEqualTo(AskedWindowMs));
                Assert.That(AskedLatestStartMs, Is.GreaterThanOrEqualTo(BotLedGroups.PulseIntervalMs), "the pulse gets one more try");
            });
        }

        [Test]
        public void UnaskedInvitesKeepEveryRule()
        {
            long now = 500 * Minute;
            InviteMemory memory = OnlineFor(200, now);
            memory.Asked(now - 20_000);
            Assert.That(memory.IsAsking(now), Is.False);
            memory.Declined(7, now - Minute);
            Assert.That(CadenceBlocks(memory, now, true, long.MaxValue), Is.EqualTo("declined_recently"));
        }

        [Test]
        public void ABoxAlreadyWaitingStopsEveryInvite()
        {
            long now = 500 * Minute;
            InviteMemory memory = OnlineFor(200, now);
            memory.Asked(now);
            memory.Pending = true;
            Assert.That(CadenceBlocks(memory, now, true, long.MaxValue), Is.EqualTo("box_waiting"));
        }

        [Test]
        public void ANoQuietsUnaskedInvitesFor30Minutes()
        {
            long now = 500 * Minute;
            InviteMemory memory = OnlineFor(200, now);
            memory.Declined(7, now - 29 * Minute);
            Assert.That(CadenceBlocks(memory, now, true, long.MaxValue), Is.EqualTo("declined_recently"));
            Assert.That(CadenceBlocks(memory, now + Minute, true, long.MaxValue), Is.Null);
        }

        [Test]
        public void ThreeNoesEndUnaskedInvitesForTheSession()
        {
            long now = 900 * Minute;
            InviteMemory memory = OnlineFor(800, now);
            memory.Declined(1, now - 300 * Minute);
            memory.Declined(2, now - 200 * Minute);
            memory.Declined(3, now - 100 * Minute);
            Assert.That(CadenceBlocks(memory, now, true, long.MaxValue), Is.EqualTo("declined_three_times"));
            memory.Asked(now);
            Assert.That(CadenceBlocks(memory, now, true, long.MaxValue), Is.Null, "asking still works");
        }

        [Test]
        public void ALeaderThePlayerTurnedDownDoesNotAskFor6Hours()
        {
            long now = 900 * Minute;
            InviteMemory memory = OnlineFor(800, now);
            memory.Declined(2, now - 359 * Minute);
            Candidate first = Party(2, level: 20), second = Party(5, level: 22);
            Assert.Multiple(() =>
            {
                Assert.That(memory.LeaderDeclined(2, now), Is.True);
                Assert.That(PickInvitingLeader([first, second], eRealm.Albion, 20, false, memory, now)?.LeaderId, Is.EqualTo(5));
                Assert.That(PickInvitingLeader([first, second], eRealm.Albion, 20, false, memory, now + Minute)?.LeaderId, Is.EqualTo(2));
            });
        }

        [Test]
        public void ADeclineEndsTheWaitingBox()
        {
            var memory = new InviteMemory(0) { Pending = true };
            memory.Declined(1, 10);
            Assert.Multiple(() =>
            {
                Assert.That(memory.Pending, Is.False);
                Assert.That(memory.SessionDeclines, Is.EqualTo(1));
            });
        }
    }
}
