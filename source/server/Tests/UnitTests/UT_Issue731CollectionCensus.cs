using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Issue #731 (owner answer A, 2026-11-28): SERVER_COLLECTION_CENSUS, the sizes of the long-lived static collections
    /// with the thread, timer and GC generation figures, every 30 minutes from the SERVER_MEMORY report. Tests only.
    /// </summary>
    [TestFixture]
    public class UT_Issue731CollectionCensus
    {
        private static readonly string[] HeaderKeys =
            ["collections", "unresolved", "threads", "poolThreads", "poolPending", "timers", "gen0MB", "gen1MB", "gen2MB",
             "lohMB", "pohMB", "gen2Collections", "ms", "weakMs"];

        /// <summary>The stores the issue's spec names (last comment on #731).</summary>
        private static readonly string[] SpecNames =
        [
            "RvrFightBoards.Groups", "RvrFightBoards.holders", "BotBrain.FightBoards", "BotBrain.fightBoardHolders",
            "RvrContactTable.owners", "RvrContactTable.realmRows",
            "AutonomousRvrEventLayer.ForceIntents",
            "AutonomousRvrEventLayer.Battlegroups", "AutonomousRvrEventLayer.BattlegroupOfForce",
            "ChatConversations.Live",
            "AutonomousRvrEventLayer.ClaimHolds", "AutonomousMobClaims.Claims",
            "AutonomousBotGroupCoordinator.Sessions", "AutonomousBotGroupCoordinator.RoleFormations",
            "CompanionDarknessFallsPaths.NextLog",
            "BotDutyLeases.Held", "BotDutyLeases.LastRefusal",
        ];

        private static List<string> Names(IEnumerable<string> lines) =>
            lines.SelectMany(line =>
            {
                Match counts = Regex.Match(line, "counts=\"([^\"]*)\"");
                Assert.That(counts.Success, line);
                return counts.Groups[1].Value.Length == 0
                    ? Enumerable.Empty<string>()
                    : counts.Groups[1].Value.Split(',').Select(pair => pair[..pair.LastIndexOf('=')]);
            }).ToList();

        [Test]
        public void Every_registered_store_resolves_to_a_countable_static_field()
        {
            var problems = new List<string>();
            foreach (ServerCollectionCensus.Entry entry in ServerCollectionCensus.Registry)
            {
                if (entry.TypeName == null) continue; // custom: counted by code that compiles
                FieldInfo field = entry.Resolve();
                if (field == null) { problems.Add($"{entry.Name}: {entry.TypeName}.{entry.FieldName} not found"); continue; }
                Type type = field.FieldType;
                if (entry.IsWeak)
                {
                    if (!type.IsGenericType || type.GetGenericTypeDefinition() != typeof(ConditionalWeakTable<,>))
                        problems.Add($"{entry.Name}: weak entry is a {type.Name}");
                }
                else
                {
                    PropertyInfo count = type.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                    bool countable = typeof(ICollection).IsAssignableFrom(type) ||
                                     count != null && (count.PropertyType == typeof(int) || count.PropertyType == typeof(long));
                    if (!countable) problems.Add($"{entry.Name}: a {type.Name} has no Count");
                }
            }
            Assert.That(problems, Is.Empty, string.Join("\n", problems));
        }

        [Test]
        public void Names_are_unique_code_names_and_include_every_store_the_spec_lists()
        {
            List<string> names = ServerCollectionCensus.Registry.Select(e => e.Name).ToList();
            Assert.That(names, Is.Unique);
            Assert.That(names, Has.All.Match(@"^[A-Za-z_][A-Za-z0-9_]*\.[A-Za-z_][A-Za-z0-9_]*$"));
            Assert.That(names, Is.SupersetOf(SpecNames));
            Assert.That(names.Count, Is.GreaterThan(300), "the bot code's static stores, not only the spec's");
        }

        [Test]
        public void The_row_writes_every_listed_name_within_the_line_limit()
        {
            var counts = ServerCollectionCensus.Registry.Select((e, i) => (e.Name, (long)i * 1_000)).ToList();
            List<string> lines = ServerCollectionCensus.Lines(new ServerCollectionCensus.Header(), counts);
            Assert.That(Names(lines), Is.EqualTo(counts.Select(c => c.Name)));
            Assert.That(lines.Select(l => l.Length), Has.All.LessThanOrEqualTo(ServerCollectionCensus.MaxLineChars));
            Assert.That(lines.Count, Is.GreaterThan(1), "the registry is longer than one line");
            for (int i = 0; i < lines.Count; i++)
                Assert.That(lines[i], Does.StartWith($"SERVER_COLLECTION_CENSUS part={i + 1}/{lines.Count} "));
            foreach (string key in HeaderKeys)
            {
                Assert.That(lines[0], Does.Contain($" {key}="));
                foreach (string later in lines.Skip(1))
                    Assert.That(later, Does.Not.Contain($" {key}="));
            }
        }

        [Test]
        public void Unreadable_stores_are_written_with_a_question_mark_and_counted()
        {
            var counts = new List<(string, long)> { ("A.one", 3), ("B.two", -1), ("C.three", 0) };
            List<string> lines = ServerCollectionCensus.Lines(new ServerCollectionCensus.Header(), counts);
            Assert.That(lines, Has.Count.EqualTo(1));
            Assert.That(lines[0], Does.Contain("collections=3 unresolved=1 "));
            Assert.That(lines[0], Does.EndWith("counts=\"A.one=3,B.two=?,C.three=0\""));
        }

        [Test]
        public void A_long_row_splits_into_numbered_parts_without_losing_a_name()
        {
            var counts = Enumerable.Range(0, 200).Select(i => ($"Store{i}.Field", (long)i)).ToList();
            List<string> lines = ServerCollectionCensus.Lines(new ServerCollectionCensus.Header(), counts, maxChars: 600);
            Assert.That(lines.Count, Is.GreaterThan(3));
            Assert.That(lines.Select(l => l.Length), Has.All.LessThanOrEqualTo(600));
            Assert.That(Names(lines), Is.EqualTo(counts.Select(c => c.Item1)));
        }

        [Test]
        public void Counts_read_Count_and_never_fail()
        {
            Assert.That(ServerCollectionCensus.CountOf(null), Is.EqualTo(0));
            Assert.That(ServerCollectionCensus.CountOf(new Dictionary<int, int> { [1] = 1, [2] = 2 }), Is.EqualTo(2));
            Assert.That(ServerCollectionCensus.CountOf(new HashSet<string> { "a" }), Is.EqualTo(1));
            Assert.That(ServerCollectionCensus.CountOf(new ConcurrentDictionary<long, long>(new[] { KeyValuePair.Create(1L, 1L) })), Is.EqualTo(1));
            Assert.That(ServerCollectionCensus.CountOf(new object()), Is.EqualTo(-1));
            Assert.That(ServerCollectionCensus.Custom("X.y", () => throw new InvalidOperationException()).Count(), Is.EqualTo(-1));
            Assert.That(ServerCollectionCensus.Field("DOL.GS.NoSuchType", "Nothing").Count(), Is.EqualTo(-1));
            Assert.That(ServerCollectionCensus.Field("DOL.GS.BotDutyLeases", "Held").Count(), Is.GreaterThanOrEqualTo(0));
            Assert.That(ServerCollectionCensus.Field("DOL.GS.BotDutyLeases", "Held").Name, Is.EqualTo("BotDutyLeases.Held"));
        }

        [Test]
        public void The_header_reads_threads_timers_and_generations()
        {
            var header = ServerCollectionCensus.Header.Now(GC.GetGCMemoryInfo());
            Assert.That(header.PoolThreads, Is.GreaterThanOrEqualTo(0));
            Assert.That(header.Timers, Is.GreaterThanOrEqualTo(0));
            Assert.That(header.Gen2Collections, Is.GreaterThanOrEqualTo(0));
            Assert.That(header.GenerationMegabytes, Has.Length.EqualTo(5));
        }

        [Test]
        public void It_runs_on_the_first_memory_report_and_every_sixth_after_it()
        {
            Assert.That(Enumerable.Range(1, 20).Where(ServerCollectionCensus.IsDue), Is.EqualTo(new[] { 1, 7, 13, 19 }));
            Assert.That(ServerCollectionCensus.IsDue(0), Is.False);
            Assert.That(ServerMemoryDiagnostics.ReportMinutes * ServerCollectionCensus.EveryMemoryReports, Is.EqualTo(30));
        }

        [Test]
        public void Source_pin_the_SERVER_MEMORY_report_starts_the_census_in_every_session()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !Directory.Exists(Path.Combine(dir, "source", "server", "GameServer")))
                dir = Path.GetDirectoryName(dir);
            Assert.That(dir, Is.Not.Null);
            string code = File.ReadAllText(Path.Combine(dir, "source", "server", "GameServer", "Managers", "GameLoop", "ServerMemoryDiagnostics.cs"));
            int report = code.IndexOf("private static void Report(GCMemoryInfo info)", StringComparison.Ordinal);
            int call = code.IndexOf("ServerCollectionCensus.OnMemoryReport(info)", StringComparison.Ordinal);
            int censusOnly = code.IndexOf("CensusEnableFileName))) return;", report, StringComparison.Ordinal);
            Assert.That(report, Is.GreaterThan(0));
            Assert.That(call, Is.GreaterThan(report), "called from the five-minute SERVER_MEMORY report");
            Assert.That(call, Is.LessThan(censusOnly), "before the memory-census.on early return: on in every session");
            Assert.That(code, Does.Contain("if (++_minutes % ReportMinutes != 0) return;"));
        }
    }
}
