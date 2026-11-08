using System;
using System.Collections.Generic;
using System.Linq;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Raid discipline (owner 2026-11-08): Xanxicar's stomp dodge had become perfect (his 40% and 20% stomps killed nobody in
    /// duo11). Now the raid's caller calls it a variable time before it lands, each raider reacts after its own delay from its
    /// personality and sometimes lapses, and all of it improves as the realm learns the boss. The owner's bands: deaths at each
    /// stomp, as a share of the raiders in his blast radius, about 25-35% unlearned and 5-15% learned.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_RaidDiscipline
    {
        private const string Mechanic = "Xanxicar/blast";
        private static readonly int[] Stomps = [80, 60, 40, 20];

        [TearDown]
        public void Clean() => RaidDiscipline.FamiliarityOverride = null;

        // ---- familiarity ------------------------------------------------------------------------------------------------

        [Test]
        public void AFreshWorldIsUnlearnedAndAttemptsAndWinsTeachTheRealm()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidDiscipline.Familiarity(0, 0), Is.EqualTo(0));
                Assert.That(RaidDiscipline.Familiarity([]), Is.EqualTo(0));
                Assert.That(RaidDiscipline.Familiarity(3, 0), Is.EqualTo(0.39).Within(0.01), "three losses");
                Assert.That(RaidDiscipline.Familiarity(5, 2), Is.EqualTo(0.78).Within(0.01), "three losses and two wins");
                Assert.That(RaidDiscipline.Familiarity(10, 6), Is.GreaterThan(0.97));
                Assert.That(RaidDiscipline.Familiarity(400, 400), Is.LessThanOrEqualTo(1));
                for (int a = 0; a < 20; a++)
                for (int w = 0; w <= a; w++)
                {
                    Assert.That(RaidDiscipline.Familiarity(a + 1, w), Is.GreaterThan(RaidDiscipline.Familiarity(a, w)), "an attempt teaches");
                    if (w < a) Assert.That(RaidDiscipline.Familiarity(a, w + 1), Is.GreaterThan(RaidDiscipline.Familiarity(a, w)), "a win teaches more");
                }
                // From the record: only wins and losses count, as for the raid's size.
                RaidResult[] record = ["Boss defeated", "Interrupted", "Defeated", "Timed out", new RaidResult("Boss defeated", true)];
                Assert.That(RaidDiscipline.Familiarity(record), Is.EqualTo(RaidDiscipline.Familiarity(4, 2)).Within(1e-12));
            });
        }

        [Test]
        public void TheRecordAndThePlayTestOverrideGiveTheRaidsFamiliarity()
        {
            RealmRaidRecord.ResetForTests();
            Assert.That(RaidDiscipline.FamiliarityOf("dungeon-albion-crystal"), Is.EqualTo(0), "no record: fully unlearned");
            RealmRaidRecord.SetForTests("dungeon-albion-crystal", "Defeated", "Defeated", "Boss defeated");
            Assert.Multiple(() =>
            {
                Assert.That(RaidDiscipline.FamiliarityOf("dungeon-albion-crystal"), Is.EqualTo(RaidDiscipline.Familiarity(3, 1)).Within(1e-12));
                Assert.That(RaidDiscipline.SeedOf("dungeon-albion-crystal"), Is.EqualTo(3), "a new raid rolls afresh");
                Assert.That(AutonomousRealmRaid.RaidIdForBoss("Xanxicar"), Is.EqualTo("dungeon-albion-crystal"));
                RaidDiscipline.FamiliarityOverride = 1;
                Assert.That(RaidDiscipline.FamiliarityOf("dungeon-albion-crystal"), Is.EqualTo(1));
                Assert.That(DOL.GS.Commands.EpicPlaytestCommandHandler.SetFamiliarity("0"), Does.Contain("0.00"));
                Assert.That(RaidDiscipline.FamiliarityOf("dungeon-albion-crystal"), Is.EqualTo(0));
                Assert.That(DOL.GS.Commands.EpicPlaytestCommandHandler.SetFamiliarity("2"), Does.Contain("not a number"));
                DOL.GS.Commands.EpicPlaytestCommandHandler.SetFamiliarity("off");
                Assert.That(RaidDiscipline.FamiliarityOverride, Is.Null);
            });
            RealmRaidRecord.ResetForTests();
        }

        // ---- the call ---------------------------------------------------------------------------------------------------

        [Test]
        public void TheCallComesEarlierAndLessOftenLateAsTheRealmLearns()
        {
            RaidDiscipline.Call[] Calls(double f) => Enumerable.Range(0, 500)
                .SelectMany(seed => Stomps.Select(s => RaidDiscipline.CallFor(1000 + seed % 7, Mechanic, s, seed, f))).ToArray();
            RaidDiscipline.Call[] unlearned = Calls(0), learned = Calls(1);
            TestContext.WriteLine($"late: {unlearned.Count(c => c.Late) / (double)unlearned.Length:P1} unlearned, " +
                                  $"{learned.Count(c => c.Late) / (double)learned.Length:P1} learned; mean lead " +
                                  $"{unlearned.Average(c => c.LeadSeconds):0.0} s / {learned.Average(c => c.LeadSeconds):0.0} s");
            Assert.Multiple(() =>
            {
                Assert.That(unlearned.Count(c => c.Late) / (double)unlearned.Length, Is.InRange(0.11, 0.19));
                Assert.That(learned.Count(c => c.Late) / (double)learned.Length, Is.InRange(0.03, 0.08));
                Assert.That(unlearned.Where(c => !c.Late).Select(c => c.LeadSeconds), Is.All.InRange(8.0, 10.5));
                Assert.That(RaidDiscipline.CallLeadUnlearnedMin, Is.GreaterThanOrEqualTo(RaidDiscipline.LateCallLeadMax), "on time starts where late ends");
                Assert.That(learned.Where(c => !c.Late).Select(c => c.LeadSeconds), Is.All.InRange(9.5, 13.5));
                Assert.That(unlearned.Concat(learned).Where(c => c.Late).Select(c => c.LeadSeconds), Is.All.InRange(RaidDiscipline.LateCallLeadMin, RaidDiscipline.LateCallLeadMax));
                Assert.That(unlearned.Average(c => c.LeadSeconds), Is.LessThan(learned.Average(c => c.LeadSeconds) - 2));
                // Deterministic per (caller, stomp, seed).
                Assert.That(RaidDiscipline.CallFor(7, Mechanic, 60, 3, 0.4), Is.EqualTo(RaidDiscipline.CallFor(7, Mechanic, 60, 3, 0.4)));
                Assert.That(Enumerable.Range(0, 50).Select(s => RaidDiscipline.CallFor(7, Mechanic, 60, s, 0.4).LeadSeconds).Distinct().Count(),
                    Is.GreaterThan(40), "another raid, another call");
            });
        }

        [Test]
        public void TheCallerTalksLikeA2003RaidLeader()
        {
            var lines = new HashSet<string>();
            for (int seed = 0; seed < 200; seed++)
            foreach (int s in Stomps)
            {
                var call = RaidDiscipline.CallFor(5, Mechanic, s, seed, seed % 2);
                lines.Add(RaidDiscipline.OutLine(call, s, 5, Mechanic, s, seed, seed % 2));
                lines.Add(RaidDiscipline.BackInLine(5, Mechanic, s, seed));
            }
            Assert.Multiple(() =>
            {
                Assert.That(lines, Has.Member("STOMP INC, everyone OUT"));
                Assert.That(lines, Has.Some.Contains("back in"));
                Assert.That(lines.Any(l => l.Contains("80%") || l.Contains("60 ") || l.Contains("at 40")), Is.True, "a learned caller names the health");
                // No internal vocabulary.
                foreach (string line in lines)
                    Assert.That(line, Does.Not.Contain("blast").IgnoreCase.And.Not.Contain("threshold").IgnoreCase
                        .And.Not.Contain("familiar").IgnoreCase.And.Not.Contain("{").And.Length.LessThan(40), line);
                Assert.That(RaidDiscipline.OutLine(new RaidDiscipline.Call(3, true), 80, 5, Mechanic, 80, 1, 0),
                    Is.AnyOf(RaidDiscipline.LateLines), "a late call sounds late");
                Assert.That(RaidDiscipline.OutLine(new RaidDiscipline.Call(8, false), 80, 5, Mechanic, 80, 1, 0),
                    Is.AnyOf(RaidDiscipline.OutLines), "an unlearned caller does not know the health it comes at");
            });
        }

        // ---- each raider ------------------------------------------------------------------------------------------------

        [Test]
        public void ReactionsRunHalfASecondToFourAndQuickenAsTheRealmLearns()
        {
            var unlearned = new List<double>();
            var learned = new List<double>();
            for (long id = 1; id <= 3000; id++)
            {
                var traits = AutonomousBotPersonality.For(id).Traits;
                var u = RaidDiscipline.ReactionFor(id, traits, RaidDiscipline.Role.Melee, Mechanic, 80, 0, 0);
                var l = RaidDiscipline.ReactionFor(id, traits, RaidDiscipline.Role.Melee, Mechanic, 80, 0, 1);
                if (u.Lapse == RaidDiscipline.Lapse.None) unlearned.Add(u.DelaySeconds);
                if (l.Lapse == RaidDiscipline.Lapse.None) learned.Add(l.DelaySeconds);
            }
            TestContext.WriteLine($"delay unlearned {unlearned.Min():0.00}-{unlearned.Max():0.00} mean {unlearned.Average():0.00}; " +
                                  $"learned {learned.Min():0.00}-{learned.Max():0.00} mean {learned.Average():0.00}");
            Assert.Multiple(() =>
            {
                Assert.That(unlearned, Is.All.InRange(0.5, 4.0));
                Assert.That(learned, Is.All.InRange(0.5, 2.5));
                Assert.That(unlearned.Max() - unlearned.Min(), Is.GreaterThan(2.5), "a real spread");
                Assert.That(learned.Average(), Is.LessThan(unlearned.Average() - 0.5));
            });
        }

        [Test]
        public void RiskTakersAndTheImpatientAreSlowerAndRiskTakersAndHealersLapseMore()
        {
            double Delay(int risk, int patience) => Enumerable.Range(1, 2000).Select(id =>
                RaidDiscipline.ReactionFor(id, new(5, risk, 5, patience, 5), RaidDiscipline.Role.Melee, Mechanic, 60, 1, 0))
                .Where(r => r.Lapse == RaidDiscipline.Lapse.None).Average(r => r.DelaySeconds);
            double Lapses(int risk, RaidDiscipline.Role role) => Enumerable.Range(1, 6000).Count(id =>
                RaidDiscipline.ReactionFor(id, new(5, risk, 5, 5, 5), role, Mechanic, 60, 1, 0).Lapse != RaidDiscipline.Lapse.None) / 6000.0;
            Assert.Multiple(() =>
            {
                Assert.That(Delay(9, 0), Is.GreaterThan(Delay(0, 9) + 1.4), "a daredevil versus a careful, patient raider");
                Assert.That(Delay(9, 9), Is.GreaterThan(Delay(0, 9)));
                Assert.That(Delay(0, 0), Is.GreaterThan(Delay(0, 9)));
                Assert.That(Lapses(9, RaidDiscipline.Role.Melee), Is.GreaterThan(Lapses(0, RaidDiscipline.Role.Melee) * 1.8));
                Assert.That(Lapses(5, RaidDiscipline.Role.Healer), Is.GreaterThan(Lapses(5, RaidDiscipline.Role.Melee) * 1.3));
                Assert.That(Lapses(5, RaidDiscipline.Role.Melee), Is.InRange(0.12, 0.20), "unlearned: about one in six");
                var r = Enumerable.Range(1, 3000).Select(id => RaidDiscipline.ReactionFor(id, new(5, 9, 5, 5, 5), RaidDiscipline.Role.Healer, Mechanic, 60, 1, 0))
                    .Where(x => x.Lapse != RaidDiscipline.Lapse.None).ToArray();
                Assert.That(r.Count(x => x.Lapse == RaidDiscipline.Lapse.Stays) / (double)r.Length, Is.InRange(0.3, 0.5));
                Assert.That(r.Select(x => x.Reason), Is.All.StartsWith("finishing a heal on the tank"));
                Assert.That(r.Where(x => x.Lapse == RaidDiscipline.Lapse.Stays).Select(x => x.Leaves), Is.All.False);
                // Deterministic per (raider, stomp, seed); another stomp or raid rolls again.
                Assert.That(RaidDiscipline.ReactionFor(42, new(5, 5, 5, 5, 5), RaidDiscipline.Role.Caster, Mechanic, 40, 2, 0.3),
                    Is.EqualTo(RaidDiscipline.ReactionFor(42, new(5, 5, 5, 5, 5), RaidDiscipline.Role.Caster, Mechanic, 40, 2, 0.3)));
                Assert.That(RaidDiscipline.ReactionFor(42, new(5, 5, 5, 5, 5), RaidDiscipline.Role.Caster, Mechanic, 40, 2, 0.3).DelaySeconds,
                    Is.Not.EqualTo(RaidDiscipline.ReactionFor(42, new(5, 5, 5, 5, 5), RaidDiscipline.Role.Caster, Mechanic, 20, 2, 0.3).DelaySeconds));
            });
        }

        // ---- the model: who gets out of the stomp in time ---------------------------------------------------------------

        // A simple model of one stomp, fitted to the container runs duo16 and duo17 (Xanxicar, 2026-11-08), not a measurement.
        // The stomp hits everyone within 2,500 of his target when his cast ends, falling off with distance; a raider dies if
        // it is still inside its role's lethal distance then (melee 400, healers 1,100, casters and archers 1,200, each
        // give or take 15%: the melee classes have the health to take it near him). At the call 35% of the raiders in range
        // stand at melee range (40-160 from him), 20% at 160-900 and 45% at 900-2,000 (EPIC_BLAST_REACT dist). Once a raider
        // goes it gets out at 150 a second, half a second after its reaction delay (its next brain turn and its path). The
        // party he fights stays in: 5 of its 8 die (duo17: 5, 5, 0, 5). Fitted: 47% deaths at a 4.5 s lead unlearned
        // (duo16: 30-42%), 29% at 7.2 s learned (duo17: 24% and 47%); from melee range it takes 8-9 s to get clear.
        private const double RunSpeed = 150, TurnSeconds = 0.5;
        private static readonly Dictionary<RaidDiscipline.Role, double> Lethal = new()
        {
            [RaidDiscipline.Role.Melee] = 400, [RaidDiscipline.Role.Healer] = 1100, [RaidDiscipline.Role.Caster] = 1200,
        };

        private sealed record StompResult(int Exposed, int Died, int Within, int WithinDied, bool LateCall);

        private static StompResult Stomp(double familiarity, int seed, int stomp, int raiders = 130, double? lead = null)
        {
            var rng = new Random(seed * 7919 + stomp);
            var call = RaidDiscipline.CallFor(seed % 13, Mechanic, stomp, seed, familiarity);
            if (lead is double fixedLead) call = new RaidDiscipline.Call(fixedLead, false);
            int died = 5, within = 8;
            for (int i = 0; i < raiders - 8; i++)
            {
                long id = seed * 1000L + i + 1;
                double pick = rng.NextDouble();
                var role = pick < 0.33 ? RaidDiscipline.Role.Melee : pick < 0.67 ? RaidDiscipline.Role.Caster : RaidDiscipline.Role.Healer;
                double where = rng.NextDouble();
                double start = where < 0.35 ? 40 + rng.NextDouble() * 120 : where < 0.55 ? 160 + rng.NextDouble() * 740 : 900 + rng.NextDouble() * 1100;
                var reaction = RaidDiscipline.ReactionFor(id, AutonomousBotPersonality.For(id).Traits, role, Mechanic, stomp, seed, familiarity);
                double moving = reaction.Leaves ? Math.Max(0, call.LeadSeconds - reaction.DelaySeconds - TurnSeconds) : 0;
                double at = start + RunSpeed * moving;
                if (at < 2500) within++;
                if (at < Lethal[role] * (0.85 + rng.NextDouble() * 0.3)) died++;
            }
            return new StompResult(raiders, died, within, died, call.Late);
        }

        private static StompResult[] Run(double familiarity) =>
            Enumerable.Range(1, 150).SelectMany(seed => Stomps.Select(s => Stomp(familiarity, seed, s))).ToArray();

        [TestCase(0.0, 0.25, 0.35)]
        [TestCase(1.0, 0.05, 0.15)]
        public void StompDeathsLandInTheOwnersBands(double familiarity, double low, double high)
        {
            StompResult[] stomps = Run(familiarity);
            double[] share = stomps.Select(s => s.Died / (double)s.Exposed).OrderBy(x => x).ToArray();
            double withinShare = stomps.Where(s => s.Within > 0).Average(s => s.WithinDied / (double)s.Within);
            TestContext.WriteLine($"familiarity {familiarity}: deaths {share.Average():P1} of the raiders in his radius at the call " +
                                  $"(p10 {share[share.Length / 10]:P0}, median {share[share.Length / 2]:P0}, p90 {share[share.Length * 9 / 10]:P0}, " +
                                  $"max {share[^1]:P0}); {withinShare:P1} of those still inside it when it lands; " +
                                  $"{stomps.Average(s => s.Within):0} inside it then");
            Assert.Multiple(() =>
            {
                Assert.That(share.Average(), Is.InRange(low, high), "deaths over the raiders in his radius");
                Assert.That(stomps.Count(s => s.Died == 0), Is.Zero, "a stomp nobody dies to is too perfect");
                Assert.That(share[^1], Is.LessThan(0.7), "and none wipes the raid alone");
            });
        }

        [Test]
        public void TheModelReproducesTheContainerRuns()
        {
            // duo16 (unlearned, real leads 4-5 s): 30-42% died; duo17 (learned, leads about 7 s): 24% and 47% at 80/60.
            double Mean(double f, double lead) => Enumerable.Range(1, 200).Average(seed => Stomp(f, seed, 80, lead: lead).Died / 130.0);
            double unlearned = Mean(0, 4.5), learned = Mean(1, 7.2), clear = Mean(1, 12);
            TestContext.WriteLine($"lead 4.5 s unlearned {unlearned:P0}, 7.2 s learned {learned:P0}, 12 s learned {clear:P0}");
            Assert.Multiple(() =>
            {
                Assert.That(unlearned, Is.InRange(0.30, 0.50));
                Assert.That(learned, Is.InRange(0.20, 0.40));
                Assert.That(clear, Is.LessThan(0.10), "from 9-10 s on, nearly everyone on time gets out");
            });
        }

        [Test]
        public void DeathsFallStepByStepAsTheRealmLearns()
        {
            double[] means = new[] { 0, 0.25, 0.5, 0.75, 1 }.Select(f => Run(f).Average(s => s.Died / (double)s.Exposed)).ToArray();
            TestContext.WriteLine(string.Join(", ", means.Select(m => m.ToString("P1"))));
            for (int i = 1; i < means.Length; i++) Assert.That(means[i], Is.LessThan(means[i - 1]));
        }

        [Test]
        public void LateCallsCostTheMost()
        {
            StompResult[] stomps = Run(0);
            double late = stomps.Where(s => s.LateCall).Average(s => s.Died / (double)s.Exposed);
            double onTime = stomps.Where(s => !s.LateCall).Average(s => s.Died / (double)s.Exposed);
            TestContext.WriteLine($"late calls {late:P1}, on time {onTime:P1}");
            // The model is kinder to late calls than the container was (duo19/duo20: 61% and 72% at 4.2-4.3 s).
            Assert.That(late, Is.GreaterThan(onTime + 0.05));
        }
        [Test]
        public void APlayTestCanForceOneCallLate()
        {
            Assert.That(RaidDiscipline.ForcedLate(0, 80), Is.False, "off");
            Assert.That(RaidDiscipline.ForcedLate(80, 80), Is.True);
            Assert.That(RaidDiscipline.ForcedLate(80, 60), Is.False, "only that threshold");
            Assert.That(RaidDiscipline.ForcedLate(-1, 20), Is.True, "all");
            for (long caller = 1; caller < 200; caller++)
            {
                RaidDiscipline.Call forced = RaidDiscipline.CallFor(caller, "Xanxicar", 80, 0, 0, forceLate: true);
                Assert.That(forced.Late, Is.True);
                Assert.That(forced.LeadSeconds, Is.InRange(RaidDiscipline.LateCallLeadMin, RaidDiscipline.LateCallLeadMax));
                RaidDiscipline.Call usual = RaidDiscipline.CallFor(caller, "Xanxicar", 80, 0, 0);
                if (usual.Late) Assert.That(usual, Is.EqualTo(forced), "a call that rolled late is the same call");
            }
            Assert.That(DOL.GS.Commands.EpicPlaytestCommandHandler.SetLateCall("80"), Does.Contain("80% call"));
            Assert.That(RaidDiscipline.ForceLateCallAt, Is.EqualTo(80));
            Assert.That(DOL.GS.Commands.EpicPlaytestCommandHandler.SetLateCall("off"), Does.Contain("off"));
            Assert.That(RaidDiscipline.ForceLateCallAt, Is.EqualTo(0));
            Assert.That(DOL.GS.Commands.EpicPlaytestCommandHandler.SetLateCall("x"), Does.Contain("not a threshold"));
        }
    }
}
