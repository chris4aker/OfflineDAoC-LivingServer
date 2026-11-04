using System.Linq;
using DOL.GS;
using NUnit.Framework;
using static DOL.GS.BotDutyLeaseRules;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;

namespace DOL.UnitTests
{
    /// <summary>The duty lease's pure rules (docs/DUTY-LEASE.md): priorities, who may take a bot from whom, endings.</summary>
    [TestFixture]
    public class UT_BotDutyLeaseRules
    {
        private static BotDutyLease Lease(Kind kind, string owner = "a") => new(kind, owner, owner, Priority(kind), 0, "test");

        private static readonly Kind[] Automatic = [Kind.BotGroupSession, Kind.BattlegroundWave, Kind.RvrEvent, Kind.RealmRaid];

        [Test]
        public void APlayersGroupOutranksEverythingAndARaidOutranksTheFrontier()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Priority(Kind.PlayerGroup), Is.GreaterThan(Priority(Kind.BotLedGroupWithPlayer)));
                Assert.That(Priority(Kind.BotLedGroupWithPlayer), Is.GreaterThan(Priority(Kind.RealmRaid)));
                Assert.That(Priority(Kind.RealmRaid), Is.GreaterThan(Priority(Kind.RvrEvent)));
                Assert.That(Priority(Kind.RvrEvent), Is.EqualTo(Priority(Kind.BattlegroundWave)), "owner 2026-11-04: equal");
                Assert.That(Priority(Kind.BattlegroundWave), Is.GreaterThan(Priority(Kind.BotGroupSession)));
                Assert.That(Priority(Kind.None), Is.Zero);
            });
        }

        [Test]
        public void AFreeBotIsGrantedToAnyClaimAndTheSameClaimIsRenewed()
        {
            foreach (Kind kind in Automatic.Append(Kind.PlayerGroup).Append(Kind.BotLedGroupWithPlayer))
            {
                Assert.That(Decide(null, kind, "x").Outcome, Is.EqualTo(Outcome.Granted), kind.ToString());
                Assert.That(Decide(Lease(kind, "x"), kind, "x").Outcome, Is.EqualTo(Outcome.Renewed), kind.ToString());
            }
            Assert.That(Decide(null, Kind.None, "x").Holds, Is.False, "nothing is not a claim");
        }

        [Test]
        public void APlayersGroupIsNeverTakenByAnythingAutomatic()
        {
            foreach (Kind kind in Automatic)
            {
                Verdict verdict = Decide(Lease(Kind.PlayerGroup, "Chris"), kind, "x");
                Assert.That(verdict.Outcome, Is.EqualTo(Outcome.Refused), kind.ToString());
                Assert.That(verdict.Reason, Is.EqualTo(DutyLeaseReason.InPlayersGroup));
            }
            Assert.That(Decide(Lease(Kind.PlayerGroup, "Chris"), Kind.BotLedGroupWithPlayer, "x").Outcome, Is.EqualTo(Outcome.Refused));
        }

        [Test]
        public void ABotLedGroupHostingAPlayerIsNeverTakenEither()
        {
            foreach (Kind kind in Automatic)
            {
                Verdict verdict = Decide(Lease(Kind.BotLedGroupWithPlayer, "g-1"), kind, "x");
                Assert.That(verdict.Outcome, Is.EqualTo(Outcome.Refused), kind.ToString());
                Assert.That(verdict.Reason, Is.EqualTo(DutyLeaseReason.HostingPlayer));
            }
            Assert.That(Decide(Lease(Kind.BotLedGroupWithPlayer, "g-1"), Kind.BotLedGroupWithPlayer, "g-2").Outcome, Is.EqualTo(Outcome.Refused),
                "one hosting group at a time");
        }

        [Test]
        public void APlayersGroupAlwaysGetsTheBotBecauseTheInviteCheckedItsDutyFirst()
        {
            foreach (Kind kind in Automatic.Append(Kind.BotLedGroupWithPlayer))
                Assert.That(Decide(Lease(kind), Kind.PlayerGroup, "Chris").Outcome, Is.EqualTo(Outcome.Taken), kind.ToString());
            Verdict handover = Decide(Lease(Kind.PlayerGroup, "Chris"), Kind.PlayerGroup, "Alys");
            Assert.That(handover.Outcome, Is.EqualTo(Outcome.Taken));
            Assert.That(handover.Reason, Is.EqualTo(DutyLeaseReason.Moved), "the leader left and the bot stays with the other player");
        }

        [Test]
        public void ABotLedGroupTakesInAPlayerOnlyFromItsOwnBotGroup()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Decide(Lease(Kind.BotGroupSession), Kind.BotLedGroupWithPlayer, "g").Outcome, Is.EqualTo(Outcome.Taken));
                Assert.That(Decide(Lease(Kind.RealmRaid), Kind.BotLedGroupWithPlayer, "g").Reason, Is.EqualTo(DutyLeaseReason.OnRealmRaid));
                Assert.That(Decide(Lease(Kind.RvrEvent), Kind.BotLedGroupWithPlayer, "g").Reason, Is.EqualTo(DutyLeaseReason.OnRvrEvent));
                Assert.That(Decide(Lease(Kind.BattlegroundWave), Kind.BotLedGroupWithPlayer, "g").Reason, Is.EqualTo(DutyLeaseReason.OnBattleground));
            });
        }

        [Test]
        public void AmongAutomaticClaimsTheHigherTakesTheLowerAndTheLowerIsRefused()
        {
            foreach (Kind held in Automatic)
            foreach (Kind incoming in Automatic)
            {
                Verdict verdict = Decide(Lease(held, "old"), incoming, "new");
                if (Priority(incoming) > Priority(held) || incoming == held)
                    Assert.That(verdict.Outcome, Is.EqualTo(Outcome.Taken), $"{incoming} over {held}");
                else
                {
                    Assert.That(verdict.Outcome, Is.EqualTo(Outcome.Refused), $"{incoming} over {held}");
                    Assert.That(verdict.Reason, Is.EqualTo(DutyLeaseReason.HeldBy(held)));
                }
            }
            Assert.That(Decide(Lease(Kind.RealmRaid, "dragon"), Kind.RealmRaid, "galladoria").Reason, Is.EqualTo(DutyLeaseReason.Moved),
                "a newer raid reservation moves the bot, as the reservation map did");
        }

        [Test]
        public void AnRvrMarchAndABattlegroundRallyNeverTakeABotFromEachOther()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Decide(Lease(Kind.BattlegroundWave, "bg"), Kind.RvrEvent, "force").Outcome, Is.EqualTo(Outcome.Refused));
                Assert.That(Decide(Lease(Kind.BattlegroundWave, "bg"), Kind.RvrEvent, "force").Reason, Is.EqualTo(DutyLeaseReason.OnBattleground));
                Assert.That(Decide(Lease(Kind.RvrEvent, "force"), Kind.BattlegroundWave, "bg").Outcome, Is.EqualTo(Outcome.Refused));
                Assert.That(Decide(Lease(Kind.RvrEvent, "force"), Kind.BattlegroundWave, "bg").Reason, Is.EqualTo(DutyLeaseReason.OnRvrEvent));
                Assert.That(Decide(Lease(Kind.RvrEvent, "a"), Kind.RvrEvent, "b").Outcome, Is.EqualTo(Outcome.Taken), "one force to another, as before");
                Assert.That(Decide(Lease(Kind.BattlegroundWave, "bg"), Kind.RealmRaid, "dragon").Outcome, Is.EqualTo(Outcome.Taken), "a raid still takes either");
                Assert.That(Decide(Lease(Kind.BotGroupSession, "g"), Kind.BattlegroundWave, "bg").Outcome, Is.EqualTo(Outcome.Taken));
            });
        }

        [Test]
        public void OnlyARallyEndsOnDeathAndNothingSurvivesARestart()
        {
            foreach (Kind kind in Automatic.Append(Kind.PlayerGroup).Append(Kind.BotLedGroupWithPlayer))
            {
                Assert.That(EndsOnDeath(kind), Is.EqualTo(kind == Kind.BattlegroundWave), kind.ToString());
                Assert.That(PersistsAcrossRestart(kind), Is.False, kind.ToString());
                Assert.That(ExemptsFromWatchdog(kind), Is.EqualTo(IsPlayerKind(kind)), kind.ToString());
            }
        }
    }

    /// <summary>The lease record itself: claims, takes, refusals, endings and what /lswhy reads.</summary>
    [TestFixture, NonParallelizable]
    public class UT_BotDutyLeases : DutyLeaseFixture
    {
        [SetUp]
        public void Fresh() => BotDutyLeases.Clear();

        [TearDown]
        public void Forget() => BotDutyLeases.Clear();

        [Test]
        public void EachGamebotHoldsAtMostOneLease()
        {
            Bot bot = Gamebot();
            Assert.That(BotDutyLeases.Claim(bot, Kind.BotGroupSession, "g-1", DutyLeaseReason.GroupSession).Outcome, Is.EqualTo(Outcome.Granted));
            BotDutyLeases.ClaimResult raid = BotDutyLeases.Claim(bot, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved);
            Assert.Multiple(() =>
            {
                Assert.That(raid.Outcome, Is.EqualTo(Outcome.Taken));
                Assert.That(raid.Previous.Kind, Is.EqualTo(Kind.BotGroupSession));
                Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.RealmRaid));
                Assert.That(BotDutyLeases.Holds(bot, Kind.BotGroupSession), Is.False);
                Assert.That(BotDutyLeases.HoldersOf(Kind.RealmRaid, "dragon"), Is.EqualTo(new GameBot[] { bot }));
            });
            Assert.That(BotDutyLeases.Claim(bot, Kind.BattlegroundWave, "bg", DutyLeaseReason.BattlegroundRally).Granted, Is.False);
            Assert.That(BotDutyLeases.KindOf(bot), Is.EqualTo(Kind.RealmRaid), "a refusal leaves the lease");
        }

        [Test]
        public void RealPlayersAndCompanionsAreNeverLeased()
        {
            Bot companion = Companion();
            Assert.That(BotDutyLeases.Claim(companion, Kind.PlayerGroup, "Chris", DutyLeaseReason.PlayerGroup).Reason,
                Is.EqualTo(DutyLeaseReason.NotAGamebot));
            Assert.That(BotDutyLeases.Current(companion), Is.Null);
            Assert.That(BotDutyLeases.Claim(null, Kind.RealmRaid, "x", "x").Granted, Is.False);
            Assert.That(BotDutyLeases.WouldGrant(companion, Kind.RealmRaid), Is.False);
        }

        [Test]
        public void ARenewalCanMoveARecruitIntoItsPartyWithoutAnotherClaim()
        {
            Bot bot = Gamebot();
            Group party = Token();
            BotDutyLeases.Claim(bot, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved);
            Assert.That(BotDutyLeases.HoldsWithReason(bot, Kind.RealmRaid, DutyLeaseReason.RaidReserved), Is.True);
            bot.Group = party;
            Assert.That(BotDutyLeases.Claim(bot, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidParty, group: party).Outcome, Is.EqualTo(Outcome.Renewed));
            Assert.Multiple(() =>
            {
                Assert.That(BotDutyLeases.HoldsWithReason(bot, Kind.RealmRaid, DutyLeaseReason.RaidReserved), Is.False);
                Assert.That(BotDutyLeases.HoldsWithReason(bot, Kind.RealmRaid, DutyLeaseReason.RaidParty, "dragon"), Is.True);
            });
        }

        [Test]
        public void AGroupLeaseEndsWhenTheBotLeavesThatGroup()
        {
            Bot bot = Gamebot();
            Group group = Token();
            bot.Group = group;
            BotDutyLeases.Claim(bot, Kind.BotGroupSession, "g-7", DutyLeaseReason.GroupSession, group: group);
            Assert.That(BotDutyLeases.Holds(bot, Kind.BotGroupSession, "g-7"), Is.True);
            bot.Group = Token();
            Assert.That(BotDutyLeases.Current(bot), Is.Null);
            var ended = BotDutyLeases.ForBot(bot.DatabaseID, null, 1)[0];
            Assert.That((ended.Outcome, ended.Reason), Is.EqualTo(("ended", DutyLeaseReason.LeftGroup)));
        }

        [Test]
        public void AnExpiredLeaseEnds()
        {
            Bot bot = Gamebot();
            BotDutyLeases.Claim(bot, Kind.RvrEvent, "rvr-1", DutyLeaseReason.RvrForce, expires: long.MaxValue - 1);
            Assert.That(BotDutyLeases.Current(bot, long.MaxValue - 2), Is.Not.Null);
            Assert.That(BotDutyLeases.Current(bot, long.MaxValue - 1), Is.Null);
            Assert.That(BotDutyLeases.ForBot(bot.DatabaseID, null, 1)[0].Reason, Is.EqualTo(DutyLeaseReason.Expired));
        }

        [Test]
        public void ARallyLeaseEndsOnDeathAndARaidLeaseDoesNot()
        {
            Bot stormer = Gamebot();
            Bot raider = Gamebot();
            BotDutyLeases.Claim(stormer, Kind.BattlegroundWave, "bg", DutyLeaseReason.BattlegroundRally);
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidParty);
            BotDutyLeases.OnDied(stormer);
            BotDutyLeases.OnDied(raider);
            Assert.That(BotDutyLeases.Current(stormer), Is.Null);
            Assert.That(BotDutyLeases.KindOf(raider), Is.EqualTo(Kind.RealmRaid));
        }

        [Test]
        public void LoggingOutEndsAnyLease()
        {
            Bot bot = Gamebot();
            BotDutyLeases.Claim(bot, Kind.PlayerGroup, "Chris", DutyLeaseReason.PlayerGroup);
            BotDutyLeases.OnLoggedOut(bot);
            Assert.That(BotDutyLeases.Current(bot), Is.Null);
            Assert.That(BotDutyLeases.ForBot(bot.DatabaseID, null, 1)[0].Reason, Is.EqualTo(DutyLeaseReason.LoggedOut));
        }

        [Test]
        public void AnActivityThatEndsReleasesOnlyItsOwnLeases()
        {
            Bot first = Gamebot(), second = Gamebot(), other = Gamebot();
            BotDutyLeases.Claim(first, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved);
            BotDutyLeases.Claim(second, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidParty);
            BotDutyLeases.Claim(other, Kind.RealmRaid, "galladoria", DutyLeaseReason.RaidReserved);
            Assert.That(BotDutyLeases.ReleaseOwner(Kind.RealmRaid, "dragon"), Is.EqualTo(2));
            Assert.That(BotDutyLeases.Current(first), Is.Null);
            Assert.That(BotDutyLeases.Current(second), Is.Null);
            Assert.That(BotDutyLeases.Holds(other, Kind.RealmRaid, "galladoria"), Is.True);
            Assert.That(BotDutyLeases.Release(other, Kind.RvrEvent), Is.False, "a release names its kind");
            Assert.That(BotDutyLeases.Release(other, Kind.RealmRaid, "dragon"), Is.False, "and its owner");
        }

        [Test]
        public void TheSameRefusalIsRecordedOncePerChange()
        {
            Bot bot = Gamebot();
            BotDutyLeases.Claim(bot, Kind.PlayerGroup, "Chris", DutyLeaseReason.PlayerGroup);
            for (int i = 0; i < 5; i++)
                BotDutyLeases.Claim(bot, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved);
            BotDutyLeases.Claim(bot, Kind.RvrEvent, "rvr-1", DutyLeaseReason.RvrForce);
            var decisions = BotDutyLeases.ForBot(bot.DatabaseID, null, 10);
            Assert.That(decisions.Select(d => (d.Outcome, d.Kind)), Is.EqualTo(new[]
            {
                ("refused", Kind.RvrEvent), ("refused", Kind.RealmRaid), ("granted", Kind.PlayerGroup),
            }));
            Assert.That(decisions[1].Reason, Is.EqualTo(DutyLeaseReason.InPlayersGroup));
        }

        [Test]
        public void SlashWhyDutyShowsTheLeaseThenTheNewestClaims()
        {
            Bot bot = Gamebot();
            Player chris = RealPlayer();
            JoinPlayersGroup(bot, chris);
            BotDutyLeases.Claim(bot, Kind.RealmRaid, "dragon-alb", DutyLeaseReason.RaidReserved);
            long now = BotDutyLeases.Current(bot).Since + 5_000;
            var lines = BotDutyLeases.WhyLines(bot, bot.Name, now, 3);
            Assert.That(lines[0], Does.StartWith("duty: in a player's group"));
            Assert.That(lines[1], Is.EqualTo("  5s ago refused realm raid (dragon-alb): in_players_group, held by in a player's group"));
            Assert.That(lines[2], Does.Contain("granted in a player's group"));
            Assert.That(BotDutyLeases.WhyLines(bot, bot.Name, now, 3, eRealm.Midgard), Has.Count.EqualTo(1), "another realm reads no claims");
            Assert.That(BotDutyLeases.WhyLines(null, "Nobody", now, 3)[0], Is.EqualTo("duty: not online"));
        }

        [Test]
        public void SlashWhyReadsTheLeaseAndTheDecisionsAsPlainLines()
        {
            Bot bot = Gamebot();
            Assert.That(BotDutyLeases.DescribeLease(null, 0), Is.EqualTo("duty: free (no lease)"));
            BotDutyLeases.Claim(bot, Kind.BotGroupSession, "g-3", DutyLeaseReason.GroupSession);
            BotDutyLeases.Claim(bot, Kind.RealmRaid, "dragon-alb", DutyLeaseReason.RaidReserved, "Golestandt");
            BotDutyLease lease = BotDutyLeases.Current(bot);
            Assert.That(BotDutyLeases.DescribeLease(lease, lease.Since + 240_000),
                Is.EqualTo("duty: realm raid (Golestandt), since 4m, raid_reserved, priority 40"));
            var taken = BotDutyLeases.ForBot(bot.DatabaseID, null, 1)[0];
            Assert.That(BotDutyLeases.Describe(taken, taken.At + 12_000),
                Is.EqualTo("12s ago taken realm raid (dragon-alb): raid_reserved, took bot group (g-3)"));
        }
    }
}
