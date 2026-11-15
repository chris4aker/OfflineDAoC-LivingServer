using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Realm raid flow, from the owner's overnight PC log of 2026-11-14 (build e3b8238, which already had the return waves,
    /// the 70% pull and the "a reset is not a wipe" rule of PR #195, and the raid-size rule of PR #193).
    /// </summary>
    [TestFixture]
    public class UT_RaidFlow
    {
        private const long Minute = 60_000;
        private static readonly EpicEncounterTactics.EarlyPull Commit = EpicEncounterTactics.EarlyPull.Commit,
            Disengage = EpicEncounterTactics.EarlyPull.Disengage, Hold = EpicEncounterTactics.EarlyPull.Hold;

        // ---- 1. an early pull is judged against the living raid --------------------------------------------------------

        [Test]
        public void AHandfulInsideAfterAWipeIsNotTheRaid()
        {
            // The PC log's three early pulls that each lost a fight: near / inside / living raid. None of them shakes off
            // (no tether, or the muster inside it), and before, near*2 >= inside committed all three.
            int Needed(int raid) => RealmRaidDungeonRoute.FinalMusterNeeded(raid, 0);
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(16, 18, 118, false, false, Needed(118)), Is.EqualTo(Hold), "ater skeleton");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(26, 35, 82, false, false, Needed(82)), Is.EqualTo(Hold), "Council Nokkvi");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(24, 32, 103, false, false, Needed(103)), Is.EqualTo(Hold), "the Host");
            // The old rule on the same numbers, for the record.
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(24, 32, false), Is.EqualTo(Commit));
        }

        [Test]
        public void TheWholeRaidInsideStillCommits()
        {
            // Galladoria's trash and Darkness Falls' princes: most of the raid is inside, so a pull is answered by all of it.
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(62, 212, 222, false, false, RealmRaidDungeonRoute.FinalMusterNeeded(222, 0)),
                Is.EqualTo(Commit), "olcasar stonemelder: 212 of 222 inside");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(29, 93, 100, false, false, RealmRaidDungeonRoute.FinalMusterNeeded(100, 0)),
                Is.EqualTo(Commit), "Prince Abdin: 93 of 100 inside");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(52, 60, 100, false, false, 70), Is.EqualTo(Commit),
                "half the raid is already in it: it is the raid's fight");
        }

        [Test]
        public void ABossThePullBackShakesOffIsLeft()
        {
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(10, 90, 100, true, false, 70), Is.EqualTo(Disengage));
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(10, 90, 100, true, true, 70), Is.EqualTo(Commit),
                "pulled back from a moment ago and still on the raid: the raid inside goes in");
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(10, 20, 100, true, true, 70), Is.EqualTo(Hold),
                "...but a handful inside is never sent in");
        }

        [Test]
        public void TheOldThreeArgumentRuleIsUnchanged()
        {
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(31, 87, true), Is.EqualTo(Disengage));
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(31, 87, false), Is.EqualTo(Commit));
            Assert.That(EpicEncounterTactics.ChooseEarlyPull(45, 83, true), Is.EqualTo(Commit));
        }

        [Test]
        public void AHeldSkirmishOfTheFewIsNotTheRaidsAttempt()
        {
            // The Host's 24 all fall while 103 live: a lost skirmish (PR #193's rule), no attempt used.
            Assert.That(RealmRaidDungeonRoute.IsWipe(24, 24, 100, 90), Is.True);
            Assert.That(RealmRaidDungeonRoute.IsWipe(24, 24, 100, 90, 103), Is.False);
        }

        // ---- 2. a wipe is recovered at the way in, not in the town ----------------------------------------------------

        [Test]
        public void ARaiderReleasesAtItsStagingByTheWayIn()
        {
            Assert.That(RealmRaidReturnWave.ReleaseAtStaging, Is.True, "on by default");
            Assert.That(RealmRaidReturnWave.ReleasesAtStaging(true, true, true), Is.True);
            Assert.That(RealmRaidReturnWave.ReleasesAtStaging(false, true, true), Is.False, "switched off: the hub, as before");
            Assert.That(RealmRaidReturnWave.ReleasesAtStaging(true, true, false), Is.False, "a party without a staging post: the hub");
            Assert.That(RealmRaidReturnWave.ReleasesAtStaging(true, false, true), Is.False, "a dragon raid has its own release");
        }

        // ---- staging posts in the frontier (owner's PC, 2026-11-14/15, build 1dc66b6: Pennine Mountains) --------------------

        private static RealmRaidReturnWave.ReleasePost Release(bool frontier, int enemies, bool atStaging = true, bool dungeon = true,
            bool partyStaging = true, bool frontierOn = true, bool raidPost = true) =>
            RealmRaidReturnWave.ChooseReleasePost(atStaging, dungeon, partyStaging, frontierOn, frontier, raidPost, enemies, out _);

        [Test]
        public void AFrontierWayInSendsEveryReleasedRaiderToOneRaidPost()
        {
            // Hall of the Corrupt's way in is in Pennine Mountains: all 59 deaths there came after the first Aidon fight, raiders
            // released at per-party posts picked off by enemy-realm bots (Edor and Armois two and three times in a row).
            Assert.That(RealmRaidReturnWave.FrontierRaidPost, Is.True, "on by default");
            Assert.That(Release(frontier: true, enemies: 0), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Raid));
            RealmRaidReturnWave.ChooseReleasePost(true, true, true, true, true, true, 0, out string reason);
            Assert.That(reason, Is.EqualTo("frontier_staging"));
        }

        [Test]
        public void EnemiesHoldingTheRaidPostSendTheReleaseToTheHub()
        {
            Assert.That(Release(frontier: true, enemies: 1), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Hub), "one enemy bot or pet holds it");
            Assert.That(Release(frontier: true, enemies: 12), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Hub));
            RealmRaidReturnWave.ChooseReleasePost(true, true, true, true, true, true, 3, out string reason);
            Assert.That(reason, Is.EqualTo("enemies_hold_raid_post"));
        }

        [Test]
        public void AWayInOutsideTheFrontierKeepsThePartyPost()
        {
            Assert.That(Release(frontier: false, enemies: 0), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Party));
            Assert.That(Release(frontier: false, enemies: 5), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Party),
                "enemies are only looked for at a frontier raid post");
            Assert.That(Release(frontier: true, enemies: 5, frontierOn: false), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Party),
                "switched off: per-party posts, as before");
            Assert.That(Release(frontier: true, enemies: 0, raidPost: false), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Party));
        }

        [Test]
        public void WithoutAStagingReleaseItIsTheHubAsBefore()
        {
            Assert.That(Release(frontier: true, enemies: 0, atStaging: false), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Hub));
            Assert.That(Release(frontier: true, enemies: 0, dungeon: false), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Hub));
            Assert.That(Release(frontier: false, enemies: 0, partyStaging: false), Is.EqualTo(RealmRaidReturnWave.ReleasePost.Hub));
        }

        [Test]
        public void TheRaidPostIsTheStagingPostNearestTheMiddle()
        {
            Assert.That(RealmRaidReturnWave.RaidPost([]), Is.EqualTo(-1));
            Assert.That(RealmRaidReturnWave.RaidPost([new(5, 5, 0)]), Is.EqualTo(0));
            System.Numerics.Vector3[] ring = [new(-400, 0, 0), new(0, 400, 0), new(40, 20, 0), new(400, 0, 0), new(0, -400, 0)];
            Assert.That(RealmRaidReturnWave.RaidPost(ring), Is.EqualTo(2));
            Assert.That(RealmRaidReturnWave.RaidPostHeldRadius, Is.EqualTo(2_300), "the threat scan radius");
        }

        [Test]
        public void RaidersReleasedAtTheHubGoBackTogether()
        {
            var go = RealmRaidReturnWave.Step.Go;
            var gather = RealmRaidReturnWave.Step.Gather;
            Assert.That(RealmRaidReturnWave.HubGroup(0, 0, 0), Is.EqualTo(gather));
            Assert.That(RealmRaidReturnWave.HubGroup(6, 6, 30_000), Is.EqualTo(gather), "falling together: wait out the grace");
            Assert.That(RealmRaidReturnWave.HubGroup(6, 6, 90_000), Is.EqualTo(go), "all of them there");
            Assert.That(RealmRaidReturnWave.HubGroup(9, 6, 3 * Minute), Is.EqualTo(gather), "three still on their way to the hub");
            Assert.That(RealmRaidReturnWave.HubGroup(20, 8, 4 * Minute), Is.EqualTo(go), "a wave's worth that waited out the patience");
            Assert.That(RealmRaidReturnWave.HubGroup(1, 1, 5 * Minute), Is.EqualTo(gather), "a lone raider waits for company");
            Assert.That(RealmRaidReturnWave.HubGroup(1, 1, 8 * Minute), Is.EqualTo(go), "...until the last call");
        }

        [Test]
        public void ACorpseNobodyCanReachReleasesSoon()
        {
            Assert.That(RealmRaidReturnWave.CorpseWait(true), Is.EqualTo(90_000), "a raider near may still raise it");
            Assert.That(RealmRaidReturnWave.CorpseWait(false), Is.EqualTo(20_000), "a wipe: nobody is left to raise it");
        }

        [Test]
        public void AWipedRaidReleasedAtTheWayInGoesBackAsOneWave()
        {
            // Tuscaran Glacier after Icelord Agmundr (81 of 81 fell): released in the town, the first 7 reached the way in after
            // eight minutes and went alone. Released at the way in, nearly all of them stand there at once.
            Assert.That(RealmRaidReturnWave.Next(81, 0, 75, 0, false), Is.EqualTo(RealmRaidReturnWave.Step.Go));
            Assert.That(RealmRaidReturnWave.Next(40, 41, 40, Minute, false), Is.EqualTo(RealmRaidReturnWave.Step.Gather),
                "half of them still lying inside: the first ones wait for them");
        }
    

        // ---- 3. the wait for formed parties is bounded --------------------------------------------------------------

        [Test]
        public void TheWaitForFormedPartiesHasALimit()
        {
            Assert.That(RealmRaidDungeonRoute.FormedWaitTooLong(0, 60 * Minute), Is.False, "not waiting");
            Assert.That(RealmRaidDungeonRoute.FormedWaitTooLong(Minute, 6 * Minute), Is.False);
            Assert.That(RealmRaidDungeonRoute.FormedWaitTooLong(Minute, 7 * Minute), Is.True,
                "Tuscaran Glacier waited 12 minutes with 83 inside; six is the limit");
            Assert.That(RealmRaidDungeonRoute.FormedWaitMinimumBody, Is.LessThan(RealmRaidDungeonRoute.MinimumRaidersAtTarget),
                "after the limit a party's worth is a main body");
        }
    

        // ---- 5. a muster is never placed in a living boss's aggro ---------------------------------------------------

        private static readonly System.Numerics.Vector3 AgmundrSpawn = new(15037, 50114, 12408);
        private static readonly RealmRaidDungeonRoute.BossDanger Agmundr =
            new("Icelord Agmundr", AgmundrSpawn, 600 + RealmRaidDungeonRoute.UntargetedBossMargin);

        [Test]
        public void TheGlacierHarrowerMusterWasInAgmundrsAggro()
        {
            var muster = new System.Numerics.Vector3(15025, 50189, 12400);
            Assert.That(RealmRaidDungeonRoute.InBossDanger(muster, [Agmundr]), Is.True, "76 units from his spawn");
            Assert.That(RealmRaidDungeonRoute.InBossDanger(new(15350, 48754, 12524), [Agmundr]), Is.False,
                "his own muster, 1,400 out, is clear");
        }

        [Test]
        public void AMusterInABossesAggroMovesBackAlongTheWalk()
        {
            var harrower = new System.Numerics.Vector3(14989, 51581, 12250);
            System.Numerics.Vector3[] walk =
                [new(14989, 51381, 12273), new(15025, 50189, 12400), new(15350, 48754, 12524), new(16000, 47000, 12600)];
            var safe = RealmRaidDungeonRoute.SafeMusterAlong(walk, harrower, RealmRaidDungeonRoute.FinalMusterStandOff, [Agmundr], null);
            Assert.That(safe.HasValue, Is.True);
            Assert.That(RealmRaidDungeonRoute.InBossDanger(safe.Value, [Agmundr]), Is.False);
            Assert.That(System.Numerics.Vector3.Distance(safe.Value, harrower), Is.GreaterThanOrEqualTo(RealmRaidDungeonRoute.FinalMusterStandOff));
            Assert.That(System.Numerics.Vector3.Distance(safe.Value, AgmundrSpawn), Is.LessThan(1_100f),
                "the first clear point of the walk, not the far end of it");
        }

        [Test]
        public void NoClearPointMeansNoMove()
        {
            var boss = new System.Numerics.Vector3(0, 0, 0);
            var everywhere = new RealmRaidDungeonRoute.BossDanger("big", new(2000, 0, 0), 5_000);
            System.Numerics.Vector3[] walk = [new(0, 0, 0), new(3000, 0, 0)];
            Assert.That(RealmRaidDungeonRoute.SafeMusterAlong(walk, boss, 1400, [everywhere], new(3000, 0, 0)), Is.Null);
            Assert.That(RealmRaidDungeonRoute.SafeMusterAlong([], boss, 1400, [], new(3000, 0, 0)), Is.EqualTo(new System.Numerics.Vector3(3000, 0, 0)),
                "the main body, when it is clear");
        }
    

        // ---- 6. raid-wide deaths outside an attempt count ------------------------------------------------------------

        [Test]
        public void TheAgmundrMassacreAtTheMusterIsAWipe()
        {
            // 08:35-08:37: 69 at the harrower's muster, then 56 fell in two minutes with about 13 left standing inside and 58
            // alive in all. No attempt was open, so before this it was not scored (the raid's end line said wipes=1).
            Assert.That(RealmRaidDungeonRoute.IsRouteWipe(56, 13, 58), Is.True);
        }

        [Test]
        public void AFewLossesOnTheWayAreNoWipe()
        {
            Assert.That(RealmRaidDungeonRoute.IsRouteWipe(7, 0, 100), Is.False, "fewer than a party's worth");
            Assert.That(RealmRaidDungeonRoute.IsRouteWipe(10, 100, 200), Is.False, "most of those inside still stand");
            Assert.That(RealmRaidDungeonRoute.IsRouteWipe(20, 10, 300), Is.False,
                "a lost skirmish: a tenth of the raid was there");
        }
    

        // ---- 4. a living boss on the way in is a route objective; epics don't respawn under a raid --------------------

        [Test]
        public void AidonOnTheWalkThroughHallOfTheCorruptIsAGate()
        {
            // Hall of the Corrupt: in from Snowdonia at 31870,31072, out to Summoner's Hall at 26191,37697; Aidon the Archwizard
            // (aggro 600) at 31353,37634,14873, where the parties logged "pass" with no way round.
            var aidon = new System.Numerics.Vector3(31353, 37634, 14873);
            System.Numerics.Vector3[] throughHisRoom =
                [new(31870, 31072, 15846), new(32242, 35521, 15369), new(31353, 37300, 14880), new(26191, 37697, 13476)];
            Assert.That(RealmRaidRouteGate.Blocks(throughHisRoom, aidon, 600), Is.True);
            System.Numerics.Vector3[] farOff = [new(31870, 31072, 15846), new(26191, 31072, 15846), new(26191, 37697, 13476)];
            Assert.That(RealmRaidRouteGate.Blocks(farOff, aidon, 600), Is.False, "a walk 4,000 away");
            System.Numerics.Vector3[] belowHim = [new(30000, 37634, 13000), new(33000, 37634, 13000)];
            Assert.That(RealmRaidRouteGate.Blocks(belowHim, aidon, 600), Is.False, "a floor 1,900 below");
            Assert.That(RealmRaidRouteGate.Blocks(throughHisRoom, aidon, 0), Is.False, "no aggro: he never pulls the walk");
            Assert.That(RealmRaidRouteGate.Blocks([], aidon, 600), Is.False);
        }

        [Test]
        public void AGateIsOnlyLookedForBeforeTheRaidIsThrough()
        {
            Assert.That(RealmRaidRouteGate.RaidStillBeforeIt(0, 64), Is.True, "Summoner's Hall, 10:42: nobody past him");
            Assert.That(RealmRaidRouteGate.RaidStillBeforeIt(40, 64), Is.False, "most of the raid already in its own dungeon");
            Assert.That(RealmRaidRouteGate.On, Is.True, "on by default");
        }

        [Test]
        public void AnEpicWaitsToRespawnWhileTheRaidIsInItsDungeon()
        {
            int hour = 60 * 60_000;
            Assert.That(RealmRaidRouteGate.HoldEpicRespawn, Is.True, "on by default");
            // Galladoria, 01:57:59: Easmarach back an hour after his death while the raid was in the last room.
            Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, hour, true), Is.True);
            Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, hour, false), Is.False, "no raid there: its own timer");
            Assert.That(RealmRaidRouteGate.HoldsRespawn(false, true, true, hour, true), Is.False, "switched off");
            Assert.That(RealmRaidRouteGate.HoldsRespawn(true, false, true, hour, true), Is.False, "ordinary monsters keep theirs");
            Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, false, hour, true), Is.False, "a realm's guard or pet");
            Assert.That(RealmRaidRouteGate.HoldsRespawn(true, true, true, 80_000, true), Is.False,
                "an encounter's quick adds (80 s) are part of its fight");
        }
    }
}
