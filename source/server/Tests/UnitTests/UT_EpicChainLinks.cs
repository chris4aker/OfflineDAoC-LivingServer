using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using DOL.Database;
using DOL.GS;
using DOL.GS.Quests;
using DOL.GS.ServerProperties;
using DOL.UnitTests.QuestHarness;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// The epic chains linked by the server as it reads the quest rows (EpicChainLinks, QUESTING.md defects 4 and 14,
    /// Living Server 1.2): epic_chains.py's plan ported to C#, checked on the same small world its own tests build
    /// (tools/claude-version/tests/test_epic_chains.py), with the same expected values, and never written to the world.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_EpicChainLinks
    {
        private static readonly Dictionary<string, int> Levels = new()
        {
            ["7"] = 7, ["7closed"] = 7, ["7si"] = 7, ["11"] = 11, ["11si"] = 11, ["15"] = 15, ["20"] = 20, ["25"] = 25, ["30"] = 30,
            ["40"] = 40, ["43"] = 43, ["45"] = 45, ["48"] = 48,
        };

        private static readonly Dictionary<string, string> Names = new()
        {
            ["7"] = "Traveler's Way -- Supply Run", ["7closed"] = "Traveler's Way -- Supply Run", ["7si"] = "Strange Beings",
            ["11"] = "Entry Into Tomorrow", ["11si"] = "Shades and Shadows", ["15"] = "Rebellion Accepted", ["20"] = "Path of the Renegade",
            ["25"] = "Regal Nobility", ["30"] = "Regal Nobility", ["40"] = "Hidden Insurrection", ["43"] = "Lord of Deceit",
            ["45"] = "Lord of Deceit", ["48"] = "Lord of Deceit",
        };

        private bool _linked;
        private string _noteFolder;

        [SetUp]
        public void SetUp()
        {
            _linked = Properties.EPIC_CHAINS_LINKED;
            _noteFolder = EpicChainLinks.NoteFolder;
            Properties.EPIC_CHAINS_LINKED = true;
            EpicChainLinks.NoteFolder = null;
            EpicChainLinks.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            Properties.EPIC_CHAINS_LINKED = _linked;
            EpicChainLinks.NoteFolder = _noteFolder;
            EpicChainLinks.Reset();
        }

        private static JsonElement Data => EpicChainLinks.Data;

        private static IEnumerable<(string Key, int Column, int Id, int ClassId)> StepIds()
        {
            List<int> classIds = Data.GetProperty("classes").EnumerateObject().Select(p => p.Value.GetInt32()).ToList();
            foreach (JsonProperty step in Data.GetProperty("steps").EnumerateObject())
            {
                int col = 0;
                foreach (JsonElement id in step.Value.GetProperty("ids").EnumerateArray())
                {
                    if (id.ValueKind != JsonValueKind.Null)
                        yield return (step.Name, col, id.GetInt32(), classIds[col]);
                    col++;
                }
            }
        }

        /// <summary>test_epic_chains.py's build(): the Guild of Shadows rows with the clean world's keys, plus a line to pin.</summary>
        private static List<DbDataQuest> CleanRows()
        {
            var rows = new List<DbDataQuest>();
            foreach ((string key, _, int id, int classId) in StepIds())
            {
                rows.Add(new DbDataQuest
                {
                    ID = id, Name = Names[key], MinLevel = (byte)Levels[key], MaxLevel = 50, AllowedClasses = classId.ToString(),
                    QuestDependency = Data.GetProperty("steps").GetProperty(key).GetProperty("upstream_dependency").GetString(),
                    ClassType = EpicChainLinks.ClassicType, StepType = "4|5", SourceText = "a|b",
                });
            }
            foreach (JsonElement fix in Data.GetProperty("rewards").EnumerateArray())
                rows.Single(r => r.ID == fix.GetProperty("id").GetInt32()).FinalRewardItemTemplates = fix.GetProperty("upstream").GetString();
            foreach (DbDataQuest row in rows.Where(r => r.ID is 21489 or 21490 or 21491 or 21492 or 21493))
                row.SourceText = "Let this (reward) be yours, (class name) of (prof name).|";
            foreach ((int id, int level, string dependency) in new[] { (30001, 20, ""), (30002, 25, "Legend of the Lake"), (30003, 30, "Legend of the Lake") })
            {
                rows.Add(new DbDataQuest
                {
                    ID = id, Name = id < 30003 ? "Legend of the Lake" : "Hands Of Fate", MinLevel = (byte)level, MaxLevel = 50,
                    AllowedClasses = "2", QuestDependency = dependency, ClassType = EpicChainLinks.ClassicType,
                });
            }
            foreach (DbDataQuest row in rows)
                row.Dirty = false;
            return rows;
        }

        private static DbDataQuest Row(IEnumerable<DbDataQuest> rows, int id) => rows.Single(r => r.ID == id);

        [Test]
        public void TheDataFileIsBuiltIntoTheServer()
        {
            Assert.That(Data.GetProperty("classes").EnumerateObject().Count(), Is.EqualTo(5));
            Assert.That(Data.GetProperty("steps").EnumerateObject().Count(), Is.EqualTo(13));
        }

        [Test]
        public void ThePlanIsEpicChainsPysOnItsOwnTestWorld()
        {
            EpicChainLinks.Plan plan = EpicChainLinks.MakePlan(CleanRows(), Data);
            Assert.That(plan.Summary(), Does.StartWith("links 60, pins 2, closed 4, rewards 2, texts 5, dialogue 0"),
                "the counts test_epic_chains.py expects on this world");
        }

        [Test]
        public void LinkingSetsTheValuesEpicChainsPyWrites()
        {
            List<DbDataQuest> rows = CleanRows();
            EpicChainLinks.Link(rows);
            Assert.Multiple(() =>
            {
                Assert.That(Row(rows, 30002).QuestDependency, Is.EqualTo("#30001"), "a dependency on its own name points at the step before");
                Assert.That(Row(rows, 30003).QuestDependency, Is.EqualTo("#30002"), "a name at two levels points at the higher one below");
                Assert.That(Row(rows, 20188).QuestDependency, Is.EqualTo("#20157/20469"));
                Assert.That(Row(rows, 20157).QuestDependency, Is.EqualTo("#21500/21324|!#20469"));
                Assert.That(Row(rows, 21324).QuestDependency, Is.EqualTo("#21324"), "the second Supply Run is offered to no one");
                Assert.That(Row(rows, 20159).FinalRewardItemTemplates, Is.EqualTo("cq_alb_flayed_skin_necklace"));
                Assert.That(Row(rows, 21489).SourceText, Is.EqualTo("Let this reward be yours, <Class> of the Guild of Shadows.|"));
                Assert.That(rows.All(r => !r.Dirty), Is.True, "nothing marks a linked row as changed, so nothing saves it");
            });
        }

        /// <summary>test_epic_chains.py's walk(): with QuestDependencies' own rule, every class can take the 7 or the
        /// Shrouded Isles 7, then exactly the next step of its branch, to 48.</summary>
        [Test]
        public void EveryGuildOfShadowsClassWalksItsChainInOrder()
        {
            List<DbDataQuest> rows = CleanRows();
            EpicChainLinks.Link(rows);
            Dictionary<int, DbDataQuest> byId = rows.ToDictionary(r => r.ID);
            List<string> classNames = Data.GetProperty("classes").EnumerateObject().Select(p => p.Name).ToList();
            var steps = Data.GetProperty("steps").EnumerateObject().ToDictionary(p => p.Name,
                p => p.Value.GetProperty("ids").EnumerateArray().Select(i => i.ValueKind == JsonValueKind.Null ? (int?)null : i.GetInt32()).ToArray());

            for (int col = 0; col < classNames.Count; col++)
            {
                int column = col;
                var ids = steps.ToDictionary(s => s.Key, s => s.Value[column]);
                var mine = ids.Values.Where(i => i != null).Select(i => i.Value).ToList();

                bool Can(int quest, HashSet<int> done)
                {
                    if (done.Contains(quest))
                        return false;
                    var entries = (byId[quest].QuestDependency ?? string.Empty).Split('|', StringSplitOptions.RemoveEmptyEntries).ToList();
                    return QuestDependencies.AreMet(entries, done.Select(d => byId[d].Name).ToList(), done, new HashSet<int>());
                }

                foreach (string[] route in new[] { new[] { "7", "11" }, new[] { "7si", "11si" } })
                {
                    var done = new HashSet<int>();
                    Assert.That(mine.Where(q => Can(q, done)).OrderBy(q => q), Is.EqualTo(new[] { ids["7"].Value, ids["7si"].Value }.OrderBy(q => q)), classNames[col]);
                    foreach (string key in route.Concat(new[] { "15", "20", "25", "30", "40", "43", "45", "48" }))
                    {
                        if (!route.Contains(key))
                            Assert.That(mine.Where(q => Can(q, done)), Is.EqualTo(new[] { ids[key].Value }), $"{classNames[col]} {string.Join("/", route)} {key}");
                        done.Add(ids[key].Value);
                    }
                    Assert.That(mine.Where(q => Can(q, done)), Is.Empty, classNames[col]);
                }
                if (ids["7closed"] is int closed)
                    Assert.That(Can(closed, new HashSet<int>()), Is.False, $"{classNames[col]}: the closed Supply Run is offered");
            }
        }

        [Test]
        public void ARowSomeoneChangedIsLeftAlone()
        {
            List<DbDataQuest> rows = CleanRows();
            Row(rows, 20455).QuestDependency = "Something Else";
            EpicChainLinks.Link(rows);
            Assert.That(Row(rows, 20455).QuestDependency, Is.EqualTo("Something Else"));
        }

        [Test]
        public void AWorldEpicChainsPyWasAppliedToReadsTheSame()
        {
            List<DbDataQuest> applied = CleanRows();
            EpicChainLinks.Link(applied);
            var once = applied.Select(r => (r.ID, r.QuestDependency, r.SourceText, r.FinalRewardItemTemplates)).ToList();

            // Rows that already hold the links (epic_chains.py --apply ran on this world): linking changes nothing.
            EpicChainLinks.Plan plan = EpicChainLinks.MakePlan(applied, Data);
            Assert.That(plan.Rows, Is.Empty, plan.Summary());
            EpicChainLinks.Link(applied);
            Assert.That(applied.Select(r => (r.ID, r.QuestDependency, r.SourceText, r.FinalRewardItemTemplates)), Is.EqualTo(once));
        }

        [Test]
        public void AWorldTheLinksWereWrittenIntoIsNotedForTheRollback()
        {
            // QUESTING.md defect 14: ROLLBACK.cmd refuses while runtime\server\epic-chains-in-world.txt is there, since
            // the released server reads written links as quest names. The server notes such a world and clears the
            // note when the rows hold no links.
            string folder = TemporarySqlite.NewFolder("epic-chains-note");
            try
            {
                EpicChainLinks.NoteFolder = folder;
                string note = Path.Combine(folder, EpicChainLinks.NoteFile);
                List<DbDataQuest> written = CleanRows();
                EpicChainLinks.Link(written);         // as epic_chains.py --apply leaves the rows
                Assert.That(File.Exists(note), Is.False, "the clean world: nothing written into it");

                EpicChainLinks.Link(written.Select(QuestWorld.Copy).ToList());
                Assert.That(EpicChainLinks.Current.LinkedInWorld, Is.EqualTo(60));
                Assert.That(File.Exists(note), Is.True, "rows that hold the links are noted");
                Assert.That(File.ReadAllText(note), Does.Contain("epic_chains.py rollback --apply"));

                EpicChainLinks.Link(CleanRows());
                Assert.That(File.Exists(note), Is.False, "put back: the note goes");
            }
            finally
            {
                TemporarySqlite.DeleteFolder(folder);
            }
        }

        [Test]
        public void DialogueIsWrittenOnlyOverAGuardedText()
        {
            // test_dialogue_is_written_only_over_a_guarded_text, on this port.
            JsonObject data = JsonNode.Parse(Data.GetRawText())!.AsObject();
            JsonArray dialogue = data["dialogue"]!.AsArray();
            JsonNode entry = dialogue.First(e => (string)e!["step"] == "15")!.DeepClone();
            int[] ids = data["steps"]!["15"]!["ids"]!.AsArray().Where(i => i != null).Select(i => (int)i!).ToArray();
            List<DbDataQuest> rows = CleanRows();
            foreach (int id in ids)
                Row(rows, id).StepType = "4|6|6|7";
            List<string> columns = entry["set"]!.AsObject().Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal).ToList();
            DbDataQuest first = Row(rows, ids[0]);
            // What the row holds by then: the links are made first, and QuestDependency is one of the dialogue's columns.
            EpicChainLinks.Plan before = EpicChainLinks.MakePlan(rows, Data);
            var held = columns.Select(c => before.Rows.Where(r => r.Id == first.ID && r.Column == c).Select(r => r.New).LastOrDefault()
                ?? (string)typeof(DbDataQuest).GetProperty(c)!.GetValue(first) ?? string.Empty).ToList();
            entry["guard"] = new JsonObject { [ids[0].ToString()] = new JsonArray(EpicChainLinks.Digest(held)) };
            data["dialogue"] = new JsonArray(entry);

            using JsonDocument changed = JsonDocument.Parse(data.ToJsonString());
            EpicChainLinks.Plan plan = EpicChainLinks.MakePlan(rows, changed.RootElement);
            var written = plan.Rows.Select(r => r.Id).ToHashSet();
            Assert.That(written, Does.Contain(ids[0]));
            Assert.That(plan.Kept.OrderBy(i => i), Is.EqualTo(ids.Skip(1).OrderBy(i => i)));
        }

        [Test]
        public void TheGuardDigestIsPythonsJsonDumps()
        {
            // python -c "import epic_chains as t; print(t.digest([...]))" from tools/claude-version.
            Assert.That(EpicChainLinks.Digest(new[] { "a", null, "Say \"hi\"\\ there\nnext\tline\u0001", "café — <Player>" }),
                Is.EqualTo("cfdc1856906b9d2ec7913c9c54a24278a8e26a49b18ed9a0cc2e4a5353bfd516"));
            Assert.That(EpicChainLinks.Digest(Array.Empty<string>()), Is.EqualTo("4f53cda18c2baa0c0354bb5f9a3ecbe5ed12ab4d8e11ba873c2f11161202b945"));
        }

        [Test]
        public void TheKillSwitchReadsTheRowsAsTheyAre()
        {
            Properties.EPIC_CHAINS_LINKED = false;
            List<DbDataQuest> rows = CleanRows();
            EpicChainLinks.Link(rows);
            Assert.That(Row(rows, 20188).QuestDependency, Is.EqualTo("Entry Into Tomorrow"));
        }

        [Test]
        public void TheSwitchIsOnByDefault()
        {
            var attribute = (ServerPropertyAttribute)typeof(Properties).GetField(nameof(Properties.EPIC_CHAINS_LINKED))!
                .GetCustomAttributes(typeof(ServerPropertyAttribute), false).Single();
            Assert.That(attribute.Key, Is.EqualTo("epic_chains_linked"));
            Assert.That(attribute.DefaultValue, Is.EqualTo(true));
        }

        // On the server: the data quest cache and a player's quest at login link the rows, and the world is not written.

        [Test]
        public void TheServerLinksTheChainsAsItReadsTheRowsAndNeverWritesThem()
        {
            List<DbDataQuest> rows = CleanRows();
            var world = new QuestWorld();
            foreach (DbDataQuest row in rows)
                world.Add(row);
            using var harness = new QuestHarness.QuestHarness(world);
            GameObject.FillDataQuestCache();
            DbDataQuest cached = GameObject.DataQuestCache.Single(r => r.ID == 20188);
            Assert.Multiple(() =>
            {
                Assert.That(cached.QuestDependency, Is.EqualTo("#20157/20469"), "the offers read the linked row");
                Assert.That(EpicChainLinks.Current, Is.Not.Null);
                Assert.That(world.WorldWrites, Is.Empty, "no quest row is written");
            });

            // A row read on its own (a player's quest at login) is linked by the same plan.
            var single = new DbDataQuest { ID = 20455, Name = "Path of the Renegade", QuestDependency = "Rebellion Accepted", ClassType = EpicChainLinks.ClassicType };
            Assert.That(EpicChainLinks.Link(single).QuestDependency, Is.EqualTo("#20188"));
            Assert.That(world.WorldWrites, Is.Empty);
        }

        [Test]
        public void TheGuildOfShadowsChainIsShownByEpicOnACleanWorld()
        {
            // QUESTING.md defect 4: /epic said "no epic chain linked by quest IDs" on a clean world. EpicChain.ChainFor is
            // what /epic asks; the rows it reads now carry the links.
            List<DbDataQuest> rows = CleanRows();
            Assert.That(EpicChain.ChainFor(Quests(rows), (int)eCharacterClass.Infiltrator), Is.Empty, "the clean world's rows: no chain");
            EpicChainLinks.Link(rows);
            IReadOnlyList<EpicQuest> chain = EpicChain.ChainFor(Quests(rows), (int)eCharacterClass.Infiltrator);
            Assert.That(chain.Select(q => q.MinLevel).Distinct(), Is.EqualTo(new[] { 7, 11, 15, 20, 25, 30, 40, 43, 45, 48 }));
        }

        private static IEnumerable<EpicQuest> Quests(IEnumerable<DbDataQuest> rows) => rows.Select(r => new EpicQuest(r.ID, r.Name, r.MinLevel, r.MaxLevel,
            r.StartRegionID, EpicChain.ParseClasses(r.AllowedClasses), EpicChain.ParseDependencies(r.QuestDependency)));
    }
}
