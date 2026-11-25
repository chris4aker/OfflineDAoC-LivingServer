using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.Database;
using DOL.GS;
using DOL.GS.Keeps;
using DOL.GS.ServerRules;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #522: one set of stealth rules for the player and the gamebots (StealthRules), and the stealther play built on it
    /// (BotStealthTravel, BotStealthArcher, BotStealthEdge, the planning and scan filters). The acceptance test of the issue:
    /// a bot and a player with the same stealth spec get the same speed, the same "too close" answer and the same uncover
    /// answer from a guard, and no gamebot is among the uncover timer's candidates. Tests only: nothing here is seen in a
    /// played log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_StealthRules
    {
        // The game objects below need a server double: GameObject's type initializer reads the database (empty here).
        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, ReadOnly>();
        public class ReadOnly : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("No database writes allowed: " + method.Name);
                Type type = method.ReturnType;
                return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                    ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null;
            }
        }
        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => Empty;
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
        }

        private GameServer _previous;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void Cleanup() => GameServer.LoadTestDouble(_previous);

        private static void SetField(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.SetValue(target, value);

        private static GameBot Bot(eRealm realm, bool autonomous, bool temporaryHelper = false)
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            SetField(typeof(GameNPC), bot, "m_brains", new ArrayList());
            bot.Realm = realm;
            typeof(GameBot).GetProperty(nameof(GameBot.IsAutonomousWorldBot))!.SetValue(bot, autonomous);
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(bot, temporaryHelper);
            return bot;
        }

        private static T Npc<T>(eRealm realm) where T : GameNPC
        {
            var npc = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            SetField(typeof(GameNPC), npc, "m_brains", new ArrayList());
            npc.Realm = realm;
            return npc;
        }

        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }

        private static string Src(params string[] parts) =>
            File.ReadAllText(Path.Combine(new[] { RepoRoot(), "source", "server", "GameServer" }.Concat(parts).ToArray()));

        private static string Between(string text, string start, string end)
        {
            int s = text.IndexOf(start, StringComparison.Ordinal);
            Assert.That(s, Is.GreaterThanOrEqualTo(0), start);
            int e = text.IndexOf(end, s + start.Length, StringComparison.Ordinal);
            Assert.That(e, Is.GreaterThan(s), end);
            return text.Substring(s, e - s);
        }

        // ---- speed ---------------------------------------------------------------------------------------------------

        [Test]
        public void ThePlayerAndTheGamebotsTakeOneStealthSpeedRule()
        {
            string calc = Src("propertycalc", "MaxSpeedCalculator.cs");
            Assert.That(calc, Does.Contain("StealthRules.SpeedFactor(player)"));
            Assert.That(calc, Does.Contain("StealthRules.SpeedFactor(bot)"));
            Assert.That(calc, Does.Not.Contain("PvpRules.StealthSpeedFactor("), "the formula is called in one place, StealthRules");
            Assert.That(Src("serverrules", "StealthRules.cs"), Does.Contain("PvpRules.StealthSpeedFactor("),
                "and that place reuses #521's formula");

            Assert.That(StealthRules.SpeedFactor(null), Is.EqualTo(1.0));
            Assert.That(StealthRules.SpeedFactor(Bot(eRealm.Albion, true)), Is.EqualTo(1.0), "not hidden: full speed");
            // The formula itself (#521): spec 30 at level 50 is half a spec-50 assassin's slow-down.
            Assert.That(PvpRules.StealthSpeedFactor(30, 50, 0, false), Is.EqualTo(0.3 + 40 * 0.3 / 60).Within(1e-9));
        }

        // ---- too close to hide ---------------------------------------------------------------------------------------

        [Test]
        public void TheTooCloseCircleIsThePlayersForEveryone()
        {
            Assert.That(StealthRules.HideRadius(50, 50, false), Is.EqualTo(128));
            Assert.That(StealthRules.HideRadius(50, 35, false), Is.EqualTo(396), "a spec-35 assassin: about 400");
            Assert.That(StealthRules.HideRadius(50, 25, true), Is.EqualTo(1152), "Detect Hidden doubles it");
            Assert.That(StealthRules.HideRadius(20, 50, false), Is.LessThanOrEqualTo(0));
            Assert.That(StealthRules.HideRadius(50, 80, false), Is.EqualTo(128), "Stealth counts up to 50");
            for (int level = 1; level <= 50; level += 7)
                for (int spec = 0; spec <= 50; spec += 5)
                    foreach (bool dh in new[] { false, true })
                        Assert.That(CompanionStealth.HideRadius(level, spec, dh), Is.EqualTo(StealthRules.HideRadius(level, spec, dh)));

            Assert.That(Src("skillhandler", "StealthSpecHandler.cs"), Does.Contain("StealthRules.IsTooClose(obj, player)"));
            string bot = Between(Src("bots", "GameBot.cs"), "public override void Stealth(bool goStealth)", "public void StartStealthUncoverAction()");
            Assert.That(bot, Does.Contain("StealthRules.EnemyTooClose(this) != null"), "a gamebot's Stealth(true) keeps the rule");
            Assert.That(Src("bots", "CompanionStealth.cs"), Does.Contain("StealthRules.EnemyTooClose(bot) == null"));
        }

        // ---- uncover -------------------------------------------------------------------------------------------------

        [Test]
        public void OnlyMonstersAndGuardsUncover_NoGamebotOrItsPetIsACandidate()
        {
            Assert.That(StealthRules.MayUncover(Npc<GameNPC>(eRealm.None)), Is.True, "a monster");
            Assert.That(StealthRules.MayUncover(Npc<GameKeepGuard>(eRealm.Midgard)), Is.True, "a keep guard");
            Assert.That(StealthRules.MayUncover(Bot(eRealm.Midgard, true)), Is.False, "an autonomous gamebot sees, it never uncovers");
            Assert.That(StealthRules.MayUncover(Bot(eRealm.Midgard, false, true)), Is.False, "a companion neither");
            Assert.That(StealthRules.MayUncover(null), Is.False);

            // Both timers ask the one rule: the player's (GamePlayer.UncoverStealthAction) and the gamebot's.
            string player = Between(Src("gameobjects", "GamePlayer.cs"), "protected class UncoverStealthAction", "private bool CanDetectStealthedBot");
            Assert.That(player, Does.Contain("StealthRules.Look(player"));
            Assert.That(player, Does.Not.Contain("GetNPCsInRadius"), "no second candidate loop");
            Assert.That(player, Does.Contain("RvrStealthRecord.Uncovered("), "every uncover of the player is logged");
            string bot = Between(Src("bots", "GameBot.cs"), "private int StealthUncoverTick", "private bool UncoverSightOnNavmesh");
            Assert.That(bot, Does.Contain("StealthRules.Look(this"));
            Assert.That(Src("serverrules", "StealthRules.cs"), Does.Contain("!MayUncover(npc)"));
        }

        [Test]
        public void TheUncoverNumbersAreTheBaseServers()
        {
            // 125 + 20 per level above the stealth spec, +125 with Detect Hidden, never under 126.
            Assert.That(StealthRules.UncoverRadius(50, 35, false, false, false, false), Is.EqualTo(425));
            Assert.That(StealthRules.UncoverRadius(50, 35, true, false, false, false), Is.EqualTo(550));
            Assert.That(StealthRules.UncoverRadius(50, 35, true, true, false, false), Is.EqualTo(425), "the hider's own Detect Hidden cancels it");
            Assert.That(StealthRules.UncoverRadius(50, 35, true, false, true, false), Is.EqualTo(425), "Camouflage cancels it");
            Assert.That(StealthRules.UncoverRadius(20, 50, false, false, false, false), Is.EqualTo(126));

            Assert.That(StealthRules.Sense(0, 50, 50), Is.EqualTo(UncoverSense.Sight));
            Assert.That(StealthRules.Sense(44, 50, 50), Is.EqualTo(UncoverSense.Sight));
            Assert.That(StealthRules.Sense(105, 50, 50), Is.EqualTo(UncoverSense.Hearing));
            Assert.That(StealthRules.Sense(255, 50, 50), Is.EqualTo(UncoverSense.Hearing));
            Assert.That(StealthRules.Sense(180, 50, 50), Is.EqualTo(UncoverSense.None), "the 30 degrees straight behind are safe");

            Assert.That(StealthRules.UncoverChance(50, 35, 100, 425), Is.EqualTo(0.25).Within(1e-9));
            Assert.That(StealthRules.UncoverChance(50, 35, 425, 425), Is.EqualTo(0.1).Within(1e-9));
        }

        [Test]
        public void AGuardGivesAPlayerAndAGamebotTheSameUncoverAnswer()
        {
            // The same guard, the same stealth spec, the same spot: the one rule answers both alike (the inputs are the
            // hider's spec, abilities and effects, never what kind of character it is).
            foreach (int spec in new[] { 20, 35, 50 })
                foreach (double angle in new[] { 0.0, 60, 105, 180, 300 })
                {
                    double radius = StealthRules.UncoverRadius(55, spec, true, false, false, false);
                    double player = StealthRules.Sense(angle, 55, 50) == UncoverSense.None ? 0 : StealthRules.UncoverChance(55, spec, 200, radius);
                    double bot = StealthRules.Sense(angle, 55, 50) == UncoverSense.None ? 0 : StealthRules.UncoverChance(55, spec, 200, radius);
                    Assert.That(bot, Is.EqualTo(player));
                }
            // And the guards that aggro on stealthers (GuardStealther) see stealthed gamebots as they see stealthed players.
            string guard = Src("ai", "brain", "Guards", "KeepGuardBrain.cs");
            Assert.That(Regex.IsMatch(guard, @"npc\.IsStealthed && Body is not GuardStealther"), Is.True);
        }

        // ---- see -----------------------------------------------------------------------------------------------------

        [Test]
        public void SeeHiddenHasIts162Strength()
        {
            Assert.That(StealthRules.SeeHiddenPre162(50), Is.EqualTo(900));
            Assert.That(StealthRules.SeeHidden162Share(50, 50), Is.EqualTo(0.25).Within(1e-9), "a quarter against a spec at the assassin's level");
            Assert.That(StealthRules.SeeHidden162Share(50, 0), Is.EqualTo(0.75).Within(1e-9), "three quarters against no spec");
            Assert.That(StealthRules.SeeHiddenRange(50, 35), Is.EqualTo(576));
            Assert.That(StealthRules.SeeHiddenRange(50, 20), Is.EqualTo(1089));

            var archer35 = new StealthLook(50, 35, ObserverDetectHidden: true, ObserverSeeHidden: true);
            Assert.That(StealthRules.SeeRange(archer35), Is.EqualTo(Math.Max(15 * 50 + 250, 576)));
            Assert.That(StealthRules.SeeRange(archer35 with { StealthSpec = 20 }), Is.EqualTo(1750), "Detect Hidden's 30 x 50 + 250 beats See Hidden there");
            Assert.That(StealthRules.SeeRange(new StealthLook(50, 20, ObserverSeeHidden: true)), Is.EqualTo(1089));
            Assert.That(StealthRules.SeeRange(new StealthLook(50, 20, ObserverSeeHidden: true, Camouflage: true)), Is.EqualTo(725),
                "Camouflage cancels See Hidden");
            Assert.That(StealthRules.SeeRange(new StealthLook(50, 20, ObserverSeeHidden: true, TargetAssassin: true)), Is.EqualTo(725),
                "See Hidden never works on assassins");
            Assert.That(StealthRules.SeeRange(new StealthLook(50, 0, ObserverDetectHidden: true)), Is.EqualTo(1900), "the cap");
            Assert.That(StealthRules.SeeRange(new StealthLook(50, 0, Vanish: true)), Is.Zero);

            // The bots' notice reads the same rule as the player's client.
            var sighting = new RvrStealthSighting(100, 50, 20, ObserverSeeHidden: true);
            Assert.That(RvrStealthNotice.HumanRange(sighting), Is.EqualTo(1089));
            Assert.That(RvrStealthNotice.Limit(sighting), Is.EqualTo(RvrStealthNotice.MaxRange), "a bot never notices beyond 350");
            string player = Src("gameobjects", "GamePlayer.cs");
            Assert.That(player, Does.Not.Contain("2700 - 36 *"), "no second copy of the pre-1.62 range");
            Assert.That(Regex.Matches(player, @"StealthRules\.SeeRange\(").Count, Is.EqualTo(2), "CanDetect and CanDetectStealthedBot");
        }

        // ---- planning and scan ---------------------------------------------------------------------------------------

        [Test]
        public void ThePlannerNeverHuntsAHiddenStealther()
        {
            Assert.That(AutonomousWorldBotController.IsRvrHuntCandidate(eRealm.Albion, false, true, true), Is.True);
            Assert.That(AutonomousWorldBotController.IsRvrHuntCandidate(eRealm.Albion, true, true, true), Is.False);
            Assert.That(AutonomousWorldBotController.IsRvrHuntCandidate(eRealm.None, false, true, true), Is.False);
            Assert.That(AutonomousWorldBotController.IsRvrHuntCandidate(eRealm.Albion, false, false, true), Is.False);
            Assert.That(AutonomousWorldBotController.IsRvrHuntCandidate(eRealm.Albion, false, true, false), Is.False);
        }

        [Test]
        public void KeepDefendersNoticeStealthersByTheStealthRuleNow()
        {
            // Gamebots left the uncover timer, so a bot at an objective would be blind to a stealthed player without this.
            Assert.That(AutonomousWorldBotController.StealthNoticeApplies(true, true, false), Is.True, "at an objective");
            Assert.That(AutonomousWorldBotController.StealthNoticeApplies(false, false, true), Is.True, "the field rule");
            Assert.That(AutonomousWorldBotController.StealthNoticeApplies(true, false, false), Is.False, "rvr_bot_perception_bots off");
            Assert.That(AutonomousWorldBotController.StealthNoticeApplies(false, true, false), Is.False, "rvr_bot_perception off");
        }

        // ---- the ambush approach's scope -----------------------------------------------------------------------------

        [Test]
        public void CompanionsAndBotsOutOfTheFieldDoNotStalk()
        {
            Assert.That(BotRvrAmbush.InAmbushScope(Bot(eRealm.Albion, false, true)), Is.False, "a companion");
            Assert.That(BotRvrAmbush.InAmbushScope(Bot(eRealm.Albion, false)), Is.False, "a /spawn helper");
            Assert.That(BotRvrAmbush.InAmbushScope(Bot(eRealm.Albion, true)), Is.False, "a gamebot outside a battle area");
            Assert.That(BotRvrAmbush.InAmbushScope(null), Is.False);
            Assert.That(RvrTacticsScope.Allows(eDutyLeaseKind.None, atObjective: true), Is.False, "a keep defender at its post");
            Assert.That(Src("bots", "BotRvrAmbush.cs"), Does.Contain("IsAllowedToAttack(bot, target, true) && InAmbushScope(bot)"));
        }

        // ---- travel --------------------------------------------------------------------------------------------------

        private static StealthTravelMove Travel(bool inScope = true, bool canStealth = true, bool camping = true, bool small = true,
            bool follower = false, bool leaderStealthed = false, bool stealthed = false, bool fighting = false, bool mustShow = false,
            bool mounted = false, bool safeArea = false) =>
            BotStealthTravel.Decide(inScope, canStealth, camping, small, follower, leaderStealthed, stealthed, fighting, mustShow,
                mounted, safeArea);

        [Test]
        public void AStealtherTravelsHiddenAndComesOutOnlyToFightRideOrPort()
        {
            Assert.That(Travel(), Is.EqualTo(StealthTravelMove.Hide), "out of the keep: hide for the walk");
            Assert.That(Travel(stealthed: true), Is.EqualTo(StealthTravelMove.Leave), "hidden: stays hidden");
            Assert.That(Travel(safeArea: true), Is.EqualTo(StealthTravelMove.Leave), "not inside the portal keep or the bind");
            Assert.That(Travel(mounted: true), Is.EqualTo(StealthTravelMove.Leave), "never pulled off a horse");
            Assert.That(Travel(stealthed: true, mustShow: true), Is.EqualTo(StealthTravelMove.Unhide), "to ride a route or use a porter");
            Assert.That(Travel(stealthed: true, fighting: true), Is.EqualTo(StealthTravelMove.Leave), "the fight owns stealth");
            Assert.That(Travel(small: false), Is.EqualTo(StealthTravelMove.Leave), "eights and warbands unchanged");
            Assert.That(Travel(inScope: false), Is.EqualTo(StealthTravelMove.Leave), "out of scope: unchanged");
            Assert.That(Travel(canStealth: false), Is.EqualTo(StealthTravelMove.Leave));
            Assert.That(Travel(camping: false), Is.EqualTo(StealthTravelMove.Leave), "a Minstrel soloer is not a camping stealther");
        }

        [Test]
        public void ADuosFollowerCopiesItsLeader()
        {
            Assert.That(Travel(camping: false, follower: true, leaderStealthed: true), Is.EqualTo(StealthTravelMove.Hide),
                "the second of a stealth duo hides with its leader");
            Assert.That(Travel(follower: true, leaderStealthed: false, stealthed: true), Is.EqualTo(StealthTravelMove.Unhide),
                "and comes out with it");
            Assert.That(Travel(follower: true, leaderStealthed: false), Is.EqualTo(StealthTravelMove.Leave));
            Assert.That(Travel(follower: true, leaderStealthed: true, canStealth: false), Is.EqualTo(StealthTravelMove.Leave),
                "only when it can stealth");
            string brain = Src("bots", "BotBrain.cs");
            Assert.That(brain, Does.Contain("BotStealthTravel.Maintain(BotBody);"));
            Assert.That(brain, Does.Contain("!BotStealthTravel.HoldsStealth(BotBody)"), "the idle unhide leaves a travelling stealther be");
        }

        // ---- the archer's opener -------------------------------------------------------------------------------------

        [Test]
        public void TheArchersOpenerIsACriticalShotFromBowRange()
        {
            Assert.That(BotStealthArcher.OpenerRange(1760), Is.EqualTo(1710), "a longbow");
            Assert.That(BotStealthArcher.OpenerRange(2200), Is.EqualTo(2150), "with flight arrows");
            Assert.That(BotStealthArcher.OpenerRange(3000), Is.EqualTo(2300), "never beyond 2,300");
            Assert.That(BotStealthArcher.IsLongOpener(BotStealthArcher.OpenerRange(1600)), Is.True, "a composite bow still opens at 1,550");
            Assert.That(BotStealthArcher.IsLongOpener(BotStealthArcher.OpenerRange(1200)), Is.False, "a shortbow is not the period opener");

            Assert.That(BotStealthArcher.CriticalShotLevel(2), Is.Zero);
            Assert.That(BotStealthArcher.CriticalShotLevel(3), Is.EqualTo(1));
            Assert.That(BotStealthArcher.CriticalShotLevel(27), Is.EqualTo(9));
            Assert.That(BotStealthArcher.CriticalShotLevel(50), Is.EqualTo(9));

            Assert.That(BotStealthArcher.CriticalAllowed(85, false, false), Is.True, "a walking target");
            Assert.That(BotStealthArcher.CriticalAllowed(191, false, false), Is.False, "a running one");
            Assert.That(BotStealthArcher.CriticalAllowed(0, true, false), Is.False, "one swinging in combat");
            Assert.That(BotStealthArcher.CriticalAllowed(0, false, true), Is.False, "a mezzed one");

            Assert.That(BotStealthArcher.StayHiddenChance(35, 50, false), Is.EqualTo(70), "the player's nocking chance");
            Assert.That(BotStealthArcher.StayHiddenChance(35, 50, true), Is.EqualTo(50), "20 less for a Critical Shot");

            Assert.That(Src("bots", "BotBrain.RvrTactics.cs"), Does.Contain("BotStealthArcher.TryOpen(BotBody, target)"));
            Assert.That(Src("bots", "BotBrain.cs"), Does.Contain("BotStealthArcher.ApproachRange(BotBody"));
        }

        // ---- my edge is gone -----------------------------------------------------------------------------------------

        [Test]
        public void AStealtherWhoseOpenerFailedRuns()
        {
            Assert.That(BotStealthEdge.Gone(true, 2_000, 80, true, 1), Is.False, "the opener's follow-up has not landed yet");
            Assert.That(BotStealthEdge.Gone(true, 5_000, 80, true, 1), Is.True, "the target still above half health");
            Assert.That(BotStealthEdge.Gone(true, 5_000, 40, true, 1), Is.False, "the opener took it below half: fight on");
            Assert.That(BotStealthEdge.Gone(true, 1_000, 30, true, 2), Is.True, "a second enemy joined");
            Assert.That(BotStealthEdge.Gone(true, 40_000, 90, true, 1), Is.False, "an old opener is an ordinary fight");
            Assert.That(BotStealthEdge.Gone(false, 5_000, 90, true, 2), Is.False, "no opener, no edge");
            Assert.That(BotStealthEdge.Gone(true, 5_000, 90, false, 1), Is.False, "the target is dead");
            Assert.That(Src("bots", "BotBrain.Retreat.cs"), Does.Contain("BotStealthEdge.Gone(bot, now, out edgeReason)"));
            Assert.That(Src("bots", "BotRvrAmbush.cs"), Does.Contain("BotStealthEdge.Opened(bot, target, now)"));
        }

        // ---- the rows ------------------------------------------------------------------------------------------------

        [Test]
        public void TheStealthRowsAreStable()
        {
            Assert.That(RvrStealthRecord.UncoverLine(7, true, "Secret", eRealm.Albion, "Guard", "Guard", 55, 300, UncoverSense.Sight, true, 1),
                Is.EqualTo("RVR_STEALTH_UNCOVER t=7 hider=player player=Secret realm=Albion byKind=Guard by=Guard byLevel=55 " +
                           "distance=300 sense=sight atKeep=true region=1"));
            Assert.That(RvrStealthRecord.UncoverLine(7, false, "Bot", eRealm.Midgard, "Npc", "Wolf", 40, 125, UncoverSense.Sight, false, 100),
                Does.StartWith("RVR_STEALTH_UNCOVER t=7 hider=bot bot=Bot realm=Midgard"));
            Assert.That(RvrStealthRecord.ArcherLine(9, "Scout", 3, eRealm.Albion, eCharacterClass.Scout, "player", "Player", 1710, true, false, 1),
                Is.EqualTo("RVR_STEALTH_ARCHER_OPEN t=9 bot=Scout id=3 realm=Albion class=Scout target=player targetKind=Player " +
                           "distance=1710 shot=critical stayedHidden=false region=1"));
            Assert.That(RvrStealthRecord.TravelLine(4, "Shade", 5, eRealm.Hibernia, true, "travel", -1, 200),
                Is.EqualTo("RVR_STEALTH_TRAVEL t=4 bot=Shade id=5 realm=Hibernia state=hidden reason=travel nearestPlayer=- region=200"));
        }
    }
}
