using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using DOL.GS;
using NUnit.Framework;
using R = DOL.GS.RealmRaidTetherReach;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Where a raid musters, regroups and fights relative to a boss's tether (RealmRaidTetherReach), from the owner's PC log
    /// of 2026-11-16 (build ccba364): Tuscaran Glacier's twins, Summoner Roesia and Marfoirwort. Pure rules: no world tick,
    /// combat or pathing runs. Tests only; nothing here was played in game.
    /// </summary>
    [TestFixture]
    public class UT_RaidTetherMusters12
    {
        private const float OldStandOff = RealmRaidDungeonRoute.FinalMusterStandOff;
        private const long Minute = 60_000;

        private static int FightAtHome(string boss) => T.For(boss)?.FightAtHomeRadius ?? 0;
        private static T.Leash Leash(string boss) => T.Leashes.Single(l => l.Boss == boss);
        private static int Reach(string boss) => R.DamageReach(Leash(boss).TetherRange);

        // ---- the catalog: every encounter's muster against its tether ---------------------------------------------------

        /// <summary>Every catalogued encounter with a tether, and its reach.</summary>
        private static IEnumerable<(string Boss, int Tether, int Home, int Reach)> Tethered()
        {
            foreach (string boss in T.Leashes.Select(l => l.Boss).Distinct())
            {
                int tether = T.Leashes.First(l => l.Boss == boss).TetherRange;
                int home = FightAtHome(boss);
                int reach = R.DamageReach(tether);
                if (reach > 0) yield return (boss, tether, home, reach);
            }
        }

        [Test]
        public void NoEncounterInTheCatalogMustersPastTheReachItCanBeHurtIn()
        {
            var flagged = new List<string>();
            foreach (var (boss, tether, home, reach) in Tethered())
            {
                Assert.That(reach, Is.LessThan(tether > 0 ? tether : int.MaxValue), $"{boss}: the reach is inside its tether");
                foreach (int aggro in new[] { 0, 400, 500, 600, 800, 1000 })
                {
                    float standOff = R.MusterStandOff(OldStandOff, aggro, reach);
                    var spawn = new Vector3(10_000, 10_000, 0);
                    Vector3 muster = spawn + new Vector3(standOff, 0, 0);
                    R.BossReach[] reaches = [new(boss, spawn, reach)];
                    Assert.That(standOff, Is.LessThanOrEqualTo(reach), $"{boss} aggro {aggro}: muster {standOff} within its reach {reach}");
                    Assert.That(standOff, Is.LessThanOrEqualTo(OldStandOff), $"{boss}: never further out than before");
                    Assert.That(R.OutOfReach(muster, reaches), Is.False, $"{boss} aggro {aggro}: the guard accepts its own muster");
                    Assert.That(standOff, Is.LessThanOrEqualTo(Math.Max(reach - R.MusterInset, 1)), $"{boss} aggro {aggro}: never at the reach's edge");
                    if (aggro + R.AggroMargin <= reach - R.MusterInset)
                        Assert.That(standOff, Is.GreaterThanOrEqualTo(aggro + R.AggroMargin), $"{boss} aggro {aggro}: outside its aggro when that fits");
                    if (tether > 0)
                        Assert.That(R.OutOfReach(spawn + new Vector3(tether + 1, 0, 0), reaches), Is.True, $"{boss}: a muster past its tether is refused");
                }
                if (reach < OldStandOff) flagged.Add($"{boss}({tether}/{home}->{(int)R.MusterStandOff(OldStandOff, 600, reach)})");
            }
            TestContext.Out.WriteLine($"Musters that stood past the reach at {OldStandOff} (boss(tether/home->new stand-off at aggro 600)): " +
                                      string.Join(", ", flagged));
            // The PC log's two, and the earlier fixes of the same family (#229 Aidon, the summoners' tether).
            foreach (string boss in new[] { "SummonerRoesia", "SummonerCunovinda", "SummonerLossren", "AidonTheArchwizard",
                         "KingTuscar", "BlackLady" })
                Assert.That(flagged.Any(f => f.StartsWith(boss + "(", StringComparison.Ordinal)), Is.True, $"{boss} was past its reach at 1,400");
        }

        [Test]
        public void ALongTetherKeepsItsMusterAt1400()
        {
            // Untouched where the tether is long: Agmundr (4,500), Warlord Dorinakka (2,000) and no tether at all.
            Assert.That(R.MusterStandOff(OldStandOff, 600, Reach("Agmundr")), Is.EqualTo(OldStandOff));
            Assert.That(R.MusterStandOff(OldStandOff, 600, R.DamageReach(2000)), Is.EqualTo(OldStandOff));
            Assert.That(R.MusterStandOff(OldStandOff, 600, 0), Is.EqualTo(OldStandOff));
            Assert.That(R.DamageReach(0), Is.EqualTo(0), "no tether: no limit");
        }

        [Test]
        public void TheEarthPrimalsMusterStandsInsideHisReachNotOnItsEdge()
        {
            // The PC's zone191.nav (UT_RaidTetherMusterProbe, 2026-11-17): his tether is 890 (reach 590) and his aggro 1,500, so
            // no muster clears his aggro; at the reach itself the walk's point snapped to the floor 590.3 from his spawn and was
            // refused, and the muster was kept unsafe. It now stands 100 inside.
            Assert.That(Leash("EarthPrimal").TetherRange, Is.EqualTo(890));
            int reach = Reach("EarthPrimal");
            float standOff = R.MusterStandOff(OldStandOff, 1500, reach);
            Assert.That(standOff, Is.EqualTo(reach - R.MusterInset));
            var spawn = new Vector3(39727, 62620, 11684);
            R.BossReach[] reaches = [new("Corrupt Primal of Earth", spawn, reach)];
            Assert.That(R.OutOfReach(new(39698.613f, 62030.676f, 11692.941f), reaches), Is.True, "the probe's first muster, 590.3 out");
            Assert.That(R.OutOfReach(spawn + new Vector3(0, -standOff, 30), reaches), Is.False, "a floor 30 lower at the stand-off is in reach");
        }

        // ---- 2. Summoner Roesia ------------------------------------------------------------------------------------------

        [Test]
        public void RoesiasMusterStandsInsideHerHomeAndOutsideHerAggro()
        {
            // musterFromSpawn=1400 tether=1300 (20:30:31); her aggro is 600, fight at home 1,000.
            Assert.That(Leash("SummonerRoesia").TetherRange, Is.EqualTo(1300));
            int reach = Reach("SummonerRoesia");
            Assert.That(reach, Is.EqualTo(1000), "her tether less the margin (her fight-at-home radius too)");
            float standOff = R.MusterStandOff(OldStandOff, 600, reach);
            Assert.That(standOff, Is.EqualTo(900f), "600 aggro + 300, 100 inside her home");
            var spawn = new Vector3(34577, 31371, 15998);
            R.BossReach[] reaches = [new("Summoner Roesia", spawn, reach)];
            Assert.That(R.OutOfReach(new(34487, 32768, 16001), reaches), Is.True, "the 11/16 muster (1,400 out) is refused");
            // Attempt 2 met her at 1,159 from her spawn, outside her home: nobody hits her there (fight at home), so targeting=0.
            Assert.That(T.OutsideHome(FightAtHome("SummonerRoesia"), 1159), Is.True, "why targeting=0: the fight began past her home");
            Assert.That(T.OutsideHome(FightAtHome("SummonerRoesia"), standOff), Is.False, "a raid walking in from her muster starts inside it");
        }

        [Test]
        public void TheRaidNeverPullsBackFromRoesiaToWhereSheCantBeHurt()
        {
            // 20:30:31: near=29 inside=62 raid=64 needed=44 resets=True tether=1300 musterFromSpawn=1400 decision=Disengage.
            bool shakes = T.BreakOffShakesOff(true, 1300, 1400);
            Assert.That(shakes, Is.True, "the old rule: the muster past her tether shakes her off");
            Assert.That(R.PullBackLeavesReach(Reach("SummonerRoesia"), 1400), Is.True);
            bool allowed = shakes && !R.PullBackLeavesReach(Reach("SummonerRoesia"), 1400);
            Assert.That(T.ChooseEarlyPull(29, 62, 64, allowed, false, 44), Is.EqualTo(T.EarlyPull.Commit),
                "62 inside, 44 needed: everyone goes in where she can be hurt");
            Assert.That(T.ChooseEarlyPull(10, 20, 64, allowed, false, 44), Is.EqualTo(T.EarlyPull.Hold),
                "too few: the ones she caught fight her, the rest gather at the muster (inside her reach now)");
            // A rally inside her reach is no pull-back past it.
            Assert.That(R.PullBackLeavesReach(Reach("SummonerRoesia"), 900), Is.False);
            Assert.That(R.PullBackLeavesReach(0, 5000), Is.False, "no reach: the old rule");
        }

        // ---- 1. Tuscaran Glacier's twins ---------------------------------------------------------------------------------

        private static readonly Vector3 KulaSpawn = new(34136, 57202, 12025), KingSpawn = new(34542, 57121, 11881);
        private static readonly Vector3 OldTwinsMuster = new(34116, 55807, 11907);

        private static R.BossReach[] TwinReaches() =>
            [new("Queen Kula", KulaSpawn, Reach("QueenKula")), new("King Tuscar", KingSpawn, Reach("KingTuscar"))];

        [Test]
        public void TheTwinsAreTankedAtHomeInsideBothTethers()
        {
            Assert.That(Leash("QueenKula").TetherRange, Is.EqualTo(1800));
            Assert.That(Leash("KingTuscar").TetherRange, Is.EqualTo(1550));
            Assert.That(FightAtHome("QueenKula"), Is.EqualTo(1400), "past it whoever she chases leads her home");
            Assert.That(FightAtHome("KingTuscar"), Is.EqualTo(1100), "past it whoever he chases leads him home");
            Assert.That(FightAtHome("QueenKula"), Is.LessThan(Leash("QueenKula").TetherRange - R.TetherMargin + 1));
            Assert.That(FightAtHome("KingTuscar"), Is.LessThan(Leash("KingTuscar").TetherRange - R.TetherMargin + 1));
            // Where they were taken on 11/16 (20:22-20:39): outside their homes, so the raid stops hitting them and leads them back.
            Assert.That(T.OutsideHome(FightAtHome("QueenKula"), 1854), Is.True);
            Assert.That(T.OutsideHome(FightAtHome("KingTuscar"), 1654), Is.True);
            Assert.That(T.LeadsHome(FightAtHome("KingTuscar"), 1381, true, true, false), Is.True,
                "the King after a healer at the old muster: the healer leads him home");
            Assert.That(T.For("KingTuscar").After, Is.EqualTo("QueenKula"), "the King is Kula's fight partner (his reach bounds her muster)");
        }

        [Test]
        public void TheTwinsMusterIsRefusedInTheFrostCircleAndPastTheKingsReach()
        {
            R.BossReach[] reaches = TwinReaches();
            // 1,395 from Kula and 1,381 from the King: inside his tether (1,550) but past his reach (1,250).
            Assert.That(Vector3.Distance(OldTwinsMuster, KingSpawn), Is.EqualTo(1381).Within(2));
            Assert.That(R.OutOfReachOf(OldTwinsMuster, reaches).Select(r => r.Name), Is.EqualTo(new[] { "King Tuscar" }));
            // ...and 288 from the middle of her frost circle.
            var frost = new RealmRaidDungeonRoute.BossDanger("Queen Kula", T.KulaFrostPoint, T.KulaFrostMusterRadius);
            Assert.That(RealmRaidDungeonRoute.InBossDanger(OldTwinsMuster, [frost]), Is.True);
            // Kula's stand-off stays 1,400 (her reach is 1,500); the walk south from her front passes the frost circle there.
            float standOff = R.MusterStandOff(OldStandOff, 600, Reach("QueenKula"));
            Assert.That(standOff, Is.EqualTo(1400f));
            var kingAggro = new RealmRaidDungeonRoute.BossDanger("King Tuscar", KingSpawn, 600 + RealmRaidDungeonRoute.UntargetedBossMargin);
            RealmRaidDungeonRoute.BossDanger[] dangers = [frost, kingAggro];
            Vector3[] walk = [new(34136, 57002, 11985), new(34120, 55000, 11900), new(34110, 53500, 11890)];
            Assert.That(RealmRaidDungeonRoute.SafeMusterAlong(walk, KulaSpawn, standOff, dangers, null, null, reaches), Is.Null,
                "no point of the walk is clear and in both reaches");
            Assert.That(RealmRaidDungeonRoute.SafeMusterAlong(walk, KulaSpawn, standOff, dangers, null), Is.Not.Null,
                "without the reaches the guard took a point past the King's (the old muster's way)");
            // A point around her, clear of the circle and the King's aggro and in both reaches (the ring fallback).
            Vector3? ring = R.NearestAccepted(R.RingCandidates(KulaSpawn, 900, 1400), OldTwinsMuster, null,
                p => !RealmRaidDungeonRoute.InBossDanger(p, dangers) && !R.OutOfReach(p, reaches) &&
                     Vector3.Distance(p, KulaSpawn) >= 600 + R.AggroMargin);
            Assert.That(ring, Is.Not.Null);
            Assert.That(Vector3.Distance(ring.Value, KingSpawn), Is.LessThanOrEqualTo(Reach("KingTuscar")));
            Assert.That(Vector3.Distance(ring.Value, KulaSpawn), Is.LessThanOrEqualTo(Reach("QueenKula")));
            Assert.That(Vector3.Distance(ring.Value, T.KulaFrostPoint), Is.GreaterThan(T.KulaFrostMusterRadius));
        }

        [Test]
        public void TwinsWalkingIntoTheRegroupAreAnsweredAsAnAttempt()
        {
            float threat = 600 + R.AggroMargin;
            // 20:38:17: Kula at 34427,55889 fighting the raid, 315 from the regroup point; the raid held at 23 of 79.
            float kulaToRegroup = Vector3.Distance(new(34427, 55889, 11892), OldTwinsMuster);
            Assert.That(R.RegroupThreatened(true, true, 48, kulaToRegroup, threat), Is.True);
            // At home (20:36:15, 1,395 away) the few she caught fight her and the regroup goes on.
            Assert.That(R.RegroupThreatened(true, true, 8, Vector3.Distance(new(34131, 57184, 12024), OldTwinsMuster), threat), Is.False);
            Assert.That(R.RegroupThreatened(false, true, 48, kulaToRegroup, threat), Is.False, "not regrouping: the early pull as before");
            Assert.That(R.RegroupThreatened(true, false, 48, kulaToRegroup, threat), Is.False, "out of combat: nothing to answer");
            Assert.That(R.RegroupThreatened(true, true, 0, kulaToRegroup, threat), Is.False, "fighting nobody of the raid");
            // The early pull then: 64 of 99 at the regroup, 70% needed: everyone commits; with fewer, the caught ones' attempt.
            Assert.That(T.ChooseEarlyPull(48, 64, 99, false, false, RealmRaidDungeonRoute.FinalMusterNeeded(99, 12 * Minute)),
                Is.Not.EqualTo(T.EarlyPull.Disengage));
        }

        // ---- 3. Marfoirwort ----------------------------------------------------------------------------------------------

        [Test]
        public void MarfoirwortsAttemptStaysOpenWhileRaidersFightHim()
        {
            // 17:45:48: "no damage for five minutes" with him in combat (lowest 79%, 33 of 35 fell).
            Assert.That(RealmRaidDragonFight.AttemptOver(true, 13 * Minute, 5 * Minute), Is.True, "the old rule");
            Assert.That(RealmRaidDragonFight.AttemptOver(true, 13 * Minute, 5 * Minute, openWorld: true, raidersAtBoss: 2), Is.False);
            Assert.That(RealmRaidDragonFight.AttemptOver(true, 13 * Minute, 5 * Minute, openWorld: true, raidersAtBoss: 0), Is.True,
                "nobody of the raid at him: the stall ends it as before");
            Assert.That(RealmRaidDragonFight.AttemptOver(false, 13 * Minute, 5 * Minute, openWorld: true, raidersAtBoss: 2), Is.True,
                "out of combat: over");
            Assert.That(RealmRaidDragonFight.AttemptOver(true, 13 * Minute, 5 * Minute, openWorld: false, raidersAtBoss: 2), Is.True,
                "a dragon keeps its rule (won on earlier builds)");
        }

        [Test]
        public void AnOpenWorldFightHeldOpenEndsAsOneWipeWhenHeLeavesCombat()
        {
            var fight = new RealmRaidDragonFight("Marfoirwort", openWorld: true);
            var raiders = Enumerable.Range(1, 40).Select(i => new RealmRaidDragonFight.Raider(i, true, true, false)).ToList();
            var boss = new RealmRaidDragonFight.Dragon(true, true, false, 100, 100_000);
            fight.Engage(raiders.Select(r => r.Id), boss, 0);
            fight.Advance(raiders, boss with { HealthPercent = 79, Health = 79_000 }, Minute);
            // Five minutes and more without damage, 33 fallen, two still on him: the attempt is still open.
            var hurt = raiders.Select(r => r.Id <= 33 ? r with { Standing = false, AtDragon = false } : r).ToList();
            hurt = hurt.Select(r => r.Id is > 33 and < 39 ? r with { AtDragon = false } : r).ToList();
            fight.Advance(hurt, boss with { HealthPercent = 95, Health = 95_000 }, 8 * Minute);
            Assert.That(fight.Regrouping, Is.False);
            Assert.That(fight.FailedAttempts, Is.EqualTo(0));
            // Raiders walking back join the fight and are part of the attempt; he leaves combat when the last falls.
            fight.Advance(hurt, boss with { InCombat = false, HealthPercent = 100, Health = 100_000 }, 12 * Minute);
            Assert.That(fight.FailedAttempts, Is.EqualTo(1), "one wipe, counted once");
            Assert.That(fight.Regrouping, Is.True);
        }

        [Test]
        public void MarfoirwortsRegroupRingIsOutOfHisReach()
        {
            // Tether 4,500, aggro 800, roam 200: 103 died on the 3,600 ring in 36 minutes; he was 8,564 out.
            Assert.That(Leash("Marfoirwort").TetherRange, Is.EqualTo(4500));
            float inner = R.OpenWorldStagingInner(4500, 800, 200);
            Assert.That(inner, Is.EqualTo(4500 + 800 + 200 + R.AggroMargin));
            Assert.That(inner, Is.GreaterThan(R.DefaultStagingInner));
            // Where they died "Fighting Marfoirwort" (18:03-18:17, 3,500-4,100 from his spawn) is inside it.
            var spawn = new Vector3(293831, 283911, 2695);
            Assert.That(Vector3.Distance(new(293205, 287456, 2463), spawn), Is.LessThan(inner));
            // The Green Knight's 10,000 tether: no ring is out of his reach, the default stays; a dragon-like short reach too.
            Assert.That(R.OpenWorldStagingInner(10_000, 600, 0), Is.EqualTo(R.DefaultStagingInner));
            Assert.That(R.OpenWorldStagingInner(1_700, 600, 0), Is.EqualTo(R.DefaultStagingInner), "Evern: the 3,600 ring is already clear");
            Assert.That(R.OpenWorldStagingInner(0, 800, 0), Is.EqualTo(R.DefaultStagingInner));
        }

        [Test]
        public void ARegroupPostStaysOutOfTheRoamingMonstersReach()
        {
            R.RingMonster[] monsters = [new(new(0, 0, 0), 500, 400), new(new(5000, 0, 0), 0, 1000)];
            Assert.That(R.ClearOfMonsters(new(1000, 0, 0), monsters), Is.False, "within 500 + 400 + 300");
            Assert.That(R.ClearOfMonsters(new(1300, 0, 0), monsters), Is.True);
            Assert.That(R.ClearOfMonsters(new(5000, 0, 0), monsters), Is.True, "a monster with no aggro is no danger");
            Assert.That(R.ClearOfMonsters(new(0, 0, 0), []), Is.True);
        }

        [Test]
        public void RingCandidatesCoverTheBand()
        {
            var c = R.RingCandidates(Vector3.Zero, 900, 1100, 100, 12).ToList();
            Assert.That(c, Has.Count.EqualTo(36));
            Assert.That(c.Min(p => p.Length()), Is.EqualTo(900).Within(0.5));
            Assert.That(c.Max(p => p.Length()), Is.EqualTo(1100).Within(0.5));
            Assert.That(R.RingCandidates(Vector3.Zero, 1200, 1100), Is.Empty);
            Vector3? west = R.NearestAccepted(c, new(1000, 0, 0), null, p => p.X < 0);
            Assert.That(west.HasValue && west.Value.X < 0, Is.True);
            Assert.That(R.NearestAccepted(c, new(1000, 0, 0), null, _ => false), Is.Null);
        }
    }
}
