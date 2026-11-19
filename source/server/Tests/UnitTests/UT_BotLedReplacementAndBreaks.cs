using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using static DOL.UnitTests.DutyLeaseTestKit;
using Kind = DOL.GS.eDutyLeaseKind;
using R = DOL.GS.AutonomousPartyReplacement;
using VC = DOL.GS.AutonomousBotVoiceCorpus;

namespace DOL.UnitTests
{
    /// <summary>
    /// Bot-led groups, B4 (docs/BOT-LED-GROUPS.md, "B4 as built"): the leader's search for a replacement ("lf 1 more"), the
    /// natural break ("last pull then town") and the leader's pull call merged with the combat calls' "inc". Automated
    /// only; nothing here is a played check.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_BotLedReplacementAndBreaks : DutyLeaseFixture
    {
        private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;
        private const long Minute = 60_000;
        private static readonly Type SessionType = typeof(AutonomousBotGroupCoordinator).GetNestedType("Session", BindingFlags.NonPublic)!;

        private int _maxMembers;

        [SetUp]
        public void Fresh()
        {
            BotDutyLeases.Clear();
            BotLedGroups.Clear();
            // The server's group size (a setting the tests don't load).
            _maxMembers = DOL.GS.ServerProperties.Properties.GROUP_MAX_MEMBER;
            DOL.GS.ServerProperties.Properties.GROUP_MAX_MEMBER = 8;
        }

        [TearDown]
        public void Forget()
        {
            BotDutyLeases.Clear();
            BotLedGroups.Clear();
            DOL.GS.ServerProperties.Properties.GROUP_MAX_MEMBER = _maxMembers;
        }

        #region The replacement

        [Test]
        public void APartyThatHostsOrHostedThePlayerLooksForAReplacementOnTheWayOrAtCamp()
        {
            Assert.Multiple(() =>
            {
                foreach (string phase in new[] { "Choosing group target", "Traveling", "Grinding" })
                    Assert.That(R.Wanted(true, false, false, phase, 5, 8), Is.True, phase);
                Assert.That(R.Wanted(false, false, false, "Grinding", 5, 8), Is.False, "a bot-only party keeps today's rules");
                Assert.That(R.Wanted(true, true, false, "Grinding", 5, 8), Is.False, "a raid refills its own parties");
                Assert.That(R.Wanted(true, false, true, "Grinding", 5, 8), Is.False, "ending");
                Assert.That(R.Wanted(true, false, false, "Meeting up", 5, 8), Is.False, "the meetup has its own no-show rules");
                Assert.That(R.Wanted(true, false, false, "Regrouping", 5, 8), Is.False);
                Assert.That(R.Wanted(true, false, false, "Grinding", 8, 8), Is.False, "no seat free");
                Assert.That(R.Wanted(true, false, false, null, 5, 8), Is.False);
            });
        }

        [Test]
        public void TheSearchTriesEveryFifteenSecondsAndGivesUpAfterFiveMinutes()
        {
            var search = new R.Search(BotPveGroupRole.Healer, 1_000, holdsParty: true);
            Assert.Multiple(() =>
            {
                Assert.That(R.Next(search, 1_000, viable: false), Is.EqualTo(R.Step.TryNow), "at once");
                search.NextTry = 1_000 + R.SearchEveryMs;
                Assert.That(R.Next(search, 10_000, false), Is.EqualTo(R.Step.Wait));
                Assert.That(R.Next(search, 1_000 + R.SearchEveryMs, false), Is.EqualTo(R.Step.TryNow));
                Assert.That(R.HoldsParty(search, 1_000 + 4 * Minute), Is.True, "a party without its core waits for the recruit");
                Assert.That(R.Next(search, 1_000 + 5 * Minute, viable: false), Is.EqualTo(R.Step.GiveUpEnd));
                Assert.That(R.Next(search, 1_000 + 5 * Minute, viable: true), Is.EqualTo(R.Step.GiveUpCarryOn), "still viable: short-handed");
                Assert.That(R.HoldsParty(search, 1_000 + 5 * Minute), Is.False, "the hold ends with the search");
                Assert.That(R.HoldsParty(new R.Search(BotPveGroupRole.Attacker, 0, false), 1_000), Is.False, "a viable party never holds");
                Assert.That(R.Next(null, 0, true), Is.EqualTo(R.Step.Wait));
            });
        }

        private static R.Candidate C(bool free = true, bool lease = true, bool waiting = true, bool realm = true, int level = 20,
            bool role = true, double minutes = 3) => new(free, lease, waiting, realm, level, role, minutes);

        [Test]
        public void TheRecruitIsAFreeWaitingGamebotOfTheRoleThatTheLeaseAllows()
        {
            var excluded = new Dictionary<string, R.Candidate>
            {
                ["busy"] = C(free: false),
                ["leased elsewhere"] = C(lease: false),
                ["on its own task"] = C(waiting: false),
                ["other realm"] = C(realm: false),
                ["other role"] = C(role: false),
                ["too high"] = C(level: 26),
                ["too low"] = C(level: 14),
                ["too far"] = C(minutes: R.MaximumTravelMinutes + 0.1),
                ["no route"] = C(minutes: -1),
            };
            Assert.Multiple(() =>
            {
                foreach ((string why, R.Candidate candidate) in excluded)
                    Assert.That(R.Pick([candidate], 20), Is.EqualTo(-1), why);
                Assert.That(R.Pick([C(level: 25)], 20), Is.EqualTo(0), "within five levels");
                Assert.That(R.Pick([C(minutes: 6), C(minutes: 2), C(minutes: 4)], 20), Is.EqualTo(1), "the nearest");
                Assert.That(R.Pick([C(minutes: 2, level: 24), C(minutes: 2, level: 21)], 20), Is.EqualTo(1), "then the closest level");
                Assert.That(R.Pick([C(level: 50)], 50), Is.EqualTo(0));
                Assert.That(R.Pick([C(level: 49)], 50), Is.EqualTo(-1), "both at 50, or within five below it");
            });
        }

        [Test]
        public void ALeasedBotIsNeverTakenByTheSearch()
        {
            Bot raider = Gamebot(20), invited = Gamebot(20), free = Gamebot(20);
            Player player = RealPlayer();
            BotDutyLeases.Claim(raider, Kind.RealmRaid, "dragon", DutyLeaseReason.RaidReserved);
            JoinPlayersGroup(invited, player);
            Assert.Multiple(() =>
            {
                foreach (Kind kind in new[] { Kind.BotLedGroupWithPlayer, Kind.BotGroupSession })
                {
                    Assert.That(BotDutyLeases.WouldGrant(raider, kind, "g-1"), Is.False, kind.ToString());
                    Assert.That(BotDutyLeases.WouldGrant(invited, kind, "g-1"), Is.False, kind.ToString());
                    Assert.That(BotDutyLeases.WouldGrant(free, kind, "g-1"), Is.True, kind.ToString());
                }
            });
        }

        [Test]
        public void TheAskSaysTheRoleTheEraWayAndKeepsToTheLfgBudget()
        {
            Assert.Multiple(() =>
            {
                Assert.That(R.NeedWord(BotPveGroupRole.Healer), Is.EqualTo("a healer"));
                Assert.That(R.NeedWord(BotPveGroupRole.Tank), Is.EqualTo("a tank"));
                Assert.That(R.NeedWord(BotPveGroupRole.Attacker), Is.EqualTo("dps"));
                Assert.That(R.LfgFits(0.5, 2), Is.True);
                Assert.That(R.LfgFits(2, 2), Is.False, "at the ceiling the ask waits");
            });
        }

        private static Group GroupOf(params GameLiving[] members)
        {
            var group = new Group(members[0]);
            var list = (List<GameLiving>)typeof(Group).GetField("_groupMembers", Hidden)!.GetValue(group)!;
            foreach (GameLiving member in members)
            {
                list.Add(member);
                member.Group = group;
            }
            return group;
        }

        private static object Session(Group group, string phase, bool hadPlayer, int lockedSize)
        {
            object session = RuntimeHelpers.GetUninitializedObject(SessionType);
            SessionType.GetProperty("Group")!.SetValue(session, group);
            SessionType.GetProperty("Id")!.SetValue(session, "g-9");
            SessionType.GetProperty("Phase")!.SetValue(session, phase);
            SessionType.GetProperty("LockedSize")!.SetValue(session, lockedSize);
            SessionType.GetProperty("HadHostedPlayer")!.SetValue(session, hadPlayer);
            SessionType.GetProperty("ObjectiveKind")!.SetValue(session, eAutonomousObjectiveKind.GroupPve);
            SessionType.GetField("<PveRoles>k__BackingField", Hidden)!.SetValue(session, new Dictionary<long, BotPveGroupRole>());
            return session;
        }

        private static bool Start(object session, BotPveGroupRole role, GameBot[] stayed, bool viable) =>
            (bool)typeof(AutonomousBotGroupCoordinator).GetMethod("TryStartReplacement", HiddenStatic)!
                .Invoke(null, [session, role, stayed, viable, "Brannoc"])!;

        [Test]
        public void AHealerLeavingAHostingPartyStartsASearchThatHoldsTheParty()
        {
            Bot tank = Gamebot(20), hitter = Gamebot(20);
            Group group = GroupOf(tank, hitter);
            object session = Session(group, "Grinding", hadPlayer: true, lockedSize: 2);
            var roles = (Dictionary<long, BotPveGroupRole>)SessionType.GetProperty("PveRoles")!.GetValue(session)!;
            roles[tank.DatabaseID] = BotPveGroupRole.Tank;
            roles[hitter.DatabaseID] = BotPveGroupRole.Attacker;
            Assert.That(Start(session, BotPveGroupRole.Healer, [tank, hitter], viable: false), Is.True);
            var search = (R.Search)SessionType.GetProperty("Replacement")!.GetValue(session)!;
            Assert.Multiple(() =>
            {
                Assert.That(search.Role, Is.EqualTo(BotPveGroupRole.Healer));
                Assert.That(search.HoldsParty, Is.True);
                Assert.That((bool)typeof(AutonomousBotGroupCoordinator).GetMethod("HasActivePveComposition", HiddenStatic)!
                    .Invoke(null, [session, new GameBot[] { tank, hitter }])!, Is.False, "no pulls while it waits for a healer");
            });
            Assert.That(Start(session, BotPveGroupRole.Attacker, [tank, hitter], viable: true), Is.True);
            Assert.That(((R.Search)SessionType.GetProperty("Replacement")!.GetValue(session)!).Role, Is.EqualTo(BotPveGroupRole.Healer),
                "one search at a time: the first role stays");
        }

        [Test]
        public void ABotOnlyPartyOrOneMeetingUpStartsNoSearch()
        {
            Bot tank = Gamebot(20), hitter = Gamebot(20);
            Group group = GroupOf(tank, hitter);
            Assert.Multiple(() =>
            {
                Assert.That(Start(Session(group, "Grinding", hadPlayer: false, 2), BotPveGroupRole.Healer, [tank, hitter], false), Is.False);
                Assert.That(Start(Session(group, "Meeting up", hadPlayer: true, 2), BotPveGroupRole.Healer, [tank, hitter], false), Is.False);
                Assert.That(Start(Session(group, "Grinding", hadPlayer: true, 1), BotPveGroupRole.Healer, [tank], false), Is.False,
                    "one gamebot left: the last-bot box instead (B2)");
            });
        }

        #endregion

        #region The natural break

        [Test]
        public void TheBreakComesWhenHalfTheBagsAreNearlyFullOrTheTaskIsEnding()
        {
            Assert.Multiple(() =>
            {
                Assert.That(BotLedGroups.BreakDue(6, 3, true, 30 * Minute), Is.EqualTo("bags"));
                Assert.That(BotLedGroups.BreakDue(6, 2, true, 30 * Minute), Is.Null, "a third of the bags is not a break");
                Assert.That(BotLedGroups.BreakDue(6, 0, true, 5 * Minute), Is.EqualTo("session"));
                Assert.That(BotLedGroups.BreakDue(6, 0, true, 5 * Minute + 1), Is.Null);
                Assert.That(BotLedGroups.BreakDue(6, 0, false, 0), Is.Null, "a task that hasn't started has no end yet");
                Assert.That(BotLedGroups.BreakDue(0, 0, false, 0), Is.Null);
                Assert.That(BotLedGroups.BreakDue(5, 3, true, 1 * Minute), Is.EqualTo("bags"), "the bags are named first");
            });
        }

        [Test]
        public void ThePartyFinishesTheLastPullBeforeItGoes()
        {
            var midFight = new BotLedGroups.BreakState("bags", 0, fighting: true);
            Assert.Multiple(() =>
            {
                Assert.That(midFight.EndsNow(true, 10_000), Is.False, "never in the middle of a fight");
                Assert.That(midFight.EndsNow(false, 20_000), Is.True, "the fight at the call was the last pull");
            });
            var calm = new BotLedGroups.BreakState("session", 0, fighting: false);
            Assert.Multiple(() =>
            {
                Assert.That(calm.EndsNow(false, 30_000), Is.False, "one more pull first");
                Assert.That(calm.EndsNow(true, 40_000), Is.False);
                Assert.That(calm.EndsNow(false, 50_000), Is.True, "that pull is over");
            });
            var empty = new BotLedGroups.BreakState("bags", 0, fighting: false);
            Assert.Multiple(() =>
            {
                Assert.That(empty.EndsNow(false, BotLedGroups.NoPullAfterCallMs - 1), Is.False);
                Assert.That(empty.EndsNow(false, BotLedGroups.NoPullAfterCallMs), Is.True, "no pull came: the party goes anyway");
            });
        }

        #endregion

        #region The pull call

        private sealed class Fixed : Random
        {
            private readonly double _value;
            public Fixed(double value) => _value = value;
            public override double NextDouble() => _value;
        }

        [Test]
        public void TheLeadersPullCallIsThePullsOneLine()
        {
            var view = new AutonomousBotCombatCalls.GroupView(true, 1, 6, 6, false);
            Assert.Multiple(() =>
            {
                Assert.That(AutonomousBotCombatCalls.PullCallChance(true), Is.GreaterThan(AutonomousBotCombatCalls.PullCallChance(false)));
                Assert.That(AutonomousBotCombatCalls.GroupEvent(new AutonomousBotCombatCalls.GroupState(), view, 0, new Fixed(0.5), botsLead: true),
                    Is.EqualTo("pull"), "a bot-led party calls more of its pulls");
                Assert.That(AutonomousBotCombatCalls.GroupEvent(new AutonomousBotCombatCalls.GroupState(), view, 0, new Fixed(0.5)), Is.Null,
                    "a group the player leads keeps its 35%");
                Assert.That(AutonomousBotCombatCalls.LeaderCallsPull(true, true, true), Is.True);
                Assert.That(AutonomousBotCombatCalls.LeaderCallsPull(true, false, true), Is.False, "a dead leader leaves it to the others");
                Assert.That(AutonomousBotCombatCalls.LeaderCallsPull(true, true, false), Is.False);
                Assert.That(AutonomousBotCombatCalls.LeaderCallsPull(false, true, true), Is.False, "the player leads: anyone's inc");
            });
        }

        private static VC Corpus()
        {
            var assembly = typeof(VC).Assembly;
            string Read(string suffix)
            {
                string name = assembly.GetManifestResourceNames().Single(n => n.EndsWith(suffix, StringComparison.Ordinal));
                using Stream stream = assembly.GetManifestResourceStream(name)!;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            return VC.Parse(Read("chat.corpus.json"), Read("chat.voices.json"));
        }

        [Test]
        public void TheLinesAreThereAndRealmEqual()
        {
            VC corpus = Corpus();
            VC.Line[] pull = corpus.Lines.Where(line => line.Topic == "lead_pull").ToArray();
            VC.Line[] ask = corpus.Lines.Where(line => line.Topic == "lead_lf_more").ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(pull.Length, Is.GreaterThanOrEqualTo(8), "variety");
                Assert.That(pull.All(line => line.Kind == "group" && line.Needs.Contains("lead_call") && line.Needs.Contains("bot_leads")), Is.True);
                Assert.That(ask.Length, Is.GreaterThan(0));
                Assert.That(ask.All(line => line.Kind == "lfg" && line.Text.Contains("{need}")), Is.True, "the ask names the role");
                foreach (string topic in new[] { "lead_pull", "lead_lf_more", "lead_sell", "lead_town", "lead_recruited", "lead_recruit_none", "lead_recruit_end" })
                    Assert.That(corpus.Lines.Where(line => line.Topic == topic).Select(line => line.Realm).Distinct(), Is.EquivalentTo(new[] { "*" }),
                        topic + " is the same for every realm");
            });
        }

        #endregion
    }
}
