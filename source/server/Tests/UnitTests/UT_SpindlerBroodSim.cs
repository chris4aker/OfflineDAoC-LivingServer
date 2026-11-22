using System;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;
using T = DOL.GS.EpicEncounterTactics;

namespace DOL.UnitTests
{
    /// <summary>
    /// #408 (Galladoria: the bot raid loses at Spindler Broodmother). A deterministic simulation of her newborns against the
    /// raid's add parties, built on her script (SpindlerBroodmotherBrain.SpawnSplinder: the first 10 s after she fights,
    /// then 1 or 2, the loop drawing its bound again each pass, and the next 10 s after a 15-25 s reset; SBAdds 8,000 health;
    /// her 100,000) and the tactic's own numbers (SpindlerAddPartyCount, SpindlerRaiderDamagePerMinute,
    /// SpindlerFightersPerParty). Her damage taken per raider is calibrated on the one kill with a known size: the raid the
    /// game opened itself on build ccba364 (128 present, 16 parties, 7 add parties at the old 45%) killed her in 4.0 minutes.
    /// <para>The logs: every loss there was a sweep raid at the period size (100 raiders, 13 parties: two wipes on 88b6ab3,
    /// lost on 9a173f4) and PC log e3b8238's wipes came with up to 44 newborns alive. At the old 45% share the 13-party raid
    /// sends 5 add parties, a kill rate of 62% of what she brings, so her newborns gather through her fight; at 60% it sends
    /// 7 (88%, the share of the 128 raiders who won), and from 14 parties the full 8.</para>
    /// <para>Not modelled: her mesmerize (300 around one raider, 60 s, about every half minute), deaths, heals, her teleport,
    /// the area spells on the clump and the burn below 25%. This measures the add parties' sizing only. Tests only: nothing
    /// here is seen in a PC log.</para>
    /// </summary>
    [TestFixture]
    public sealed class UT_SpindlerBroodSim
    {
        private const int CcbaParties = 16, CcbaAddParties = 7;
        private const double CcbaKillMinutes = 4.0;

        private static int MaxHealthOf<TNpc>() where TNpc : GameNPC => ((TNpc)RuntimeHelpers.GetUninitializedObject(typeof(TNpc))).MaxHealth;

        /// <summary>What one raider of a party on her deals her a minute, from the ccba364 kill.</summary>
        private static double BossPerRaiderMinute() =>
            MaxHealthOf<SpindlerBroodmother>() / (CcbaKillMinutes * (CcbaParties - CcbaAddParties) * T.SpindlerFightersPerParty);

        public readonly record struct Result(double KillMinutes, int Spawned, double AliveAtDeath, double Peak);

        /// <summary>One fight with <paramref name="parties"/> parties, <paramref name="addParties"/> of them on her newborns.</summary>
        public static Result Fight(int parties, int addParties, int seed)
        {
            var rng = new Random(seed);
            double her = MaxHealthOf<SpindlerBroodmother>();
            double newborn = MaxHealthOf<SBAdds>();
            double onHer = (parties - addParties) * T.SpindlerFightersPerParty * BossPerRaiderMinute() / 60.0;
            double onAdds = addParties * T.SpindlerFightersPerParty * T.SpindlerRaiderDamagePerMinute / 60.0;
            double pool = 0, peak = 0; // the newborns' health alive
            int spawned = 0;
            double nextSpawn = 10;
            int t = 0;
            for (; her > 0 && t < 60 * 60; t++)
            {
                if (t >= nextSpawn)
                {
                    int count = 0;
                    for (int i = 0; i < rng.Next(1, 3); i++) count++; // for (i < Util.Random(1, 2)): the bound drawn each pass
                    spawned += count;
                    pool += count * newborn;
                    nextSpawn = t + rng.Next(15, 26) + 10;
                }
                pool = Math.Max(0, pool - onAdds);
                her -= onHer;
                peak = Math.Max(peak, pool / newborn);
            }
            return new(t / 60.0, spawned, pool / newborn, peak);
        }

        private static Result Mean(int parties, int addParties)
        {
            var runs = Enumerable.Range(1, 200).Select(s => Fight(parties, addParties, s)).ToArray();
            return new(runs.Average(r => r.KillMinutes), (int)runs.Average(r => r.Spawned), runs.Average(r => r.AliveAtDeath), runs.Average(r => r.Peak));
        }

        /// <summary>The add parties' kill rate over her spawn rate.</summary>
        private static double Share(int addParties) =>
            addParties * T.SpindlerFightersPerParty * T.SpindlerRaiderDamagePerMinute /
            (T.SpindlerNewbornsPerMinute * T.SpindlerNewbornHealth);

        [Test]
        public void TheSimulationUsesHerOwnHealthAndHerNewbornsHealth()
        {
            Assert.That(MaxHealthOf<SpindlerBroodmother>(), Is.EqualTo(100_000));
            Assert.That(MaxHealthOf<SBAdds>(), Is.EqualTo(T.SpindlerNewbornHealth));
            // Her spawn rate as the script draws it: 1.5 every 30 s on average, the tactic's 3 a minute.
            var fight = Fight(16, 0, 1);
            double perMinute = Enumerable.Range(1, 200).Select(s => Fight(1, 0, s)).Average(r => r.Spawned / (r.KillMinutes - 10 / 60.0));
            Assert.That(perMinute, Is.EqualTo(T.SpindlerNewbornsPerMinute).Within(0.2));
            Assert.That(fight.Spawned, Is.GreaterThan(0));
            // The calibration gives the ccba364 kill back.
            Assert.That(Mean(CcbaParties, CcbaAddParties).KillMinutes, Is.EqualTo(CcbaKillMinutes).Within(0.05));
        }

        [Test]
        public void TheSweepsRaidSentTooFewAddPartiesAndTheNewShareMatchesTheRaidThatWon()
        {
            // The old share, as the code had it (45% of the parties, at most the 8 her spawn rate asks for).
            static int Old(int parties) => Math.Clamp(8, Math.Min(parties >= 4 ? 2 : 1, Math.Max(1, parties * 45 / 100)), Math.Max(1, parties * 45 / 100));
            double won = Share(Old(CcbaParties));
            foreach (int parties in new[] { 13, 14, 15, 16 })
            {
                int before = Old(parties), after = T.SpindlerAddPartyCount(parties);
                Result b = Mean(parties, before), a = Mean(parties, after);
                TestContext.Out.WriteLine($"{parties} parties: before {before} add parties ({Share(before):P0} of her spawn rate), " +
                                          $"she dies in {b.KillMinutes:0.0} min with {b.AliveAtDeath:0.0} newborns alive (peak {b.Peak:0.0}); " +
                                          $"after {after} ({Share(after):P0}): {a.KillMinutes:0.0} min, {a.AliveAtDeath:0.0} alive (peak {a.Peak:0.0})");
                Assert.That(Share(after), Is.GreaterThanOrEqualTo(won), $"{parties} parties: at least the share of the raid that won");
                Assert.That(a.AliveAtDeath, Is.LessThan(b.AliveAtDeath + 1e-9), $"{parties} parties: no more newborns left at her death");
                Assert.That(a.KillMinutes, Is.LessThan(10), $"{parties} parties: she still dies well inside the raid's time");
            }
            Assert.That(Share(Old(13)), Is.LessThan(0.65), "the sweep's 13 parties: 5 add parties, 62%");
            Assert.That(Mean(13, Old(13)).AliveAtDeath, Is.GreaterThan(Mean(CcbaParties, Old(CcbaParties)).AliveAtDeath),
                "the sweep's raid ended her fight with more newborns alive than the raid that won");
        }
    }
}
