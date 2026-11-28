using System;
using System.IO;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// #570 step 4: the party corpse walk. One resurrector of an autonomous gamebot PvE party walks to a distant corpse,
    /// also while regrouping, unless the corpse lies by what killed it; every corpse of the party waits longer while it does.
    /// Pure rules and source pins; tests only until a PC log.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_CorpseWalk570
    {
        private static readonly DateTime Utc = new(2026, 11, 28, 12, 0, 0, DateTimeKind.Utc);

        [SetUp]
        public void Reset()
        {
            AutonomousPveBossDanger.Clear();
            AutonomousGroupDangerRetreat.ResetForTests();
        }

        [TearDown]
        public void Clean()
        {
            AutonomousPveBossDanger.Clear();
            AutonomousGroupDangerRetreat.ResetForTests();
        }

        // ---- the grant ----

        [TestCase(true, true, true, 0, 0, 1_000, ExpectedResult = true)]
        [TestCase(false, true, true, 0, 0, 1_000, ExpectedResult = false)]    // the walker died
        [TestCase(true, false, true, 0, 0, 1_000, ExpectedResult = false)]    // the walker left the party
        [TestCase(true, true, false, 0, 0, 1_000, ExpectedResult = false)]    // the corpse was raised or released
        [TestCase(true, true, true, 0, 0, 5_000, ExpectedResult = false)]     // not seen for 5 s
        [TestCase(true, true, true, 0, 96_000, 99_999, ExpectedResult = true)]
        [TestCase(true, true, true, 0, 99_000, 100_000, ExpectedResult = false)] // 100 s are up
        public bool TheGrantLapsesSensibly(bool walkerAlive, bool inParty, bool corpseDead, long started, long lastSeen, long now) =>
            AutonomousBotGroupCoordinator.IsCorpseWalkCurrent(walkerAlive, inParty, corpseDead, started + 1, lastSeen + 1, now + 1);

        [Test]
        public void AGrantThatNeverStartedIsNotCurrent() =>
            Assert.That(AutonomousBotGroupCoordinator.IsCorpseWalkCurrent(true, true, true, 0, 0, 0), Is.False);

        [TestCase(ConColor.RED, true, false, ExpectedResult = true)]
        [TestCase(ConColor.ORANGE, true, false, ExpectedResult = true)]
        [TestCase(ConColor.PURPLE, false, true, ExpectedResult = true)]     // a fighting monster, aggressive or not
        [TestCase(ConColor.PURPLE, false, false, ExpectedResult = false)]   // a passive giant can be walked past
        [TestCase(ConColor.YELLOW, true, true, ExpectedResult = false)]
        [TestCase(ConColor.BLUE, true, true, ExpectedResult = false)]
        public bool CorpseThreats(ConColor con, bool aggressive, bool fighting) =>
            AutonomousBotGroupCoordinator.IsCorpseThreat(con, aggressive, fighting);

        // ---- the arbitration (one walker, the 3 s recheck, the 30 s refusal) ----

        private static AutonomousBotGroupCoordinator.CorpseWalkStep Arbitrate(bool hasClaim, bool current, bool sameWalker,
            bool sameCorpse, long started, long nextCheck, long nextChoice, long now, out long refuseUntil) =>
            AutonomousBotGroupCoordinator.ArbitrateCorpseWalk(hasClaim, current, sameWalker, sameCorpse, started, nextCheck,
                nextChoice, now, out refuseUntil);

        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(false, false)]
        public void OnlyTheGrantedWalkerToTheGrantedCorpseMayWalk(bool sameWalker, bool sameCorpse)
        {
            Assert.That(Arbitrate(true, true, sameWalker, sameCorpse, 1_000, 10_000, 0, 5_000, out long refuse),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Hold), "one walker, one corpse per party");
            Assert.That(refuse, Is.Zero);
            Assert.That(Arbitrate(true, true, sameWalker, sameCorpse, 1_000, 2_000, 0, 5_000, out _),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Hold), "not even when the holder's recheck is due");
        }

        [Test]
        public void TheGrantedWalkerKeepsItsGrantUntilTheRecheckIsDue()
        {
            const long started = 1_000, nextCheck = started + AutonomousBotGroupCoordinator.CorpseWalkSafetyRecheckMilliseconds;
            Assert.That(Arbitrate(true, true, true, true, started, nextCheck, 0, nextCheck - 1, out long refuse),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Grant));
            Assert.That(refuse, Is.Zero);
            Assert.That(Arbitrate(true, true, true, true, started, nextCheck, 0, nextCheck, out _),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Recheck), "3 s on, the corpse is scanned again");
            Assert.That(Arbitrate(true, true, true, true, started, nextCheck, 0, nextCheck + 20_000, out _),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Recheck));
            Assert.That(AutonomousBotGroupCoordinator.CorpseWalkSafetyRecheckMilliseconds, Is.EqualTo(3_000));
        }

        [Test]
        public void AWalkThatRanOutOfTimeRefusesItsCorpseFor30Seconds()
        {
            const long started = 1_000;
            long now = started + AutonomousBotGroupCoordinator.CorpseWalkMaxMilliseconds;
            Assert.That(Arbitrate(true, false, true, true, started, now - 1_000, 0, now, out long refuse),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Choose), "the lapsed grant is dropped");
            Assert.That(refuse, Is.EqualTo(now + 30_000));
            Assert.That(AutonomousBotGroupCoordinator.CorpseWalkRefusalMilliseconds, Is.EqualTo(30_000));

            Assert.That(Arbitrate(true, false, true, true, started, started + 3_000, 0, now - 1, out refuse),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Choose));
            Assert.That(refuse, Is.Zero, "a walk that went stale (walker died, corpse raised) refuses nothing");
        }

        [Test]
        public void ALapsedGrantFreesThePartyForAnyWalker()
        {
            Assert.That(Arbitrate(true, false, false, false, 1_000, 4_000, 0, 9_000, out _),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Choose));
            Assert.That(Arbitrate(true, false, false, false, 1_000, 4_000, 10_000, 9_000, out _),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Hold), "but the next choice still waits its turn");
        }

        [Test]
        public void WithNoGrantTheNextChoiceWaitsItsTurn()
        {
            Assert.That(Arbitrate(false, false, false, false, 0, 0, 5_000, 4_999, out long refuse),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Hold));
            Assert.That(refuse, Is.Zero);
            Assert.That(Arbitrate(false, false, false, false, 0, 0, 5_000, 5_000, out _),
                Is.EqualTo(AutonomousBotGroupCoordinator.CorpseWalkStep.Choose));
        }

        [Test]
        public void ARefusedCorpseIsNotOfferedUntilItsRefusalRunsOut()
        {
            var refused = new System.Collections.Generic.Dictionary<long, long> { [7] = 40_000 };
            Assert.That(AutonomousBotGroupCoordinator.IsCorpseWalkRefused(refused, 7, 39_999), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.IsCorpseWalkRefused(refused, 7, 40_000), Is.False);
            Assert.That(AutonomousBotGroupCoordinator.IsCorpseWalkRefused(refused, 8, 0), Is.False, "another corpse");
        }

        // ---- the monster beside the corpse ----

        [Test]
        public void AnAttackableMonsterThatOutclassesTheWalkerRefusesTheWalk()
        {
            Assert.That(AutonomousBotGroupCoordinator.IsCorpseNeighbourThreat(false, true, true, false, true,
                ConColor.ORANGE, true, false), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.IsCorpseNeighbourThreat(false, true, true, false, true,
                ConColor.YELLOW, true, true), Is.False, "an even fight is not a reason to leave the body");
        }

        [TestCase(true, true, true, false, true, TestName = "another bot")]
        [TestCase(false, false, true, false, true, TestName = "a dead monster")]
        [TestCase(false, true, false, false, true, TestName = "an inactive monster")]
        [TestCase(false, true, true, true, true, TestName = "a peaceful or untargetable monster")]
        [TestCase(false, true, true, false, false, TestName = "a monster the walker may not attack")]
        public void HarmlessNeighboursNeverRefuse(bool isBot, bool alive, bool active, bool peace, bool attackable) =>
            Assert.That(AutonomousBotGroupCoordinator.IsCorpseNeighbourThreat(isBot, alive, active, peace, attackable,
                ConColor.PURPLE, true, true), Is.False);

        [Test]
        public void TheGrantUsesTheArbitrationRule()
        {
            string walk = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.CorpseWalk.cs");
            int method = walk.IndexOf("public static bool MayWalkToCorpse(", StringComparison.Ordinal);
            Assert.That(method, Is.GreaterThan(0));
            string body = walk.Substring(method, Math.Min(6_000, walk.Length - method));
            Assert.That(body, Does.Contain("bool current = claim != null && ClaimCurrent(claim, group, now);"));
            Assert.That(body, Does.Contain("CorpseWalkStep step = ArbitrateCorpseWalk(claim != null, current, claim?.Walker == walker,\n" +
                "                claim?.Corpse == corpseBot, claim?.Started ?? 0, claim?.NextSafetyCheck ?? 0, state.NextChoiceTick, now,\n" +
                "                out long refuseUntil);"));
            Assert.That(body, Does.Contain("if (refuseUntil > 0)\n                    state.Refused[MemberKey(claim.Corpse)] = refuseUntil;"),
                "a timed-out walk's refusal is stored");
            Assert.That(body, Does.Contain("case CorpseWalkStep.Hold:\n                    return false;"));
            Assert.That(body, Does.Contain("case CorpseWalkStep.Grant:\n                    claim.LastSeen = now;\n                    return true;"));
            Assert.That(body, Does.Contain("candidates = step == CorpseWalkStep.Recheck ? [claim.Corpse] : BotMembers(group)"),
                "a recheck scans the claimed corpse alone");
            Assert.That(body, Does.Contain("!IsCorpseWalkRefused(state.Refused, MemberKey(member), now))"));
            Assert.That(body, Does.Contain("if (CorpseSafeToApproach(walker, candidate, now, out string reason))"));
        }

        [Test]
        public void TheMonsterScanJudgesTheMonstersConAsTheWalkerSeesIt()
        {
            string walk = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.CorpseWalk.cs");
            int method = walk.IndexOf("private static bool CorpseSafeToApproach(", StringComparison.Ordinal);
            Assert.That(method, Is.GreaterThan(0));
            string body = walk.Substring(method);
            Assert.That(body, Does.Contain("foreach (GameNPC npc in corpse.GetNPCsInRadius(CorpseThreatRadius))"));
            Assert.That(body, Does.Contain("GameServer.ServerRules.IsAllowedToAttack(walker, npc, true)"));
            Assert.That(body, Does.Contain("if (IsCorpseNeighbourThreat(isBot, alive, active, peaceOrCantTarget, attackable,\n" +
                "                    ConLevels.GetConColor(walker.GetConLevel(npc)), aggressive, npc.InCombat))\n" +
                "            {\n                reason = "), "the walker's view of the monster, and a threat refuses");
            Assert.That(body, Does.Not.Contain("npc.GetConLevel(walker)"), "not the monster's view of the walker");
            Assert.That(AutonomousBotGroupCoordinator.CorpseThreatRadius, Is.EqualTo(1_500));
        }

        [Test]
        public void OutsideAGamebotPartyTheOldRuleStands()
        {
            Assert.That(AutonomousBotGroupCoordinator.MayWalkToCorpse(null, null, regrouping: false), Is.True);
            Assert.That(AutonomousBotGroupCoordinator.MayWalkToCorpse(null, null, regrouping: true), Is.False);
        }

        // ---- the wait ----

        [Test]
        public void ARaiseUnderWayGetsTheLongWait()
        {
            Assert.That(AutonomousBotGroupCoordinator.CorpseReleaseWait(false), Is.EqualTo(60_000));
            Assert.That(AutonomousBotGroupCoordinator.CorpseReleaseWait(true), Is.EqualTo(120_000));
            Assert.That(AutonomousBotGroupCoordinator.CorpseWalkReleaseWaitMilliseconds,
                Is.GreaterThan(AutonomousBotGroupCoordinator.CorpseWalkMaxMilliseconds), "a walk that runs its full time can still raise");
        }

        // ---- not into the killer (remembered dangers) ----

        [Test]
        public void ACorpseBesideADeathSpotForTheWalkersLevelIsRefused()
        {
            AutonomousPveBossDanger.Record(200, 100_000, 100_000, 30, Utc, "glimmer ardent");
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 95_000, 100_000, 15, 200, 101_000, 100_000, Utc, 0),
                Is.EqualTo("death spot at the corpse"));
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 95_000, 100_000, 30, 200, 101_000, 100_000, Utc, 0),
                Is.Null, "a walker the killer no longer outclasses may go");
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(201, 95_000, 100_000, 15, 201, 101_000, 100_000, Utc, 0),
                Is.Null, "another region is never blocked");
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 95_000, 100_000, 15, 200, 101_000, 100_000,
                Utc + AutonomousPveBossDanger.Lifetime + TimeSpan.FromMinutes(1), 0), Is.Null, "the spot is forgotten");
        }

        [Test]
        public void AStraightWayThroughADeathSpotIsRefusedUnlessTheWalkerStandsInIt()
        {
            AutonomousPveBossDanger.Record(200, 100_000, 100_000, 30, Utc, "glimmer ardent");
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 90_000, 100_000, 15, 200, 110_000, 100_000, Utc, 0),
                Is.EqualTo("death spot on the straight route"));
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 90_000, 90_000, 15, 200, 110_000, 90_000, Utc, 0),
                Is.Null, "a way 10,000 to the side is fine");
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 101_000, 100_000, 15, 200, 110_000, 100_000, Utc, 0),
                Is.Null, "a walker already inside the spot is judged by the corpse alone");
        }

        [Test]
        public void APartyDangerSpotForTheWalkersBandIsRefused()
        {
            AutonomousGroupDangerRetreat.MarkSpot(200, 50_000, 50_000, 20, 0);
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 40_000, 50_000, 18, 200, 52_000, 50_000, Utc, 1_000),
                Is.EqualTo("party danger spot at the corpse"));
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 40_000, 50_000, 21, 200, 52_000, 50_000, Utc, 1_000),
                Is.Null, "a walker above the band may go");
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 40_000, 50_000, 18, 200, 60_000, 50_000, Utc, 1_000),
                Is.Null, "a corpse 10,000 away from the spot");
        }

        [Test]
        public void WithNoMemoryNothingIsRefused() =>
            Assert.That(AutonomousBotGroupCoordinator.CorpseMemoryDanger(200, 0, 0, 10, 200, 5_000, 0, Utc, 0), Is.Null);

        // ---- wiring ----

        private static string Source(params string[] parts)
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())))
                dir = Path.GetDirectoryName(dir);
            Assume.That(dir, Is.Not.Null, "the sources are next to the tests");
            return File.ReadAllText(Path.Combine(new[] { dir, "GameServer" }.Concat(parts).ToArray())).Replace("\r\n", "\n");
        }

        [Test]
        public void TheBrainAsksForTheWalkOnlyBeyondSpellRange()
        {
            string brain = Source("bots", "BotBrain.cs");
            int method = brain.IndexOf("private bool TryResurrectGroupMember()", StringComparison.Ordinal);
            Assert.That(method, Is.GreaterThan(0));
            string body = brain.Substring(method, Math.Min(8_000, brain.Length - method));
            Assert.That(body, Does.Contain("BotBody.IsWithinRadius(member, range) ||\n" +
                "                                 AutonomousBotGroupCoordinator.MayWalkToCorpse(BotBody, member, regrouping))"));
            Assert.That(body, Does.Not.Contain(".Where(member => !regrouping || BotBody.IsWithinRadius(member, range))"),
                "the old regroup filter is replaced, not kept beside it");
        }

        [Test]
        public void PlayersCompanionsAndRaidsKeepTheOldRule()
        {
            string walk = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.CorpseWalk.cs");
            Assert.That(walk, Does.Contain("if (group == null || corpse is not GameBot corpseBot || !walker.IsAutonomousWorldBot || " +
                "walker.IsTemporaryGroupHelper ||\n            walker.IsPlayerLedGroup || AutonomousRealmRaid.GetView(group) != null)\n" +
                "            return !regrouping;"));
            Assert.That(walk, Does.Contain("session.ObjectiveKind != eAutonomousObjectiveKind.GroupPve)\n                return !regrouping;"));
        }

        [Test]
        public void TheCorpseWaitUsesTheWaitRule()
        {
            string coordinator = Source("bots", "autonomous", "AutonomousBotGroupCoordinator.cs");
            int method = coordinator.IndexOf("public static PveCorpseDisposition PveCorpseRecovery(GameBot deadBot)", StringComparison.Ordinal);
            Assert.That(method, Is.GreaterThan(0));
            string body = coordinator.Substring(method, 6_000);
            Assert.That(body, Does.Contain("bool corpseWalk = CorpseWalkUnderWay(session, GameLoop.GameLoopTime);"));
            Assert.That(body, Does.Contain("potentialResurrection ? CorpseReleaseWait(raiseUnderWay) :"));
            Assert.That(body, Does.Contain("regrouping={session.Recovery.IsRegrouping} walk={corpseWalk}"));
        }
    }
}
