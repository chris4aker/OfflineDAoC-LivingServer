using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Field = DOL.GS.AutonomousChatFieldTalk;
using Hold = DOL.GS.AutonomousGroupMemberStall.HoldClock;
using Stall = DOL.GS.AutonomousGroupMemberStall;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.GS.Tests;

/// <summary>
/// Owner's PC 2026-11-15 (build 1dc66b6, server-0118.log): the autonomous PvE party albion-f86e88-1370 (leader Cabalistie)
/// stood in Caer Diogel from 00:55 to past 01:19 in phase "Traveling" on its way to griffins in Gwyddneau, while two of its
/// members were in Camelot (AUTONOMOUS_CAPITAL_EGRESS_RECOVERY at 01:05); its members said "pulling" and "need pow" in
/// /say there, and the leader welcomed the player 1,186 units away with "we are out at Aldland, come find us".
/// </summary>
[TestFixture, NonParallelizable]
public class UT_GroupStuckTraveling : DOL.UnitTests.DutyLeaseFixture
{
    [SetUp]
    public void Fresh() => BotLedGroups.Clear();

    [TearDown]
    public void Forget() => BotLedGroups.Clear();

    /// <summary>A real group object with these members (no packets are sent: members are put in directly).</summary>
    private static Group GroupOf(params GameLiving[] members)
    {
        var group = new Group(members[0]);
        var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(group)!;
        foreach (GameLiving member in members)
        {
            list.Add(member);
            member.Group = group;
        }
        return group;
    }

    private static AutonomousBotGroupCoordinator.Directive Directive(GameBot leader, eAutonomousObjectiveKind kind) =>
        new("g-1370", "Traveling", "", "", kind, leader, Vector3.Zero, null, 2, 50, 0, 6, true);

    private const int Cohesion = 500;
    private const long Minute = 60_000L;

    // ---- 1. a member away in another region, or not getting closer, holds its PvE party at most 10 minutes ----------------

    [Test]
    public void AMemberInAnotherRegionHoldsThePartyTenMinutesThenNoLonger()
    {
        var clock = new Hold();
        long now = 1_000;
        bool released = false;
        long releasedAt = 0;
        for (; now <= 11 * Minute; now += 1_000)
            if (clock.Step(false, float.MaxValue, Cohesion, now))
            {
                released = true;
                releasedAt = now;
                break;
            }
        Assert.That(released, Is.True, "Camelot is never closer to Caer Diogel: after 10 minutes the leader goes on");
        Assert.That(releasedAt - 1_000, Is.EqualTo(Stall.LongHoldMilliseconds));
    }

    [Test]
    public void AMemberWalkingButNotGettingCloserHoldsTenMinutes()
    {
        var clock = new Hold();
        Assert.That(clock.Step(true, 18_600, Cohesion, 0), Is.False);
        // It keeps walking (its own 45-second stall rule never fires), always 18,500 to 18,700 units out.
        for (long t = 1_000; t < Stall.LongHoldMilliseconds; t += 1_000)
            Assert.That(clock.Step(true, 18_600 + (t / 1_000 % 2 == 0 ? 100 : -100), Cohesion, t), Is.False, $"t={t}");
        Assert.That(clock.Step(true, 18_650, Cohesion, Stall.LongHoldMilliseconds), Is.True);
    }

    [Test]
    public void GettingCloserOrComingBackRestartsTheClock()
    {
        var clock = new Hold();
        clock.Step(true, 5_000, Cohesion, 0);
        for (long t = 1_000; t <= 9 * Minute; t += 1_000)
            clock.Step(true, 5_000, Cohesion, t);
        Assert.That(clock.Step(true, 4_790, Cohesion, 9 * Minute + 1_000), Is.False, "210 units closer");
        Assert.That(clock.Since, Is.EqualTo(9 * Minute + 1_000));
        long t2 = 9 * Minute + 2_000;
        for (; t2 < 9 * Minute + 1_000 + Stall.LongHoldMilliseconds; t2 += 1_000)
            Assert.That(clock.Step(true, 4_700, Cohesion, t2), Is.False, "100 units is no progress, but the clock restarted");
        Assert.That(clock.Step(true, 4_700, Cohesion, t2), Is.True);

        var away = new Hold();
        for (long t = 0; t <= 9 * Minute; t += 1_000)
            away.Step(false, float.MaxValue, Cohesion, t);
        Assert.That(away.Step(true, 12_000, Cohesion, 9 * Minute + 1_000), Is.False, "back in the leader's region counts as closer");
        Assert.That(away.Since, Is.EqualTo(9 * Minute + 1_000));
    }

    [Test]
    public void AMemberInFormationOrAStaleClockHoldsNothing()
    {
        var clock = new Hold();
        for (long t = 0; t <= 9 * Minute; t += 1_000)
            clock.Step(false, float.MaxValue, Cohesion, t);
        Assert.That(clock.Step(true, 300, Cohesion, 9 * Minute + 1_000), Is.False);
        Assert.That(clock.Since, Is.EqualTo(0), "within the cohesion radius: not holding");

        var stale = new Hold();
        for (long t = 0; t <= 9 * Minute; t += 1_000)
            stale.Step(false, float.MaxValue, Cohesion, t);
        // Nobody stepped it for a minute (it was close, or no leader waited): the old time does not count.
        Assert.That(stale.Step(false, float.MaxValue, Cohesion, 10 * Minute + 1_000), Is.False);
        Assert.That(stale.Since, Is.EqualTo(10 * Minute + 1_000));
    }

    [Test]
    public void TheFortyFiveSecondRuleIsUnchanged()
    {
        Assert.That(Stall.IsStalled(20f, 1, 45_001), Is.True);
        Assert.That(Stall.IsStalled(400f, 1, 45_001), Is.False, "a moving member is still not released by the 45-second rule");
        Assert.That(Stall.ReleaseFromOtherRegion(null, null, 0), Is.False);
    }

    // ---- 2. field /say: the pull calls only at the camp or in a fight -----------------------------------------------------

    private static readonly string[] PullCalls = ["pulling", "inc", "oom", "sitting", "need pow", "wait for heals", "adds", "careful"];

    private static Field.Facts Grouped(bool atCamp, bool inCombat = false, string monster = "griffin") =>
        new Field.Facts(1370, eRealm.Albion, "Reaver", 50, "Aldland", "Krondon", false, false, false, true, 7, false, false, false,
            atCamp, monster, null, 0, false, "Gwyddneau", 0, 1, false, Field.WorldView.Empty)
        { Live = true, InCombat = inCombat };

    private static HashSet<string> SayLines(Field.Facts facts, int draws = 300)
    {
        var texts = new HashSet<string>(StringComparer.Ordinal);
        for (int seed = 0; seed < draws; seed++)
        {
            Field.Said said = Field.Line("say", facts, null, new Random(seed));
            if (said != null)
                texts.Add(said.Text.Trim().TrimEnd('.', '!').ToLowerInvariant());
        }
        return texts;
    }

    private static bool IsPullCall(string text) =>
        PullCalls.Any(call => text == call || text.StartsWith(call + " ", StringComparison.Ordinal) ||
                              text.EndsWith(" inc", StringComparison.Ordinal) || text.Contains(" link", StringComparison.Ordinal));

    [Test]
    public void AGroupedBotOnTheRoadOrInTownSaysNoPullCalls()
    {
        HashSet<string> lines = SayLines(Grouped(atCamp: false));
        Assert.That(lines, Is.Not.Empty);
        Assert.That(lines.Where(IsPullCall), Is.Empty, string.Join(" | ", lines));
    }

    [Test]
    public void AtTheCampOrInAFightThePullCallsStay()
    {
        Assert.That(SayLines(Grouped(atCamp: true)).Any(IsPullCall), Is.True);
        Assert.That(SayLines(Grouped(atCamp: false, inCombat: true)).Any(IsPullCall), Is.True, "a fight on the road");
    }

    // ---- 3. the welcome: no "come find us" to a player beside the leader -------------------------------------------------

    [TestCase(true, true, 100.0, ExpectedResult = BotLedGroups.Lead.Welcome, TestName = "Assembling: meet up at the town")]
    [TestCase(false, true, 1186.0, ExpectedResult = BotLedGroups.Lead.WelcomeHere, TestName = "1,186 units away in Caer Diogel: stick with me")]
    [TestCase(false, true, BotLedGroups.NearLeaderRadius, ExpectedResult = BotLedGroups.Lead.WelcomeHere, TestName = "At the near radius")]
    [TestCase(false, true, BotLedGroups.NearLeaderRadius + 1.0, ExpectedResult = BotLedGroups.Lead.WelcomeOnTheWay, TestName = "Farther: come find us")]
    [TestCase(false, false, -1.0, ExpectedResult = BotLedGroups.Lead.WelcomeOnTheWay, TestName = "Another region: come find us")]
    public BotLedGroups.Lead TheWelcomeFitsWhereThePlayerIs(bool assembling, bool sameRegion, double distance) =>
        BotLedGroups.WelcomeFor(assembling, sameRegion, distance);

    [Test]
    public void TheHereWelcomeHasItsOwnLinesAndNoneSendsThePlayerAway()
    {
        Assert.That(BotLedGroups.Topic(BotLedGroups.Lead.WelcomeHere), Is.EqualTo("lead_welcome_here"));
        Assert.That(BotLedGroups.Fallback(BotLedGroups.Lead.WelcomeHere), Does.Not.Contain("come find us"));
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        VC corpus = VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        VC.Line[] here = corpus.Lines.Where(line => line.Topic == "lead_welcome_here").ToArray();
        Assert.That(here, Is.Not.Empty);
        Assert.That(here.Select(line => line.Text), Has.None.Contains("come find us").And.None.Contains("out at"));
        Assert.That(here.All(line => line.Needs.Contains("lead_call")), Is.True, "only the leader's announce says them");
    }

    // ---- 4. the coordinator: a member in another region still holds at once (the 10 minutes start then) -------------------

    [Test]
    public void AMemberInAnotherRegionStillHoldsThePartyAtFirst()
    {
        Bot leader = Gamebot(), near = Gamebot(), away = Gamebot();
        near.TestX = 200;
        away.Region = 10;
        GroupOf(leader, near, away);
        Assert.Multiple(() =>
        {
            Assert.That(AutonomousBotGroupCoordinator.IsCohesive(Directive(leader, eAutonomousObjectiveKind.GroupPve)), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.FarthestMemberDistance(Directive(leader, eAutonomousObjectiveKind.GroupPve)),
                Is.EqualTo(float.MaxValue));
            Assert.That(AutonomousBotGroupCoordinator.IsCohesive(Directive(leader, eAutonomousObjectiveKind.RvR)), Is.False,
                "a warband keeps its old rule");
        });
        away.Region = 1;
        Assert.That(AutonomousBotGroupCoordinator.IsCohesive(Directive(leader, eAutonomousObjectiveKind.GroupPve)), Is.True);
    }

    // ---- 5. a bot-led party with the player calls its fight in /group, not in /say or /lfg --------------------------------

    [Test]
    public void ABotLedPartyWithThePlayerWatchesItsFight()
    {
        Bot leader = Gamebot(), member = Gamebot();
        Player player = RealPlayer();
        Group group = GroupOf(leader, member, player);
        Assert.That(AutonomousBotCombatCalls.ObservesFight(member), Is.False, "a gamebot party without the player calls nothing");
        BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(player, "g-1370"));
        Assert.Multiple(() =>
        {
            Assert.That(member.IsPlayerLedGroup, Is.False, "the bots lead: they never entered the player's group");
            Assert.That(AutonomousBotCombatCalls.ObservesFight(leader), Is.True);
            Assert.That(AutonomousBotCombatCalls.ObservesFight(member), Is.True);
            // ... and then B4's pull call is the leader's (UT_BotLedReplacementAndBreaks has the line and its odds).
            var view = new AutonomousBotCombatCalls.GroupView(true, 1, 7, 7, false);
            Assert.That(AutonomousBotCombatCalls.GroupEvent(new AutonomousBotCombatCalls.GroupState(), view, 0, new Random(1), botsLead: true),
                Is.EqualTo("pull").Or.Null);
            Assert.That(AutonomousBotCombatCalls.LeaderCallsPull(true, true, true), Is.True);
        });
        BotLedGroups.SetHosted(group, new BotLedGroups.HostedView(player, "g-1370") { RaidGuest = true });
        Assert.That(AutonomousBotCombatCalls.ObservesFight(member), Is.False, "a raid party's guest seat: unchanged");
    }

    [TestCase(true, false, true, false, ExpectedResult = true, TestName = "In the player's group")]
    [TestCase(true, false, false, true, ExpectedResult = true, TestName = "Bot-led party hosting the player")]
    [TestCase(true, false, false, false, ExpectedResult = false, TestName = "Gamebot party, no player")]
    [TestCase(false, true, true, false, ExpectedResult = false, TestName = "A companion")]
    public bool WhoWatchesTheFight(bool autonomous, bool helper, bool playerLed, bool hosted) =>
        AutonomousBotCombatCalls.ObservesFight(autonomous, helper, playerLed, hosted);

    [Test]
    public void WithThePlayerInTheGroupNoPullCallGoesToSay()
    {
        Field.Facts atCampWithPlayer = Grouped(atCamp: true, inCombat: true) with { InPlayerGroup = true };
        HashSet<string> lines = SayLines(atCampWithPlayer);
        Assert.That(lines.Where(IsPullCall), Is.Empty, string.Join(" | ", lines));
    }

    [Test]
    public void TheJoinGreetingsAreNoLfgPost()
    {
        var assembly = typeof(VC).Assembly;
        string Read(string suffix)
        {
            string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
            using Stream stream = assembly.GetManifestResourceStream(name);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        VC corpus = VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        VC.Line healer = corpus.Lines.Single(line => line.Text == "hi all, healer here, try not to die");
        Assert.That(healer.Kind, Is.EqualTo("lfg"));
        Assert.That(AutonomousBotChatCoordinator.IsAmbientChannelLine(healer), Is.False);
        Assert.That(corpus.Lines.Where(line => line.Kind == "lfg" && line.Topic == "join").Any(AutonomousBotChatCoordinator.IsAmbientChannelLine),
            Is.False);
        Assert.That(corpus.Lines.Where(line => line.Topic == "lfm").All(AutonomousBotChatCoordinator.IsAmbientChannelLine), Is.True);
    }
}
