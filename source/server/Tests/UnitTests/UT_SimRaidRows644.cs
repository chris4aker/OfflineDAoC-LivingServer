using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.AI.Brain;
using DOL.GS;
using NUnit.Framework;
using GkRule = DOL.UnitTests.UT_RaidSim122GreenKnight.GkRule;
using CureRule = DOL.UnitTests.UT_RaidSim121SummonersGreenKnight.CureRule;
using RoesiaFight = DOL.UnitTests.UT_RaidSim121SummonersGreenKnight.RoesiaFight;

namespace DOL.UnitTests
{
    /// <summary>
    /// #644 part 6, "the sims writing the same rows": the raid simulations with a whole fight and the game's loss (Summoner
    /// Roesia, UT_RaidSim121SummonersGreenKnight.RoesiaFight; the Green Knight, UT_RaidSim122GreenKnight) write each attempt as
    /// the server's RAID_* rows through <see cref="SimRaidRows"/>. Pinned here: a simulated attempt's rows carry the same fields,
    /// in the same order, as the server's formatters write (read with the same key=value rule as
    /// living_report_sections/_util.py's fields, which _raid.py and raid_postmortem.py use), they join on one attempt id, their
    /// counts agree with each other and with the fight, and writing them changes nothing in the fight. The server job then reads
    /// the written log with raid_postmortem.py --check. Tests only: a sim is a model, not a game result.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_SimRaidRows644
    {
        private EpicTestServerScope _server;

        [SetUp]
        public void SetUp() => _server = new EpicTestServerScope();

        [TearDown]
        public void TearDown() => _server.Dispose();

        /// <summary>One row of each kind from the server's own formatters (the rows UT_RaidAttemptRows644,
        /// UT_RaidFightSampleRows644 and UT_RaidMechanicRows644 pin), the reference a simulated row's fields are held to.</summary>
        private static Dictionary<string, string> ServerRows() => new()
        {
            [RaidAttemptRows.AttemptMarker] = RaidAttemptRows.FormatAttempt(new RaidAttemptRows.AttemptFacts("dungeon-albion-krondon.1",
                "dungeon-albion-krondon", "dungeon", 61, "Orylle", 1, 96, 64, 0, "12:30:8:46", "Cleric:20,Armsman:12", 1, "abc1234")),
            [RaidAttemptRows.EndMarker] = RaidAttemptRows.FormatEnd(new RaidAttemptRows.EndFacts("e.3", "wipe", 412, 96, 70, 61, 9, 88, 100, 31)),
            [RaidAttemptRows.DeathMarker] = RaidAttemptRows.FormatDeath(new RaidAttemptRows.DeathFacts("e.1", 73, 14, "player", true, "Cleric",
                "healer", 50, "boss", "Icelord Kvasir", "aoe", 4321, "breath", 812, 3, 2)),
            [RaidFightSampleRows.SampleMarker] = RaidFightSampleRows.FormatSample(new RaidFightSampleRows.SampleFacts("e.1", 45, 72, null, -1,
                new RaidFightSampleRows.Counts("4:10:2:32", "3:10:1:20", 61, 2, 640, 1, 0), "tank")),
            [RaidAttemptRows.MechanicMarker] = RaidAttemptRows.FormatMechanic(new RaidAttemptRows.MechanicFacts("e.1", "Legion", "soulBlast", 84,
                1000, 22, 10, 12, 4, 8, false, 2500, 450, 41200)),
            [RaidAttemptRows.ThreatMarker] = RaidAttemptRows.FormatThreat(new RaidAttemptRows.ThreatFacts("e.1", "Legion", 31, "tank", "healer",
                "gamebot", "threat", 12400)),
            [RaidAttemptRows.RoleTotalsMarker] = RaidAttemptRows.FormatRoleTotals(RaidAttemptRows.RoleTotals("e.1",
                Array.Empty<RaidAttemptRows.RaiderLine>())[0]),
        };

        /// <summary>The key=value and key="text" pairs of a row, in order: living_report_sections/_util.py's FIELD, exactly.</summary>
        private static readonly Regex Field = new(@"(\w+)=(""([^""]*)""|\S+)", RegexOptions.CultureInvariant);

        private static string Tag(string row) => row.Split(' ', 2)[0];

        private static List<(string Key, string Value)> Fields(string row)
        {
            string[] parts = row.Split(' ', 2);
            return parts.Length < 2 ? new() : Field.Matches(parts[1])
                .Select(m => (Key: m.Groups[1].Value, Value: m.Groups[3].Success ? m.Groups[3].Value : m.Groups[2].Value)).ToList();
        }

        private static string V(string row, string key) => Fields(row).FirstOrDefault(f => f.Key == key).Value;

        private static int N(string row, string key) => int.Parse(V(row, key));

        private static SimRaidRows RoesiaRows(int seed, int n, string realm, out RoesiaFight fight)
        {
            var rows = new SimRaidRows("summoners-" + realm, seed + 1, "dungeon", 248, RoesiaFight.SummonerRoesiaName, 64);
            fight = RoesiaFight.Run(seed, n, CureRule.Round2, realm, rows: rows);
            return rows;
        }

        private static SimRaidRows GreenKnightRows(int seed, int n, int attempt, out (bool Won, double Minutes, int MaxTrees, int Fallen, double Left) fight)
        {
            var rows = new SimRaidRows("openworld-albion-greenknight", attempt, "open", 1, UT_RaidSim122GreenKnight.GreenKnightName, 40);
            fight = UT_RaidSim122GreenKnight.GreenKnightFight(seed, GkRule.TagBurn, n, 80.0, true, rows: rows);
            return rows;
        }

        /// <summary>The checks every simulated attempt passes (the server's fields, one id, counts that agree).</summary>
        private static void CheckAttempt(SimRaidRows sim, int n, string outcome, int fallen, string what)
        {
            Dictionary<string, string> server = ServerRows();
            IReadOnlyList<string> rows = sim.Rows;
            Assert.That(rows, Is.Not.Empty, what);
            foreach (string row in rows)
            {
                string tag = Tag(row);
                Assert.That(server.ContainsKey(tag), Is.True, $"{what}: {tag} is one of the server's raid rows");
                Assert.That(Fields(row).Select(f => f.Key), Is.EqualTo(Fields(server[tag]).Select(f => f.Key)),
                    $"{what}: {tag} has the server's fields in the server's order: {row}");
                Assert.That(V(row, "attempt"), Is.EqualTo(sim.Attempt), $"{what}: every row joins on the attempt id");
                Assert.That(row, Does.Not.Contain("\n"));
            }
            string pull = rows[0];
            Assert.That(Tag(pull), Is.EqualTo(RaidAttemptRows.AttemptMarker), what + ": the pull comes first");
            Assert.That(rows.Count(r => Tag(r) == RaidAttemptRows.AttemptMarker), Is.EqualTo(1), what + ": one pull (no second run of the id)");
            Assert.That((V(pull, "build"), N(pull, "size"), V(pull, "event"), V(pull, "kind")), Is.EqualTo((SimRaidRows.SimBuild, n, sim.Event, sim.Kind)));
            Assert.That(V(pull, "roles").Split(':').Sum(int.Parse), Is.EqualTo(n), what + ": every raider has a role");
            Assert.That(V(pull, "classes").Split(',').Sum(c => int.Parse(c.Split(':')[1])), Is.EqualTo(n));

            string[] ends = rows.Where(r => Tag(r) == RaidAttemptRows.EndMarker).ToArray();
            Assert.That(ends, Has.Length.EqualTo(1), what + ": one end");
            string end = ends[0];
            string[] deaths = rows.Where(r => Tag(r) == RaidAttemptRows.DeathMarker).ToArray();
            Assert.That(V(end, "outcome"), Is.EqualTo(outcome), what + ": the outcome is the fight's");
            Assert.That((N(end, "engaged"), N(end, "fallen"), N(end, "died"), N(end, "left"), N(end, "deaths")),
                Is.EqualTo((n, fallen, fallen, 0, deaths.Length)), what + ": every fallen raider died once, and has a RAID_DEATH row");
            Assert.That(deaths.Select(d => N(d, "idx")), Is.Unique, what + ": one death per raider index");
            Assert.That(deaths.Select(d => N(d, "idx")), Is.All.InRange(1, n));
            Assert.That(deaths.Select(d => V(d, "killerKind")), Is.All.AnyOf("boss", "add"));
            Assert.That(deaths.Select(d => V(d, "source")), Is.All.AnyOf("melee", "spell", "dot", "aoe", "fall", "other"));
            Assert.That(deaths.Select(d => N(d, "t")), Is.Ordered, what + ": deaths in the order they happened");
            Assert.That(N(end, "lowest"), Is.LessThanOrEqualTo(N(end, "health")));

            int[] samples = rows.Where(r => Tag(r) == RaidFightSampleRows.SampleMarker).Select(r => N(r, "t")).ToArray();
            Assert.That(samples, Is.Not.Empty);
            Assert.That(samples, Is.EqualTo(Enumerable.Range(0, samples.Length).Select(k => k * 15)), what + ": a sample at the pull, then every 15 s");
            Assert.That(rows.Where(r => Tag(r) == RaidFightSampleRows.SampleMarker).Select(r => V(r, "phase") + V(r, "adds")), Is.All.EqualTo("--"),
                what + ": phase and adds are '-', as on the server");

            string[] roles = rows.Where(r => Tag(r) == RaidAttemptRows.RoleTotalsMarker).ToArray();
            Assert.That(roles.Select(r => V(r, "role")), Is.EqualTo(RaidAttemptRows.Roles), what + ": four role totals");
            Assert.That(roles.Sum(r => N(r, "raiders")), Is.EqualTo(n));
            Assert.That(roles.Sum(r => N(r, "deaths")), Is.EqualTo(deaths.Length));
            Assert.That(rows.SkipWhile(r => Tag(r) != RaidAttemptRows.EndMarker).Skip(1).Select(Tag), Is.All.EqualTo(RaidAttemptRows.RoleTotalsMarker),
                what + ": nothing but the role totals after the end, as the server writes them");
            Assert.That(rows.Count(r => Tag(r) == RaidAttemptRows.ThreatMarker), Is.GreaterThanOrEqualTo(1), what + ": the first target");
        }

        [Test]
        public void ASimulatedRoesiaPullWritesTheServersRowsFieldForField()
        {
            foreach ((int seed, int n, string realm) in new[] { (0, 80, "albion"), (1, 106, "midgard"), (2, 64, "hibernia") })
            {
                SimRaidRows sim = RoesiaRows(seed, n, realm, out RoesiaFight fight);
                TestContext.WriteLine($"Roesia {realm} {n} seed {seed}: won {fight.Won}, fallen {fight.Fallen}, {sim.Rows.Count} rows");
                // killed; the game's wipe (half of the engaged fallen); else the model's 15 minutes ran out.
                string outcome = fight.Won ? "killed" : fight.Fallen * 2 >= n ? "wipe" : "stopped";
                CheckAttempt(sim, n, outcome, fight.Fallen, $"Roesia {realm} {n} seed {seed}");
                // Her model has no mechanic signal (the server has none for her): no RAID_MECHANIC_FIRE.
                Assert.That(sim.Rows.Count(r => Tag(r) == RaidAttemptRows.MechanicMarker), Is.Zero);
                Assert.That(sim.Rows.Where(r => Tag(r) == RaidAttemptRows.DeathMarker).Select(r => V(r, "killer")),
                    Is.All.EqualTo(RoesiaFight.SummonerRoesiaName));
                Assert.That(sim.Rows.Where(r => Tag(r) == RaidAttemptRows.DeathMarker).Select(r => V(r, "dist")), Is.All.EqualTo("-"),
                    "no positions in her model: the distance is unknown, not 0");
            }
        }

        [Test]
        public void ASimulatedGreenKnightPullWritesTheServersRowsFieldForField()
        {
            foreach ((int seed, int n) in new[] { (0, 56), (1, 40), (2, 80) })
            {
                SimRaidRows sim = GreenKnightRows(seed, n, seed + 1, out var fight);
                TestContext.WriteLine($"Green Knight {n} seed {seed}: won {fight.Won}, fallen {fight.Fallen}, {sim.Rows.Count} rows");
                // killed; wipe when no raider is left fighting (the model's loss, before its 45 minutes); else "stopped".
                string outcome = fight.Won ? "killed" : fight.Minutes < 45 ? "wipe" : "stopped";
                CheckAttempt(sim, n, outcome, fight.Fallen, $"Green Knight {n} seed {seed}");
                string[] fires = sim.Rows.Where(r => Tag(r) == RaidAttemptRows.MechanicMarker).ToArray();
                Assert.That(fires.Select(r => V(r, "mechanic")).Distinct(), Is.SubsetOf(new[] { "heat", "heal", "trees" }),
                    "his three measure-only mechanics (OFGreenKnight's RaidBossMechanics calls)");
                Assert.That(fires.Select(r => V(r, "mechanic")), Does.Contain("trees").And.Contain("heat"));
                Assert.That(fires.Select(r => V(r, "called")), Is.All.EqualTo("0"), "measure-only: the raid AI is not told");
                foreach (string heat in fires.Where(r => V(r, "mechanic") == "heat"))
                {
                    Assert.That((N(heat, "radius"), N(heat, "leadMs"), V(heat, "reactMedianMs")), Is.EqualTo((500, 0, "-")));
                    Assert.That(N(heat, "damage"), Is.EqualTo(N(heat, "hit") * 200), "200 a raider hit (the model's heat after resists)");
                    Assert.That(N(heat, "within"), Is.EqualTo(N(heat, "exposedAtCall")), "it lands at once");
                }
                foreach (string trees in fires.Where(r => V(r, "mechanic") == "trees"))
                    Assert.That((V(trees, "exposedAtCall"), V(trees, "within"), V(trees, "avoided")), Is.EqualTo(("-", "-", "-")), "no area");
                Assert.That(sim.Rows.Where(r => Tag(r) == RaidAttemptRows.DeathMarker).Select(r => V(r, "killer")),
                    Is.All.AnyOf(UT_RaidSim122GreenKnight.GreenKnightName, UT_RaidSim122GreenKnight.TreeName));
            }
        }

        [Test]
        public void WritingTheRowsChangesNothingInTheFight()
        {
            for (int seed = 0; seed < 4; seed++)
            {
                RoesiaFight plain = RoesiaFight.Run(seed, 93, CureRule.Round2, "albion");
                RoesiaRows(seed, 93, "albion", out RoesiaFight logged);
                Assert.That((logged.Won, logged.Minutes, logged.Lowest, logged.Fallen, logged.Cures, logged.FirstMinutePain, logged.Contributing),
                    Is.EqualTo((plain.Won, plain.Minutes, plain.Lowest, plain.Fallen, plain.Cures, plain.FirstMinutePain, plain.Contributing)),
                    $"Roesia seed {seed}");
                var plainStats = new UT_RaidSim122GreenKnight.Stats();
                var gk = UT_RaidSim122GreenKnight.GreenKnightFight(seed, GkRule.TagBurn, 56, 80.0, true, stats: plainStats);
                var loggedStats = new UT_RaidSim122GreenKnight.Stats();
                var gkLogged = UT_RaidSim122GreenKnight.GreenKnightFight(seed, GkRule.TagBurn, 56, 80.0, true, stats: loggedStats,
                    rows: new SimRaidRows("openworld-albion-greenknight", seed + 1, "open", 1, UT_RaidSim122GreenKnight.GreenKnightName, 40));
                Assert.That(gkLogged, Is.EqualTo(gk), $"Green Knight seed {seed}");
                Assert.That((loggedStats.Contributed, loggedStats.Walks, loggedStats.OnHim.Sum(), loggedStats.Heals.Sum()),
                    Is.EqualTo((plainStats.Contributed, plainStats.Walks, plainStats.OnHim.Sum(), plainStats.Heals.Sum())));
            }
        }

        [Test]
        public void TheRowsUseTheRaidsAndTheScriptsOwnNumbers()
        {
            AutonomousRealmRaid.Definition summoners = AutonomousRealmRaid.Definitions.Single(d => d.Id == "summoners-albion");
            AutonomousRealmRaid.Definition gk = AutonomousRealmRaid.Definitions.Single(d => d.Id == "openworld-albion-greenknight");
            Assert.That(((int)summoners.Region, summoners.SuggestedBots, (int)gk.Region, gk.SuggestedBots), Is.EqualTo((248, 64, 1, 40)));
            var knight = (OFGreenKnight)RuntimeHelpers.GetUninitializedObject(typeof(OFGreenKnight));
            Spell heat = knight.GreenKnightHeatDD;
            Assert.That((heat.ID, heat.Radius, heat.Range, heat.CastTime, heat.SpellType),
                Is.EqualTo((UT_RaidSim122GreenKnight.HeatSpellId, UT_RaidSim122GreenKnight.HeatSpellRadius, 0, 0, eSpellType.DirectDamageNoVariance)),
                "Might of the Forrest: around him (no range), at once");
            var brain = (OFGreenKnightBrain)RuntimeHelpers.GetUninitializedObject(typeof(OFGreenKnightBrain));
            var heal = (Spell)typeof(OFGreenKnightBrain).GetProperty("GreenKnightHeal", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(brain);
            Assert.That(heal.CastTime, Is.EqualTo(UT_RaidSim122GreenKnight.HealCastMs));
            var roesia = (SummonerRoesiaBrain)RuntimeHelpers.GetUninitializedObject(typeof(SummonerRoesiaBrain));
            Spell Of(string property) => (Spell)typeof(SummonerRoesiaBrain).GetProperty(property, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(roesia);
            Assert.That((Of("RoesiaDot").SpellType, Of("RoesiaDS").SpellType), Is.EqualTo((eSpellType.DamageOverTime, eSpellType.DamageShield)));
            // The death's source from those hits, by the server's own rule.
            Assert.That(RaidAttemptRows.Source(new RaidAttemptRows.LastHit(0, false, AttackData.eAttackType.Spell, 1, eSpellType.DamageOverTime, 0), 1_000),
                Is.EqualTo("dot"));
            Assert.That(RaidAttemptRows.Source(new RaidAttemptRows.LastHit(0, false, AttackData.eAttackType.Spell, heat.ID, heat.SpellType, heat.Radius), 1_000),
                Is.EqualTo("aoe"));
            Assert.That(SimRaidRows.Headed(61_500, "X"), Is.EqualTo("01/01 00:01:01 | INF | DOL.GS.LivingRows | X"),
                "the server log's header, which living_report.py and raid_postmortem.py read");
        }

        [Test]
        public void TheSimulatedRowsAreWrittenForTheServerJobsCheck()
        {
            var attempts = new List<SimRaidRows>();
            for (int seed = 0; seed < 3; seed++)
            {
                attempts.Add(RoesiaRows(seed, 93, "albion", out _));
                attempts.Add(GreenKnightRows(seed, 56, seed + 1, out _));
            }
            string path = SimRaidRows.Write("raid-sims", attempts);
            TestContext.WriteLine(path);
            string[] lines = File.ReadAllLines(path);
            Assert.That(lines, Has.Length.EqualTo(attempts.Sum(a => a.Rows.Count)));
            var header = new Regex(@"^\d\d/\d\d \d\d:\d\d:\d\d \| INF \| DOL\.GS\.LivingRows \| RAID_[A-Z_]+ ");
            Assert.That(lines, Has.All.Matches<string>(l => header.IsMatch(l)));
            Assert.That(lines.Select(l => l[..14]), Is.Ordered.Using((IComparer<string>)StringComparer.Ordinal), "in time order, as a log");
        }
    }
}
