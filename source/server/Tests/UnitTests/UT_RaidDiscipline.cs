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
        public void Clean()
        {
            RaidDiscipline.FamiliarityOverride = null;
            // Other fixtures see the server's state before the seeding: the record's familiarity.
            RaidLearning.ResetForTests(RaidLearning.SeedingState.Pending);
        }

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

        // ---- learning from the raiders (Bots as people WP2R) -------------------------------------------------------------

        private const string CrystalCave = "dungeon-albion-crystal";

        private static RaidLearning.Learner Raider(long id) => new(id, BotPersonKey.Of(id), false);
        private static RaidLearning.Learner SpecialForce(long id) => new(AutonomousEpicSpecialForces.FirstBotId + id, default, true);

        private static int SeenOf(BotPeopleStore store, long id) =>
            store.MechanicsSeen.TryGet((BotPersonKey.Of(id), Mechanic), out OfflineBotMechanicSeenRecord row) ? row.Seen : 0;

        [Test]
        public void TheRaidKnowsTheMechanicFromSixtyPercentOfItsRaidersAndAQuarterWithNobody()
        {
            Assert.Multiple(() =>
            {
                Assert.That(RaidDiscipline.RosterFamiliarity(0.8, 0), Is.EqualTo(0.2).Within(1e-12), "nobody: a quarter");
                Assert.That(RaidDiscipline.RosterFamiliarity(0.8, 0.6), Is.EqualTo(0.8).Within(1e-12), "60%: full");
                Assert.That(RaidDiscipline.RosterFamiliarity(0.8, 1), Is.EqualTo(0.8).Within(1e-12));
                Assert.That(RaidDiscipline.RosterFamiliarity(0.8, 0.3), Is.EqualTo(0.8 * (0.25 + 0.75 * 0.5)).Within(1e-12), "linear between");
                Assert.That(RaidDiscipline.RosterFamiliarity(0, 1), Is.EqualTo(0), "a fresh world stays unlearned");
                Assert.That(RaidDiscipline.RosterFamiliarity(0.8, default(RaidDiscipline.RosterKnowledge)), Is.EqualTo(0.8),
                    "nobody counted (real players only): the record's");
                for (int i = 1; i <= 60; i++)
                    Assert.That(RaidDiscipline.RosterFamiliarity(0.9, i / 100.0), Is.GreaterThan(RaidDiscipline.RosterFamiliarity(0.9, (i - 1) / 100.0)));
                Assert.That(RaidDiscipline.OwnKnowledge(0), Is.EqualTo(0));
                Assert.That(RaidDiscipline.OwnKnowledge(1), Is.EqualTo(0.5));
                Assert.That(RaidDiscipline.OwnKnowledge(RaidDiscipline.KnowsAfterRaids), Is.EqualTo(1));
                Assert.That(RaidDiscipline.OwnKnowledge(400), Is.EqualTo(1));
            });
        }

        [Test]
        public void ANewcomerLapsesMoreThanTheVeteranBesideHimAndTheRaidsMeanStays()
        {
            var random = new Random(7);
            for (int trial = 0; trial < 500; trial++)
            {
                double record = random.NextDouble();
                int[] seen = Enumerable.Range(0, 10 + random.Next(100)).Select(_ => random.Next(4)).ToArray();
                var roster = seen.Aggregate(default(RaidDiscipline.RosterKnowledge), (r, n) => r.With(n));
                double raid = RaidDiscipline.RosterFamiliarity(record, roster);
                double[] own = seen.Select(n => RaidDiscipline.RaiderFamiliarity(record, raid, RaidDiscipline.OwnKnowledge(n), roster.MeanKnowledge)).ToArray();
                Assert.That(own, Is.All.InRange(0.0, 1.0));
                Assert.That(own.Average(), Is.EqualTo(raid).Within(1e-9), "the raid's mean is the raid's familiarity");
                double newcomer = RaidDiscipline.RaiderFamiliarity(record, raid, 0, roster.MeanKnowledge);
                double veteran = RaidDiscipline.RaiderFamiliarity(record, raid, 1, roster.MeanKnowledge);
                Assert.That(newcomer, Is.LessThanOrEqualTo(veteran));
            }
            // A raid of 0.78 with 60% veterans: the veteran 0.94, the newcomer 0.55.
            var half = Enumerable.Range(0, 10).Aggregate(default(RaidDiscipline.RosterKnowledge), (r, i) => r.With(i < 6 ? 2 : 0));
            double r78 = RaidDiscipline.RosterFamiliarity(0.78, half);
            double vet = RaidDiscipline.RaiderFamiliarity(0.78, r78, 1, half.MeanKnowledge), fresh = RaidDiscipline.RaiderFamiliarity(0.78, r78, 0, half.MeanKnowledge);
            Assert.That(r78, Is.EqualTo(0.78).Within(1e-12));
            Assert.That(fresh, Is.LessThan(vet - 0.3));
            // ...so the newcomer lapses more often (the same reaction rule, its own familiarity).
            int Lapses(double f) => Enumerable.Range(1, 20_000).Count(id =>
                RaidDiscipline.ReactionFor(id, RaidDiscipline.NeutralTraits, RaidDiscipline.Role.Melee, Mechanic, 80, 1, f).Lapse != RaidDiscipline.Lapse.None);
            Assert.That(Lapses(fresh), Is.GreaterThan(Lapses(vet) * 1.4));
        }

        [Test]
        public void TheSeenShareRisesBetweenTwoRunsAndARaiderIsCreditedOncePerRaid()
        {
            RaidLearning.ResetForTests();
            var store = new BotPeopleStore(() => null);
            RaidLearning.Learner[] raiders = Enumerable.Range(1, 60).Select(i => Raider(i)).ToArray();
            DateTime now = new(2026, 11, 20, 20, 0, 0, DateTimeKind.Utc);

            // Run 1 (unlearned, record 0.39 from three losses): nobody had seen the stomp.
            double first = RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 103, 3, 0.39, false, raiders, now);
            Assert.That(RaidLearning.RosterOf(CrystalCave, Mechanic)?.SeenShare, Is.EqualTo(0));
            Assert.That(first, Is.EqualTo(0.39 * RaidDiscipline.RosterFloor).Within(1e-12));
            // Its later stomps: the same raid, nobody credited twice, the roster read before the raid stands.
            double second = RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 103, 3, 0.39, false, raiders, now);
            Assert.That(second, Is.EqualTo(first));
            Assert.That(raiders.Select(r => SeenOf(store, r.Id)), Is.All.EqualTo(1));
            Assert.That(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 3, 1, first), Is.EqualTo(first).Within(1e-12), "all newcomers: the raid's");

            // Run 2 (the record moved on: seed 4): 45 of them again and 15 newcomers.
            RaidLearning.Learner[] again = raiders.Take(45).Concat(Enumerable.Range(100, 15).Select(i => Raider(i))).ToArray();
            double learned = RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 104, 4, 0.52, false, again, now.AddHours(3));
            var roster = RaidLearning.RosterOf(CrystalCave, Mechanic).Value;
            Assert.That(roster.SeenShare, Is.EqualTo(0.75).Within(1e-12), "seenShare rises between the runs");
            Assert.That(learned, Is.EqualTo(0.52).Within(1e-12));
            double veteran = RaidLearning.ForRaider(true, CrystalCave, Mechanic, 4, 1, learned);
            double newcomer = RaidLearning.ForRaider(true, CrystalCave, Mechanic, 4, 100, learned);
            Assert.That(newcomer, Is.LessThan(veteran));
            Assert.That(again.Average(r => RaidLearning.ForRaider(true, CrystalCave, Mechanic, 4, r.Id, learned)), Is.EqualTo(learned).Within(1e-9));
            Assert.That(SeenOf(store, 1), Is.EqualTo(2));
            Assert.That(SeenOf(store, 100), Is.EqualTo(1));
            Assert.That(SeenOf(store, 50), Is.EqualTo(1), "not there this time");
            Assert.That(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 4, 999, learned), Is.EqualTo(learned), "not counted at the call: the raid's");
            Assert.That(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 3, 1, learned), Is.EqualTo(learned), "an earlier raid's seed: the raid's");
            RaidLearning.ResetForTests();
        }

        [Test]
        public void TwoForcedRunsWithTheSameSeedAreTwoRaids()
        {
            // A raid from the launcher (or any uncounted end) never joins the record, so both runs have the same seed: the
            // raid's instance (its creation tick) tells them apart (review MUST FIX 1, the played check "Crystal Cave twice").
            RaidLearning.ResetForTests();
            var store = new BotPeopleStore(() => null);
            DateTime now = new(2026, 11, 21, 23, 0, 0, DateTimeKind.Utc);
            RaidLearning.Learner[] run1 = Enumerable.Range(1, 60).Select(i => Raider(i)).ToArray();
            double first = RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 5_000, 3, 0.39, false, run1, now);
            Assert.That(RaidLearning.RosterOf(CrystalCave, Mechanic)?.SeenShare, Is.EqualTo(0));
            // Run 2, same seed: 45 return, 15 new; the 15 who stayed away are not in this raid's roster.
            RaidLearning.Learner[] run2 = run1.Take(45).Concat(Enumerable.Range(100, 15).Select(i => Raider(i))).ToArray();
            double second = RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 9_000_000, 3, 0.39, false, run2, now.AddHours(2));
            var roster = RaidLearning.RosterOf(CrystalCave, Mechanic).Value;
            Assert.Multiple(() =>
            {
                Assert.That(roster.Raiders, Is.EqualTo(60), "only the raiders of run 2");
                Assert.That(roster.SeenShare, Is.EqualTo(0.75).Within(1e-12), "seenShare rises between the runs");
                Assert.That(second, Is.GreaterThan(first));
                Assert.That(second, Is.EqualTo(0.39).Within(1e-12));
                Assert.That(SeenOf(store, 1), Is.EqualTo(2), "credited again in the second raid");
                Assert.That(SeenOf(store, 50), Is.EqualTo(1));
                Assert.That(SeenOf(store, 100), Is.EqualTo(1));
                Assert.That(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 3, 100, second),
                    Is.LessThan(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 3, 1, second)), "the newcomer below the veteran");
            });
            // Its later stomps (the same instance): nobody credited twice.
            RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 9_000_000, 3, 0.39, false, run2, now.AddHours(2));
            Assert.That(SeenOf(store, 1), Is.EqualTo(2));
            RaidLearning.ResetForTests();
        }

        [Test]
        public void ARealPlayerInThePartiesKeepsTheRecordExactly()
        {
            RaidLearning.ResetForTests();
            var store = new BotPeopleStore(() => null);
            RaidLearning.Learner[] raiders = Enumerable.Range(1, 30).Select(i => Raider(i)).ToArray();
            double familiarity = RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 7_000, 3, 0.61, true, raiders, DateTime.UtcNow);
            Assert.That(familiarity, Is.EqualTo(0.61), "no learning lowers a player's own fight");
            Assert.That(store.MechanicsSeen.Count, Is.EqualTo(0));
            Assert.That(RaidLearning.RosterOf(CrystalCave, Mechanic), Is.Null);
            Assert.That(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 3, 1, 0.61), Is.EqualTo(0.61));
            RaidLearning.ResetForTests();
        }

        [Test]
        public async System.Threading.Tasks.Task TheSeedingWaitsForTheRecordAndFailsSafely()
        {
            RaidLearning.ResetForTests(RaidLearning.SeedingState.Pending);
            var store = new BotPeopleStore(() => null);
            int looks = 0, ensured = 0;
            var sources = new RaidLearning.SeedingSources
            {
                EnsureLoading = () => ensured++,
                Loaded = () => ++looks >= 3,
                Bots = () => UpgradedRealm(),
                Encounters = () => [(CrystalCave, eRealm.Albion, "Xanxicar")],
                RecordFamiliarity = _ => 0.39,
                PollMilliseconds = 10, WaitMilliseconds = 5_000,
            };
            Assert.That(await RaidLearning.StartSeeding(store, true, sources), Is.EqualTo(RaidLearning.SeedingState.Done));
            Assert.Multiple(() =>
            {
                Assert.That(ensured, Is.EqualTo(1));
                Assert.That(looks, Is.EqualTo(3), "it waited for the record");
                Assert.That(RaidLearning.Seeding, Is.EqualTo(RaidLearning.SeedingState.Done));
                Assert.That(store.MechanicsSeen.Count, Is.GreaterThan(0));
            });
            // Never loads: Failed, nothing written, the record's familiarity this run.
            var empty = new BotPeopleStore(() => null);
            var never = new RaidLearning.SeedingSources
            {
                EnsureLoading = () => { }, Loaded = () => false, Bots = () => UpgradedRealm(),
                Encounters = () => [(CrystalCave, eRealm.Albion, "Xanxicar")], RecordFamiliarity = _ => 0.39,
                PollMilliseconds = 10, WaitMilliseconds = 60,
            };
            Assert.That(await RaidLearning.StartSeeding(empty, true, never), Is.EqualTo(RaidLearning.SeedingState.Failed));
            Assert.That(RaidLearning.Seeding, Is.EqualTo(RaidLearning.SeedingState.Failed));
            Assert.That(empty.MechanicsSeen.Count, Is.EqualTo(0));
            Assert.That(RaidLearning.FamiliarityAtCall(CrystalCave, Mechanic, [], null), Is.EqualTo(RaidDiscipline.FamiliarityOf(CrystalCave)));
            // The roster can't be read: Failed too.
            var broken = new RaidLearning.SeedingSources
            {
                EnsureLoading = () => { }, Loaded = () => true, Bots = () => throw new InvalidOperationException("no database"),
                Encounters = () => [], RecordFamiliarity = _ => 0.39, PollMilliseconds = 10,
            };
            Assert.That(await RaidLearning.StartSeeding(empty, true, broken), Is.EqualTo(RaidLearning.SeedingState.Failed));
            // The switch off at start: nothing runs, the seeding stays Pending.
            Assert.That(await RaidLearning.StartSeeding(empty, false, sources), Is.EqualTo(RaidLearning.SeedingState.Pending));
            Assert.That(RaidLearning.Seeding, Is.EqualTo(RaidLearning.SeedingState.Pending));
        }

        [Test]
        public void TheLogLinesCarryTheDesignsFields()
        {
            var roster = default(RaidDiscipline.RosterKnowledge).With(0).With(2).WithVeteran();
            string line = RaidLearning.LearnLine(CrystalCave, Mechanic, 0.78, roster, 0.78, 80);
            Assert.That(line, Does.StartWith($"RAID_LEARN raid={CrystalCave} mechanic={Mechanic} record=0.78 seenShare=0.67 familiarity=0.78 "));
            Assert.That(line, Does.Contain("raiders=3 seen=2 specialForces=1"));
            Assert.That(line, Does.Contain(" newcomer=").And.Contain(" veteran=").And.EndWith(" health=80"));
            Assert.That(RaidLearning.SeenLine(CrystalCave, Mechanic, 60, 15, 80), Does.StartWith("RAID_MECHANIC_SEEN how=raid"));
            Assert.That(RaidLearning.SeedLine(new RaidLearning.SeedResult(CrystalCave, Mechanic, 1, 0.39, 0.8, 150, 121, false)),
                Is.EqualTo($"RAID_MECHANIC_SEEN how=seeded raid={CrystalCave} mechanic={Mechanic} realm=1 record=0.39 chance=0.80 level50=150 seeded=121"));
        }

        [Test]
        public void EpicSpecialForcesHaveSeenEverythingAndLeaveNoRow()
        {
            RaidLearning.ResetForTests();
            var store = new BotPeopleStore(() => null);
            DateTime now = new(2026, 11, 20, 20, 0, 0, DateTimeKind.Utc);
            // A wipe's retry: 24 Special Forces beside 16 raiders who never saw him.
            RaidLearning.Learner[] raiders = Enumerable.Range(1, 24).Select(i => SpecialForce(i)).Concat(Enumerable.Range(1, 16).Select(i => Raider(i))).ToArray();
            double familiarity = RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 106, 6, 0.6, false, raiders, now);
            var roster = RaidLearning.RosterOf(CrystalCave, Mechanic).Value;
            Assert.Multiple(() =>
            {
                Assert.That(roster.Raiders, Is.EqualTo(40));
                Assert.That(roster.SpecialForces, Is.EqualTo(24));
                Assert.That(roster.SeenShare, Is.EqualTo(0.6).Within(1e-12));
                Assert.That(familiarity, Is.EqualTo(0.6).Within(1e-12), "the realm's veterans carry the raid");
                Assert.That(store.MechanicsSeen.Count, Is.EqualTo(16), "only the realm's own raiders remember");
                Assert.That(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 6, AutonomousEpicSpecialForces.FirstBotId + 1, familiarity),
                    Is.GreaterThan(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 6, 1, familiarity)));
                // A whole raid of them: the record's familiarity.
                RaidLearning.ResetForTests();
                Assert.That(RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 107, 7, 0.6, false, raiders.Take(24).ToArray(), now), Is.EqualTo(0.6).Within(1e-12));
                // The Special Forces' and buffbots' bots have no person (BotPersonKey.Of(GameBot) is None for in-memory bots).
                Assert.That(new RaidLearning.Learner(5, default, false).Person.IsNone, Is.True);
                Assert.That(RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 108, 8, 0.6, false, [new RaidLearning.Learner(5, default, false)], now),
                    Is.EqualTo(0.6), "nobody counted: the record's");
            });
            RaidLearning.ResetForTests();
        }

        [Test]
        public void TheSwitchOffGivesTodaysFamiliarityExactly()
        {
            RaidLearning.ResetForTests();
            RealmRaidRecord.ResetForTests();
            RealmRaidRecord.SetForTests(CrystalCave, "Defeated", "Boss defeated", "Defeated");
            bool was = DOL.GS.ServerProperties.Properties.RAID_LEARNS_FROM_RAIDERS;
            var store = new BotPeopleStore(() => null);
            DateTime now = new(2026, 11, 20, 20, 0, 0, DateTimeKind.Utc);
            RaidLearning.Learner[] raiders = Enumerable.Range(1, 40).Select(i => Raider(i)).ToArray();
            try
            {
                double today = RaidDiscipline.FamiliarityOf(CrystalCave);
                Assert.Multiple(() =>
                {
                    Assert.That(RaidLearning.AtCall(store, false, CrystalCave, Mechanic, 103, 3, today, false, raiders, now), Is.EqualTo(today));
                    Assert.That(store.MechanicsSeen.Count, Is.EqualTo(0), "off: nothing remembered");
                    Assert.That(RaidLearning.ForRaider(false, CrystalCave, Mechanic, 3, 1, today), Is.EqualTo(today));
                    DOL.GS.ServerProperties.Properties.RAID_LEARNS_FROM_RAIDERS = false;
                    Assert.That(RaidLearning.FamiliarityAtCall(CrystalCave, Mechanic, [], null), Is.EqualTo(today));
                    DOL.GS.ServerProperties.Properties.RAID_LEARNS_FROM_RAIDERS = true;
                    Assert.That(RaidLearning.FamiliarityAtCall(CrystalCave, Mechanic, [], null), Is.EqualTo(today), "on, no running realm raid (a player's own group): the record's");
                    RaidLearning.Seeding = RaidLearning.SeedingState.Pending;
                    Assert.That(RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 103, 3, today, false, [], now), Is.EqualTo(today));
                    Assert.That(RaidLearning.FamiliarityAtCall(CrystalCave, Mechanic, [], null), Is.EqualTo(today), "before the seeding: the record's");
                    Assert.That(RaidLearning.AtCall(store, true, CrystalCave, "Myrddraxis/blast", 103, 3, today, false, raiders, now), Is.EqualTo(today),
                        "a mechanic not learned keeps the record's");
                    RaidDiscipline.FamiliarityOverride = 0.9;
                    Assert.That(RaidLearning.AtCall(store, true, CrystalCave, Mechanic, 103, 3, 0.9, false, raiders, now, overridden: true), Is.EqualTo(0.9),
                        "a play test's override stands");
                    Assert.That(store.MechanicsSeen.Count, Is.EqualTo(0), "and credits nobody: a forced familiarity is not a raid learned from");
                    Assert.That(RaidLearning.ForRaider(true, CrystalCave, Mechanic, 3, 1, 0.9, overridden: true), Is.EqualTo(0.9));
                });
            }
            finally
            {
                DOL.GS.ServerProperties.Properties.RAID_LEARNS_FROM_RAIDERS = was;
                RaidDiscipline.FamiliarityOverride = null;
                RealmRaidRecord.ResetForTests();
                RaidLearning.ResetForTests();
            }
        }

        [Test]
        public void TheLearnedMechanicIsTheCallsKeyAndItsRaidIsCrystalCave()
        {
            Assert.That(RaidDiscipline.LearnedMechanics, Has.Length.EqualTo(1));
            foreach (var (mechanic, boss) in RaidDiscipline.LearnedMechanics)
            {
                Assert.That(mechanic, Is.EqualTo(boss + "/blast"), "EpicEncounterTactics.MechanicKey");
                Assert.That(RaidDiscipline.IsLearned(mechanic), Is.True);
                Assert.That(AutonomousRealmRaid.RaidIdForBoss(boss), Is.EqualTo(CrystalCave));
                Assert.That(mechanic.Length, Is.LessThanOrEqualTo(64), "the Mechanic column");
            }
        }

        private static RaidLearning.RosterBot[] UpgradedRealm() =>
            Enumerable.Range(1, 400).Select(i => new RaidLearning.RosterBot(i, i % 3 + 1, i % 5 == 0 ? 42 : 50)).ToArray();

        [TestCase(0.15)]
        [TestCase(0.39)]
        [TestCase(0.78)]
        [TestCase(0.97)]
        public void TheSeedingKeepsTheRealmsAverageFamiliarityOnAnUpgradedWorld(double record)
        {
            RaidLearning.ResetForTests();
            var store = new BotPeopleStore(() => null);
            RaidLearning.RosterBot[] bots = UpgradedRealm();
            (string, eRealm, string)[] encounters = [(CrystalCave, eRealm.Albion, "Xanxicar"), ("dungeon-albion-avalon", eRealm.Albion, "DuraekEmpowered")];
            DateTime now = new(2026, 11, 20, 20, 0, 0, DateTimeKind.Utc);
            var results = RaidLearning.Seed(store, bots, encounters, id => id == CrystalCave ? record : 0, now);
            Assert.That(results, Has.Count.EqualTo(1));
            RaidLearning.RosterBot[] albion50 = bots.Where(b => b.Realm == (int)eRealm.Albion && b.Level == 50).ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(results[0].Seeded, Is.EqualTo(store.MechanicsSeen.Count));
                Assert.That(store.MechanicsSeen.Where(_ => true).Select(r => r.CharacterId),
                    Is.SubsetOf(albion50.Select(b => b.Id)), "only Albion's level 50s");
                Assert.That(store.MechanicsSeen.Where(_ => true).Select(r => r.Seen), Is.All.EqualTo(RaidDiscipline.KnowsAfterRaids));
                Assert.That(results[0].Seeded / (double)albion50.Length, Is.EqualTo(RaidDiscipline.SeedChance(record)).Within(0.08));
            });

            // Raids drawn from the realm's level 50s, 40 to 120 strong: the raid's familiarity, and its raiders' mean, stay the record's.
            var random = new Random(11);
            var raids = new List<double>();
            var raiders = new List<double>();
            for (int raid = 0; raid < 400; raid++)
            {
                long[] roster = albion50.OrderBy(_ => random.Next()).Take(40 + random.Next(Math.Min(81, albion50.Length - 39))).Select(b => b.Id).ToArray();
                var knowledge = roster.Aggregate(default(RaidDiscipline.RosterKnowledge), (k, id) => k.With(SeenOf(store, id)));
                double familiarity = RaidDiscipline.RosterFamiliarity(record, knowledge);
                raids.Add(familiarity);
                raiders.Add(roster.Average(id => RaidDiscipline.RaiderFamiliarity(record, familiarity,
                    RaidDiscipline.OwnKnowledge(SeenOf(store, id)), knowledge.MeanKnowledge)));
            }
            TestContext.WriteLine($"record {record:0.00}: raids {raids.Average():0.000} (min {raids.Min():0.000}), raiders {raiders.Average():0.000}");
            Assert.That(raids.Average(), Is.EqualTo(record).Within(0.01 * record + 1e-9), "the upgrade keeps the average");
            Assert.That(raids.Min(), Is.GreaterThan(record * 0.9), "and no raid falls far");
            Assert.That(raiders.Average(), Is.EqualTo(raids.Average()).Within(1e-9));
            // The design's first wording (a chance equal to the familiarity) would not have kept it below 60%.
            if (record < RaidDiscipline.RosterFullShare)
                Assert.That(RaidDiscipline.RosterFamiliarity(record, record), Is.LessThan(record * 0.9));

            // Once: a second start (or a world that has learned) seeds nothing more.
            var again = RaidLearning.Seed(store, bots, encounters, id => id == CrystalCave ? record : 0, now.AddDays(1));
            Assert.That(again.Single().AlreadyKnown, Is.True);
            Assert.That(again.Single().Seeded, Is.EqualTo(0));
        }

        [Test]
        public void TheSeedingSkipsAFreshWorldAndRollsTheSameForTheSameBot()
        {
            var store = new BotPeopleStore(() => null);
            (string, eRealm, string)[] encounters = [(CrystalCave, eRealm.Albion, "Xanxicar")];
            var results = RaidLearning.Seed(store, UpgradedRealm(), encounters, _ => 0, DateTime.UtcNow);
            Assert.That(results, Is.Empty, "no counted attempt: nothing seeded");
            Assert.That(store.MechanicsSeen.Count, Is.EqualTo(0));
            for (long id = 1; id < 300; id++)
            {
                Assert.That(RaidDiscipline.SeededAtUpgrade(id, 50, Mechanic, 0.5), Is.EqualTo(RaidDiscipline.SeededAtUpgrade(id, 50, Mechanic, 0.5)));
                Assert.That(RaidDiscipline.SeededAtUpgrade(id, 49, Mechanic, 1), Is.False, "only level 50s raid");
            }
            Assert.That(RaidDiscipline.SeedChance(0), Is.EqualTo(0));
            Assert.That(RaidDiscipline.SeedChance(0.39), Is.EqualTo(RaidDiscipline.SeedShareFloor));
            Assert.That(RaidDiscipline.SeedChance(0.97), Is.EqualTo(0.97));
        }
    }
}
