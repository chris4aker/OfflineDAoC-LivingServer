using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using DOL.GS;
using NUnit.Framework;
using C = DOL.GS.RaidCurePoison;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// Sweep 7b (owner's PC log, build 9a173f4, 2026-11-18 21:59 to 11-19 01:30): #359's split camps for Summoner Roesia held
    /// half the back line about 1,670 from her while her Summoner Pain still reached both camps (four attempts lost at 89-93%),
    /// Summoner Pain was never cured (0 RAID_CURE_POISON lines), #358's walk-back sent raiders from up to 9,947 away, and a
    /// dragon's REALM_RAID_WIPE printed only "living". Pure rules and a deterministic model: no world tick, combat, spell or
    /// pathing runs. Tests only.
    /// </summary>
    [TestFixture]
    public sealed class UT_RaidSweep7b
    {
        // ---- 1. the split camps are out of play ---------------------------------------------------------------------------

        [Test]
        public void RoesiaHasNoSplitCampsAndTheRaidStandsWithin900OfHerSpawnAsOnB1b6846()
        {
            Assert.Multiple(() =>
            {
                Assert.That(T.AllDungeonRules.Select(r => r.Encounter), Has.No.Member("SummonerPainCamps"));
                string[] hers = T.AllDungeonRules.Where(r => r.Targets.Contains(nameof(SummonerRoesia))).Select(r => r.Encounter).ToArray();
                Assert.That(hers, Is.EqualTo(new[] { "SummonersInsideTether" }), "her only raid rule is b1b6846's tether rule");
                const BindingFlags Any = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                foreach (string gone in new[] { "WatchPainCamps", "ThinkPainCamp", "PainCampAdvice", "AtPainCamp", "PainWatch", "PainPlans" })
                    Assert.That(typeof(T).GetMember(gone, Any), Is.Empty, gone);
                // A back-liner at the old camp post (920 from her spawn) walks in to 900, b1b6846's rule.
                var spawn = new Vector3(34577, 31371, 15998);
                Vector3 post = spawn + new Vector3(920, 0, 0);
                Assert.That(T.SummonerStepIn(post, spawn) is Vector3 inside && Vector3.Distance(inside, spawn) <= T.SummonerInnerRadius, Is.True);
                Assert.That(T.SummonerStepIn(spawn + new Vector3(850, 0, 0), spawn), Is.Null, "within 900 it fights where it stands");
                Assert.That(T.For(nameof(SummonerRoesia))?.FightAtHomeRadius, Is.EqualTo(1_000), "her catalog row is unchanged");
            });
        }

        // ---- 2. Summoner Pain is cured ------------------------------------------------------------------------------------

        private static C.Dotted D(long id, bool own, int health) => new(id, own, health);

        [Test]
        public void TheCureComesFirstForTwoDottedRaidersWhenNobodyOfThePartyIsInDanger()
        {
            Assert.Multiple(() =>
            {
                Assert.That(C.CuresFirst(true, 2, 0, false), Is.True);
                Assert.That(C.CuresFirst(true, 1, 0, false), Is.False, "one dotted raider: the turn's heal as before");
                Assert.That(C.CuresFirst(true, 8, 1, false), Is.False, "a party member under 50%: healed first");
                Assert.That(C.CuresFirst(true, 8, 0, true), Is.False, "a raider a boss mechanic marks: healed first");
                Assert.That(C.CuresFirst(false, 8, 0, false), Is.False, "outside a raid: unchanged");
                Assert.That(C.Which(true, true, 2), Is.EqualTo(C.Cure.Group), "two of its own party: the group cure");
                Assert.That(C.Which(true, true, 1), Is.EqualTo(C.Cure.Single));
                Assert.That(C.Which(false, true, 5), Is.EqualTo(C.Cure.Single), "no group cure in the class: the single one");
                Assert.That(C.Which(true, false, 1), Is.EqualTo(C.Cure.Group));
                Assert.That(C.Which(true, false, 0), Is.EqualTo(C.Cure.None), "the group cure cannot reach another party");
                Assert.That(C.Which(false, false, 3), Is.EqualTo(C.Cure.None), "no cure spell: nothing new is cast");
                Assert.That(C.Decide(true, false, true, true, [D(1, true, 90), D(2, true, 90)], 0, false, 7).Cure, Is.EqualTo(C.Cure.None),
                    "the cure delay is not over");
            });
        }

        [Test]
        public void TheSingleCureTakesTheOwnPartysLowestThenSpreadsOverTheRaid()
        {
            var dotted = new List<C.Dotted> { D(10, false, 60), D(11, true, 80), D(12, true, 70), D(13, false, 55) };
            Assert.That(C.Choose(dotted, 99), Is.EqualTo(2), "its own party's lowest");
            var others = Enumerable.Range(100, 30).Select(i => D(i, false, 70)).ToList();
            Assert.That(Enumerable.Range(500, 12).Select(curer => C.Choose(others, curer)).Distinct().Count(), Is.EqualTo(12),
                "twelve curers take twelve raiders");
            Assert.That(C.Choose([], 1), Is.EqualTo(-1));
        }

        [Test]
        public void ACureIsNeverReAimedByTheRaidsHealReservation()
        {
            Assert.Multiple(() =>
            {
                // Build 9a173f4: a single cure poison (realm target, cast time, no duration) went to the lowest raider under 80%.
                Assert.That(BotGroupSupport.Reserves(true, true, eSpellType.CurePoison, eSpellTarget.REALM, false, 0), Is.False);
                Assert.That(BotGroupSupport.Reserves(true, true, eSpellType.CureMezz, eSpellTarget.REALM, false, 0), Is.False);
                Assert.That(BotGroupSupport.Reserves(true, true, eSpellType.CureDisease, eSpellTarget.REALM, false, 0), Is.False);
                Assert.That(BotGroupSupport.Reserves(true, true, eSpellType.Heal, eSpellTarget.REALM, false, 0), Is.True, "heals as before");
                Assert.That(BotGroupSupport.Reserves(false, true, eSpellType.Heal, eSpellTarget.REALM, false, 0), Is.False);
                Assert.That(BotGroupSupport.Reserves(true, true, eSpellType.Heal, eSpellTarget.REALM, true, 0), Is.False);
            });
        }

        /// <summary>
        /// A deterministic model of one party under Summoner Pain (her script: 150 every 3 s for 36 s, recast every 20 s on a
        /// random raider's 1,000) with one healer turn every half second, CheckHeals' order (the raid cure, the emergency heal
        /// under 40%, the cure branch, the top-up heal) and its 5 s cure delay. The raid around it always has someone under 40%
        /// (the raid-wide patient check's emergency), which on build 9a173f4 blocked every cure.
        /// </summary>
        private sealed class PainModel
        {
            public const int MaxHealth = 1_700, Tick = 150, TickMs = 3_000, PainMs = 36_000, CureDelay = 5_000, TurnMs = 500;
            public const int Heal = 450, EmergencyHeal = 600, CastMs = 2_500;
            public readonly int[] Health;
            public readonly long[] PainUntil, NextTick;
            public long CuredAt = -1, NextCure, BusyUntil;
            public int Cures, Heals, EmergencyHeals;
            public bool Fixed;

            public PainModel(int members, bool fix)
            {
                Health = Enumerable.Repeat(MaxHealth, members).ToArray();
                PainUntil = new long[members];
                NextTick = new long[members];
                Fixed = fix;
            }

            public void Pain(long now, IEnumerable<int> who)
            {
                foreach (int i in who) { PainUntil[i] = now + PainMs; NextTick[i] = now + TickMs; }
            }

            private int Pct(int i) => Health[i] * 100 / MaxHealth;
            public bool Dotted(int i, long now) => PainUntil[i] > now && Health[i] > 0;

            public void Step(long now)
            {
                for (int i = 0; i < Health.Length; i++)
                    if (Dotted(i, now) && now >= NextTick[i]) { Health[i] = Math.Max(0, Health[i] - Tick); NextTick[i] += TickMs; }
                if (now < BusyUntil) return;
                int n = Health.Length;
                int inDanger = Enumerable.Range(0, n).Count(i => Health[i] > 0 && Pct(i) < C.DangerPercent);
                bool partyEmergency = Enumerable.Range(0, n).Any(i => Health[i] > 0 && Pct(i) < 40);
                const bool raidEmergency = true; // somebody in the raid within 1,800 is under 40% (the raid-wide patient)
                var dotted = Enumerable.Range(0, n).Where(i => Dotted(i, now)).Select(i => D(i, true, Pct(i))).ToList();
                var (cure, _) = Fixed ? C.Decide(true, now >= NextCure, true, true, dotted, inDanger, false, 42) : (C.Cure.None, -1);
                if (cure == C.Cure.Group)
                {
                    foreach (C.Dotted d in dotted) PainUntil[d.Id] = 0;
                    Cures++;
                    if (CuredAt < 0) CuredAt = now;
                    NextCure = now + CureDelay;
                    BusyUntil = now + CastMs;
                    return;
                }
                int lowest = Enumerable.Range(0, n).Where(i => Health[i] > 0).OrderBy(i => Health[i]).First();
                if (partyEmergency || raidEmergency)
                {
                    // The emergency heal (the raid's patient may be in another party; this party's lowest when it is the one).
                    if (partyEmergency) { Health[lowest] = Math.Min(MaxHealth, Health[lowest] + EmergencyHeal); EmergencyHeals++; }
                    BusyUntil = now + CastMs;
                    return;
                }
                if (dotted.Count > 0 && now >= NextCure) { foreach (C.Dotted d in dotted) PainUntil[d.Id] = 0; Cures++; NextCure = now + CureDelay; return; }
                if (Pct(lowest) < 80) { Health[lowest] = Math.Min(MaxHealth, Health[lowest] + Heal); Heals++; BusyUntil = now + CastMs; }
            }

            public void Run(long from, long to)
            {
                for (long t = from; t < to; t += TurnMs) Step(t);
            }
        }

        [Test]
        public void APartyUnderSummonerPainIsCuredWithinSecondsAndStillGetsItsEmergencyHeals()
        {
            // Build 9a173f4's order: the raid's emergency always wins, so the party is never cured and dies.
            var before = new PainModel(8, fix: false);
            before.Pain(0, Enumerable.Range(0, 8));
            before.Run(0, 36_000);
            Assert.Multiple(() =>
            {
                Assert.That(before.Cures, Is.Zero, "0 RAID_CURE_POISON all night");
                Assert.That(before.CuredAt, Is.EqualTo(-1));
            });

            // The fix: the whole party carries Pain, nobody is under 50%: the first turn casts the group cure.
            var after = new PainModel(8, fix: true);
            after.Pain(0, Enumerable.Range(0, 8));
            after.Run(0, 3_000);
            Assert.Multiple(() =>
            {
                Assert.That(after.CuredAt, Is.InRange(0, 3_000), "cured within a few seconds");
                Assert.That(Enumerable.Range(0, 8).Any(i => after.Dotted(i, 3_000)), Is.False, "nobody carries it any more");
            });

            // Her next cast (20 s later) is cured too, and over a whole 2-minute fight with a cast every 20 s nobody dies.
            after.Run(3_000, 20_000);
            for (long cast = 20_000; cast < 120_000; cast += 20_000)
            {
                after.Pain(cast, Enumerable.Range(0, 8));
                after.Run(cast, cast + 20_000);
            }
            Assert.Multiple(() =>
            {
                Assert.That(after.Cures, Is.GreaterThanOrEqualTo(6));
                Assert.That(after.Health.All(h => h > 0), Is.True, "nobody of the party fell");
            });

            // Emergency heals stay first: a party member at 35% is healed before anyone is cured.
            var danger = new PainModel(8, fix: true);
            danger.Health[3] = PainModel.MaxHealth * 35 / 100;
            danger.Pain(0, Enumerable.Range(0, 8));
            danger.Step(0);
            Assert.Multiple(() =>
            {
                Assert.That(danger.EmergencyHeals, Is.EqualTo(1), "the emergency heal first");
                Assert.That(danger.Cures, Is.Zero);
            });
            // Once it is out of danger (above 50%) the next free turn cures.
            danger.Run(500, 10_000);
            Assert.That(danger.Cures, Is.GreaterThanOrEqualTo(1));
            Assert.That(danger.CuredAt, Is.LessThan(10_000));
        }

        // ---- 3. #358's walk-back has a distance cap -----------------------------------------------------------------------

        [Test]
        public void OnlyARaiderWithin1500OfTheHomesEdgeWalksBackInside()
        {
            const int RoesiaHome = 1_000;
            Assert.Multiple(() =>
            {
                Assert.That(T.WalkBackReach, Is.EqualTo(1_500));
                Assert.That(T.StandsBackInside(RoesiaHome, 1_400, 900, 200), Is.True, "just outside: walks back in");
                Assert.That(T.StandsBackInside(RoesiaHome, 2_500, 1_900, 200), Is.True, "1,500 out of the edge: still");
                Assert.That(T.StandsBackInside(RoesiaHome, 2_600, 2_000, 200), Is.False, "joining: the waves bring it");
                Assert.That(T.StandsBackInside(RoesiaHome, 9_947, 9_000, 200), Is.False, "Unclespud, build 9a173f4");
                Assert.That(T.StandsBackInside(RoesiaHome, 8_454, 8_000, 200), Is.False, "Lordnugget, build 9a173f4");
                Assert.That(T.StandsBackInside(1_100, 2_134, 1_000, 700), Is.True, "Aidon's healer at 2,134 (b1b6846) still walks in");
            });
        }

        // ---- 4. a dragon's wipe line says who fell -------------------------------------------------------------------------

        [Test]
        public void ADragonWipeLinePrintsTheFallenToo()
        {
            // Build 9a173f4, 23:13:47: REALM_RAID_DRAGON_WIPE engaged=91 fallen=72; REALM_RAID_WIPE said only living=96.
            Assert.That(AutonomousRealmRaid.WipeLine("dragon-albion", "Golestandt", 2, 96, 96, 91, 72, 48),
                Is.EqualTo("REALM_RAID_WIPE event=dragon-albion boss=\"Golestandt\" failures=2 support=96 living=96 " +
                           "engaged=91 fallen=72 minutes=48"));
        }
    }
}
