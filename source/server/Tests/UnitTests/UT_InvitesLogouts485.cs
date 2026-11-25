using System;
using System.Linq;
using System.Reflection;
using DOL.GS;
using DOL.GS.ServerProperties;
using NUnit.Framework;
using A = DOL.GS.BotInviteDecision.Activity;
using V = DOL.GS.BotInviteDecision.Verdict;

namespace DOL.UnitTests
{
    /// <summary>
    /// Realism 3a (#485): invites, joins and logouts with a person behind them. The rules only (no world): the invite decision
    /// shared by the act and the words, the accept pauses, the bot-led invite's range and its "want in?" waiting for a yes, the
    /// seated logout, the release spread after a wipe, the guild join pause and the bot_show_as_bots setting. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_InvitesLogouts485
    {
        [SetUp]
        public void Clean() => BotInviteDecision.Reset();

        // ---- The decision: the reasons the bot gives decide the invite ----

        [TestCase(48, 6)]
        [TestCase(6, 48)]
        [TestCase(30, 21)]
        public void AWideLevelGapIsANo(int bot, int player) =>
            Assert.That(BotInviteDecision.Decide(bot, player, A.Idle, friend: false, 0, 0.5).Verdict, Is.EqualTo(V.DeclineLevelGap));

        [Test]
        public void TheGapIsTheSameEightTheTellUses()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotInviteDecision.Decide(30, 22, A.Idle, false, 0, 0.5).Verdict, Is.EqualTo(V.Accept), "8 apart");
                Assert.That(BotInviteDecision.Decide(30, 21, A.Idle, false, 0, 0.5).Verdict, Is.EqualTo(V.DeclineLevelGap), "9 apart");
                Assert.That(AutonomousBotChatCoordinator.LevelGapReply(30, 22, new Random(1)), Is.Null, "the tell agrees at 8");
                Assert.That(AutonomousBotChatCoordinator.LevelGapReply(30, 21, new Random(1)), Is.Not.Null, "and at 9");
            });
        }

        [Test]
        public void AFriendTheBotOutlevelsIsPowerLevelledButALowerFriendStillSaysNo()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotInviteDecision.Decide(48, 10, A.Idle, friend: true, 0, 0.5).Verdict, Is.EqualTo(V.Accept));
                Assert.That(BotInviteDecision.Decide(10, 48, A.Idle, friend: true, 0, 0.5).Verdict, Is.EqualTo(V.DeclineLevelGap));
                Assert.That(BotInviteDecision.PowerLevelLine(new Random(3)), Is.Not.Empty);
            });
        }

        [Test]
        public void ABotGroupMidFightNeverLeaves()
        {
            for (double roll = 0; roll < 1; roll += 0.05)
                Assert.That(BotInviteDecision.Decide(20, 20, A.GroupFighting, friend: true, roll, 0.5).Verdict, Is.EqualTo(V.DeclineBotGroup));
        }

        [Test]
        public void ABotInItsGroupSometimesLeavesTheLessItsGroupIsDoingTheMoreOften()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotInviteDecision.LeaveChance(A.GroupGathering, false), Is.GreaterThan(BotInviteDecision.LeaveChance(A.GroupTravelling, false)));
                Assert.That(BotInviteDecision.LeaveChance(A.GroupTravelling, false), Is.GreaterThan(BotInviteDecision.LeaveChance(A.GroupGrinding, false)));
                Assert.That(BotInviteDecision.LeaveChance(A.GroupGrinding, false), Is.GreaterThan(0), "sometimes, even at camp");
                Assert.That(BotInviteDecision.LeaveChance(A.GroupGathering, false), Is.LessThan(1), "not always");
                Assert.That(BotInviteDecision.LeaveChance(A.GroupFighting, true), Is.EqualTo(0));
                Assert.That(BotInviteDecision.LeaveChance(A.GroupGrinding, true), Is.GreaterThan(BotInviteDecision.LeaveChance(A.GroupGrinding, false)),
                    "for a friend more often");
                Assert.That(BotInviteDecision.Decide(20, 20, A.GroupGathering, false, 0.10, 0.5).Verdict, Is.EqualTo(V.LeaveGroupAndAccept));
                Assert.That(BotInviteDecision.Decide(20, 20, A.GroupGathering, false, 0.90, 0.5).Verdict, Is.EqualTo(V.DeclineBotGroup));
            });
        }

        [Test]
        public void ADeclineForItsGroupGivesTheReasonInWords()
        {
            var random = new Random(7);
            foreach (A activity in new[] { A.GroupFighting, A.GroupTravelling, A.GroupGrinding, A.GroupGathering })
                for (int i = 0; i < 20; i++)
                {
                    string line = BotInviteDecision.BotGroupDeclineLine(activity, i % 2 == 0, random);
                    Assert.That(line, Does.Contain("grp").Or.Contain("group").Or.Contain("grouped"), line);
                }
        }

        [Test]
        public void TheAnswerForTheSamePlayerIsHeldTenMinutes()
        {
            int rolls = 0;
            double Roll() => ++rolls == 1 ? 0.1 : 0.9;
            double first = BotInviteDecision.Remember(5, "Dumbledork", 1_000, Roll);
            double again = BotInviteDecision.Remember(5, "Dumbledork", 1_000 + BotInviteDecision.BotGroupAnswerMs - 1, Roll);
            double later = BotInviteDecision.Remember(5, "Dumbledork", 1_000 + BotInviteDecision.BotGroupAnswerMs, Roll);
            double other = BotInviteDecision.Remember(5, "Someone", 1_000, () => 0.5);
            Assert.That((first, again, later, other), Is.EqualTo((0.1, 0.1, 0.9, 0.5)), "asking again does not reroll it");
        }

        [Test]
        public void AFightingBotFinishesItsFightFirst()
        {
            BotInviteDecision.Decision decision = BotInviteDecision.Decide(20, 20, A.Fighting, false, 0, 0.5);
            Assert.Multiple(() =>
            {
                Assert.That(decision.Verdict, Is.EqualTo(V.AcceptAfterFight));
                Assert.That(decision.Joins, Is.True);
                Assert.That(BotInviteDecision.AfterFightCapMs, Is.EqualTo(60_000));
                Assert.That(BotInviteDecision.FightingLine(new Random(1)), Does.Contain("sec"));
            });
        }

        [Test]
        public void NoAcceptIsInstantAndThePauseDependsOnWhatTheBotIsDoing()
        {
            foreach (A activity in Enum.GetValues<A>())
            {
                int quickest = BotInviteDecision.AcceptDelayMs(activity, 0);
                int slowest = BotInviteDecision.AcceptDelayMs(activity, 1);
                Assert.That(quickest, Is.GreaterThanOrEqualTo(2_000), activity.ToString());
                Assert.That(slowest, Is.LessThanOrEqualTo(15_000), activity.ToString());
                Assert.That(slowest, Is.GreaterThan(quickest), activity.ToString());
            }
            Assert.Multiple(() =>
            {
                Assert.That(BotInviteDecision.AcceptDelayMs(A.Town, 0.5), Is.GreaterThan(BotInviteDecision.AcceptDelayMs(A.Idle, 0.5)));
                Assert.That(BotInviteDecision.AcceptDelayMs(A.Travelling, 0.5), Is.GreaterThan(BotInviteDecision.AcceptDelayMs(A.Idle, 0.5)));
                Assert.That(BotInviteDecision.AcceptDelayMs(A.GroupGathering, 0), Is.GreaterThan(BotInviteDecision.AcceptDelayMs(A.Idle, 1)),
                    "leaving a group takes a word to it first");
                Assert.That(BotInviteDecision.Decide(20, 20, A.Idle, false, 0, 0).DelayMs, Is.GreaterThanOrEqualTo(2_000));
            });
        }

        [Test]
        public void WhatTheBotIsDoingComesFromItsState()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotInviteDecision.ActivityOf(true, true, "Grinding", false, false, false, false), Is.EqualTo(A.GroupFighting));
                Assert.That(BotInviteDecision.ActivityOf(true, false, "Traveling", false, false, true, false), Is.EqualTo(A.GroupTravelling));
                Assert.That(BotInviteDecision.ActivityOf(true, false, "Grinding", false, false, false, false), Is.EqualTo(A.GroupGrinding));
                Assert.That(BotInviteDecision.ActivityOf(true, false, "Meeting up", false, false, false, true), Is.EqualTo(A.GroupGathering));
                Assert.That(BotInviteDecision.ActivityOf(true, false, null, false, false, false, false), Is.EqualTo(A.GroupGathering));
                Assert.That(BotInviteDecision.ActivityOf(false, false, null, true, true, true, true), Is.EqualTo(A.Fighting));
                Assert.That(BotInviteDecision.ActivityOf(false, false, null, false, true, false, true), Is.EqualTo(A.Resting));
                Assert.That(BotInviteDecision.ActivityOf(false, false, null, false, false, true, true), Is.EqualTo(A.Travelling));
                Assert.That(BotInviteDecision.ActivityOf(false, false, null, false, false, false, true), Is.EqualTo(A.Town));
                Assert.That(BotInviteDecision.ActivityOf(false, false, null, false, false, false, false), Is.EqualTo(A.Idle));
            });
        }

        // ---- Bot-led invites: range, the asked pause, and "want in?" waits for a yes ----

        private static BotLedGroups.Candidate Party(double distance) =>
            new(1, "Leader", eRealm.Albion, 20, 6, 8, true, Hunting: true, OnRealmDuty: false, Reachable: true, HasPlayer: false, Distance: distance);

        [Test]
        public void NoInviteFromAcrossTheMap()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.CandidateBlocks(Party(58_420), eRealm.Albion, 20), Is.EqualTo("too_far"), "the played /lfg's distance");
                Assert.That(BotLedGroups.CandidateBlocks(Party(BotLedGroups.MaxInviteDistance), eRealm.Albion, 20), Is.Null);
                Assert.That(BotLedGroups.CandidateBlocks(Party(2_000), eRealm.Albion, 20), Is.Null);
                Assert.That(BotLedGroups.CandidateBlocks(Party(-1), eRealm.Albion, 20), Is.Null, "another region (a capital's door) is not measured");
            });
        }

        [Test]
        public void TheLeaderReadsAndTypesBeforeAnsweringAndTheBoxStillComesWithin15Seconds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.AskedTellDelayMinMs, Is.GreaterThanOrEqualTo(3_000), "not the same second");
                Assert.That(BotLedGroups.AskedWindowMs, Is.EqualTo(15_000), "the owner's 15 seconds (2026-11-04)");
                Assert.That(BotLedGroups.AskedLatestStartMs + BotLedGroups.AskedLongestDelayMs, Is.LessThanOrEqualTo(BotLedGroups.AskedWindowMs));
                Assert.That(BotLedGroups.AskedLatestStartMs, Is.GreaterThanOrEqualTo(BotLedGroups.PulseIntervalMs));
            });
        }

        [Test]
        public void AnUnaskedWantInWaitsForAYesBeforeTheBox()
        {
            var memory = new BotLedGroups.InviteMemory(0);
            memory.AwaitAnswer(42, 1_000);
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.CadenceBlocks(memory, 2_000, true, long.MaxValue), Is.EqualTo("box_waiting"), "nothing else meanwhile");
                Assert.That(memory.TakeYes(7), Is.False, "a yes to another leader");
                Assert.That(memory.TakeYes(42), Is.True, "the yes brings the box");
                Assert.That(memory.TakeYes(42), Is.False, "once");
                Assert.That(memory.Pending, Is.True, "the box is still to answer");
            });
        }

        [Test]
        public void NoAnswerToWantInLetsItGo()
        {
            var memory = new BotLedGroups.InviteMemory(0);
            memory.AwaitAnswer(42, 1_000);
            Assert.Multiple(() =>
            {
                Assert.That(memory.AnswerWaitOver(1_000 + BotLedGroups.UnaskedAnswerWaitMs), Is.False);
                Assert.That(memory.AnswerWaitOver(1_001 + BotLedGroups.UnaskedAnswerWaitMs), Is.True);
                Assert.That(memory.Pending, Is.False);
                Assert.That(memory.TakeYes(42), Is.False, "a late yes brings no box");
                Assert.That(memory.AnswerWaitOver(10 * BotLedGroups.UnaskedAnswerWaitMs), Is.False, "logged once");
            });
        }

        [Test]
        public void ANoToWantInEndsTheWait()
        {
            var memory = new BotLedGroups.InviteMemory(0);
            memory.AwaitAnswer(42, 1_000);
            memory.Declined(42, 2_000);
            Assert.That((memory.Pending, memory.TakeYes(42)), Is.EqualTo((false, false)));
        }

        // ---- Logging out: the goodbye, then seated about 20 s ----

        [Test]
        public void ALogoutSitsAboutTwentySecondsAfterTheGoodbye()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotSchedule.QuitSeatedMs(0), Is.EqualTo(18_000));
                Assert.That(AutonomousBotSchedule.QuitSeatedMs(1), Is.EqualTo(24_000));
                Assert.That(AutonomousBotSchedule.QuitSeatedMs(0.5), Is.EqualTo(21_000));
                Assert.That(AutonomousBotSchedule.SitAfterLineMinMs, Is.GreaterThan(0), "the line first, then it sits");
            });
        }

        [Test]
        public void TheGoodbyeIsSaidOnceNotAtEveryTry()
        {
            long ten = AutonomousBotSchedule.GoodbyeRepeatMs;
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotSchedule.SaysGoodbye(null, 5_000), Is.True);
                Assert.That(AutonomousBotSchedule.SaysGoodbye(5_000, 5_000 + 30_000), Is.False, "attacked while quitting: leaves quietly later");
                Assert.That(AutonomousBotSchedule.SaysGoodbye(5_000, 5_000 + ten), Is.True);
            });
        }

        // ---- A wipe releases one by one ----

        [Test]
        public void EachBotReleasesAfterItsOwn15To45Seconds()
        {
            int lowest = int.MaxValue, highest = int.MinValue;
            for (long id = 1; id <= 2_000; id++)
            {
                int wait = BotReleaseTiming.WipeReleaseMs(id, 3);
                Assert.That(wait, Is.InRange(15_000, 45_000));
                Assert.That(BotReleaseTiming.WipeReleaseMs(id, 3), Is.EqualTo(wait), "the same death, the same wait");
                lowest = Math.Min(lowest, wait);
                highest = Math.Max(highest, wait);
            }
            Assert.That((lowest < 17_000, highest > 43_000), Is.EqualTo((true, true)), "the whole range is used");
            Assert.That(Enumerable.Range(0, 10).Select(death => BotReleaseTiming.WipeReleaseMs(77, death)).Distinct().Count(), Is.GreaterThan(5),
                "another death, another wait");
        }

        [Test]
        public void AFourBotWipeIsNotOneBeat()
        {
            // The old rule: all four corpses gone in the same 3 s timer tick. Now the first and last release are seconds apart.
            int spread = 0, parties = 500;
            for (int party = 0; party < parties; party++)
            {
                int[] waits = Enumerable.Range(0, 4).Select(member => BotReleaseTiming.WipeReleaseMs(10_000 + party * 4 + member, 2)).ToArray();
                if (waits.Max() - waits.Min() >= 6_000)
                    spread++;
            }
            Assert.That(spread, Is.GreaterThan(parties * 9 / 10), $"{spread} of {parties} wipes spread over 6 s or more");
        }

        // ---- Guild joins ----

        [Test]
        public void AGuildJoinTakesThreeToTenSeconds()
        {
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotGuilds.GuildJoinDelayMs(0), Is.EqualTo(3_000));
                Assert.That(AutonomousBotGuilds.GuildJoinDelayMs(1), Is.EqualTo(10_000));
                Assert.That(AutonomousBotGuilds.GuildJoinFightCapMs, Is.EqualTo(60_000));
            });
        }

        // ---- bot_show_as_bots (owner decision 2026-11-24): hidden by default ----

        [Test]
        public void TheBotTagIsASettingHiddenByDefault()
        {
            ServerPropertyAttribute attribute = typeof(Properties).GetField(nameof(Properties.BOT_SHOW_AS_BOTS))
                .GetCustomAttribute<ServerPropertyAttribute>();
            Assert.Multiple(() =>
            {
                Assert.That((attribute.Category, attribute.Key, attribute.DefaultValue), Is.EqualTo(("bots", "bot_show_as_bots", (object) false)));
                Assert.That(LivingServerSettingsUpgrade.Changes.Select(change => change.Key), Does.Not.Contain("bot_show_as_bots"),
                    "a new key: a world from an earlier build has no row and takes the hidden default without an upgrade line");
                Assert.That(BotGroupInvite.WhoTag(false), Is.Empty);
                Assert.That(BotGroupInvite.WhoTag(true), Is.EqualTo(" [Playerbot]"));
            });
        }

        [Test]
        public void RefusalsSayGamebotOrBuffbotOnlyWhenBotsAreShown()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotGroupInvite.OtherRealmMessage(true), Does.Contain("gamebot"));
                Assert.That(BotGroupInvite.OtherRealmMessage(false), Does.Not.Contain("bot"));
                Assert.That(BotGroupInvite.BusyMessage("Bob", BotGroupInvite.Duty.Buffing, true), Does.Contain("buffbot"));
                Assert.That(BotGroupInvite.BusyMessage("Bob", BotGroupInvite.Duty.Buffing, false), Does.Not.Contain("bot"));
                foreach (BotGroupInvite.Duty duty in Enum.GetValues<BotGroupInvite.Duty>())
                    Assert.That(BotGroupInvite.BusyMessage("Bob", duty, false) ?? string.Empty, Does.Not.Contain("gamebot").And.Not.Contain("buffbot"),
                        duty.ToString());
            });
        }
    }
}
