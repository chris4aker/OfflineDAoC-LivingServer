using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// The lore bible (Living Server 1.2, docs/LORE.md). <see cref="EveryLoreFilePassesTheSchema"/> is the check every
    /// writer's files must pass: it reads data/lore as committed and validates it against the encounter catalog.
    /// </summary>
    [TestFixture]
    public class UT_LoreBible
    {
        private static string RepoRoot()
        {
            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "docs", "encounters", "epic-encounters.json")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Repository not found beside the tests.");
            return dir;
        }

        /// <summary>
        /// The lore data the schema test reads: data/lore as committed, or the folder in LORE_DATA_DIR (to check a writer's
        /// branch from a checkout of it, read-only). LORE_REPORT names a file for every problem and warning.
        /// </summary>
        private static string LoreDir(string root) =>
            Environment.GetEnvironmentVariable("LORE_DATA_DIR") is { Length: > 0 } other ? other
                : Path.Combine(root, "source", "server", "GameServer", "bots", "autonomous", "data", "lore");

        private static bool OtherData => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LORE_DATA_DIR"));

        /// <summary>What a warning is about, for the counts the schema test prints.</summary>
        public static string WarningCause(string warning) => warning switch
        {
            _ when warning.Contains(": game talk in ") => "game talk in in-world text",
            _ when warning.Contains("history file yet") => "era/faction link before the realm's history file",
            _ when warning.Contains("a link to a file not in this set") => "link to a place or boss not in this set",
            _ when warning.Contains("words (at least") => "shorter than the floor",
            _ when warning.Contains("words (over") => "long for one sitting",
            _ when warning.Contains("words (250-700") => "chapter length",
            _ when warning.Contains("volumes of") => "book bound in volumes",
            _ when warning.Contains("only summoners") => "faction id in two realms' files",
            _ => "other",
        };

        /// <summary>
        /// The tests' own Krondon and Scurceol Hyrde (inventory sample 2), so they do not change when the writers' files
        /// replace the shipped ones. They must pass the same checks.
        /// </summary>
        private static string FixtureDir(string root) => Path.Combine(root, "source", "server", "Tests", "UnitTests", "LoreFixtures");

        private static IEnumerable<(string Path, string Json)> DiskFiles(string root)
        {
            string dir = LoreDir(root);
            return Directory.GetFiles(dir, "*.json", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.Ordinal)
                .Select(f => (Path.GetRelativePath(dir, f).Replace('\\', '/'), File.ReadAllText(f)));
        }

        private static LoreBible.Catalog CatalogFrom(string root)
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs", "encounters", "epic-encounters.json")));
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var adds = new Dictionary<string, string[]>(StringComparer.Ordinal);
            var partners = new Dictionary<string, string[]>(StringComparer.Ordinal);
            foreach (JsonElement e in document.RootElement.GetProperty("encounters").EnumerateArray())
            {
                ids.Add(e.GetProperty("id").GetString());
                string cls = e.GetProperty("class").GetString().ToLowerInvariant();
                string[] names = e.TryGetProperty("adds", out JsonElement a) && a.ValueKind == JsonValueKind.Array
                    ? a.EnumerateArray().Select(x => x.GetProperty("class").GetString().ToLowerInvariant()).ToArray()
                    : [];
                if (e.TryGetProperty("spawnsEncounters", out JsonElement spawned) && spawned.ValueKind == JsonValueKind.Array)
                    names = names.Concat(spawned.EnumerateArray().Select(x => x.GetString().ToLowerInvariant())).ToArray();
                adds[cls] = (adds.TryGetValue(cls, out string[] had) ? had : []).Concat(names).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
                if (e.TryGetProperty("partners", out JsonElement mates) && mates.ValueKind == JsonValueKind.Array && mates.GetArrayLength() > 0)
                    partners[cls] = (partners.TryGetValue(cls, out string[] known) ? known : [])
                        .Concat(mates.EnumerateArray().Select(x => x.GetString().ToLowerInvariant())).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            }
            // As tools/lore/build_encounter_adds.py: "<Boss>Add" and a "...SpawnedAdd" naming its boss as partner are adds too.
            foreach (JsonElement e in document.RootElement.GetProperty("encounters").EnumerateArray())
            {
                string cls = e.GetProperty("class").GetString();
                var bosses = new List<string>();
                if (cls.EndsWith("Add", StringComparison.Ordinal) && adds.ContainsKey(cls[..^3].ToLowerInvariant()))
                    bosses.Add(cls[..^3].ToLowerInvariant());
                if (cls.EndsWith("SpawnedAdd", StringComparison.Ordinal) && e.TryGetProperty("partners", out JsonElement p) && p.ValueKind == JsonValueKind.Array)
                    bosses.AddRange(p.EnumerateArray().Select(x => x.GetString().ToLowerInvariant()));
                foreach (string boss in bosses)
                    adds[boss] = (adds.TryGetValue(boss, out string[] had) ? had : []).Append(cls.ToLowerInvariant()).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
            }
            return new LoreBible.Catalog(ids, adds, partners);
        }

        private static LoreBible Fixture()
        {
            string root = RepoRoot();
            string dir = FixtureDir(root);
            return LoreBible.Parse(new[] { "locations/krondon.json", "bosses/scurceol-hyrde.json", "history/albion.json" }
                .Select(p => (p, File.ReadAllText(Path.Combine(dir, p)))));
        }

        /// <summary>Krondon and Scurceol Hyrde as committed, changed by <paramref name="edit"/> (a JSON tweak), for the failure cases.</summary>
        private static List<string> ProblemsWith(Func<string, string> editLocation = null, Func<string, string> editBoss = null,
            params (string Path, string Json)[] extra) => ProblemsWith(editLocation, editBoss, null, null, extra);

        private static List<string> ProblemsWith(Func<string, string> editLocation, Func<string, string> editBoss, Func<string, string> editHistory,
            List<string> warnings, params (string Path, string Json)[] extra)
        {
            string root = RepoRoot();
            string dir = FixtureDir(root);
            string history = File.ReadAllText(Path.Combine(dir, "history/albion.json"));
            string location = File.ReadAllText(Path.Combine(dir, "locations/krondon.json"));
            string boss = File.ReadAllText(Path.Combine(dir, "bosses/scurceol-hyrde.json"));
            var files = new List<(string, string)>
            {
                ("locations/krondon.json", editLocation?.Invoke(location) ?? location),
                ("bosses/scurceol-hyrde.json", editBoss?.Invoke(boss) ?? boss),
            };
            files.Add(("history/albion.json", editHistory?.Invoke(history) ?? history));
            files.AddRange(extra);
            return LoreBible.Parse(files).Validate(CatalogFrom(root), warnings);
        }

        #region The writers' check

        [Test]
        public void EveryLoreFilePassesTheSchema()
        {
            string root = RepoRoot();
            LoreBible bible = LoreBible.Parse(DiskFiles(root));
            if (!OtherData)
                Assert.That(bible.Locations.Length, Is.GreaterThan(0), "no lore locations found");
            var warnings = new List<string>();
            List<string> problems = bible.Validate(CatalogFrom(root), warnings);
            // The depth floor (LORE-1.2-DEPTH.md) and short texts are advisory: printed so the depth can be tracked.
            TestContext.Out.WriteLine($"LORE_DEPTH {bible.Locations.Length} locations, {bible.Bosses.Length} bosses, {bible.Histories.Length} history files, " +
                                      $"{warnings.Count} length warnings");
            foreach (string line in bible.DepthReport())
                TestContext.Out.WriteLine("LORE_DEPTH " + line);
            foreach (var cause in warnings.GroupBy(WarningCause).OrderByDescending(g => g.Count()))
                TestContext.Out.WriteLine($"LORE_WARNINGS {cause.Count()} {cause.Key}");
            foreach (string warning in warnings.Take(40))
                TestContext.Out.WriteLine("LORE_WARNING " + warning);
            if (Environment.GetEnvironmentVariable("LORE_REPORT") is { Length: > 0 } report)
                File.WriteAllLines(report, problems.Select(p => "PROBLEM " + p).Concat(warnings.Select(w => $"WARNING [{WarningCause(w)}] {w}")));
            Assert.That(problems, Is.Empty, "docs/LORE.md lists the rules:\n" + string.Join("\n", problems));
        }

        [Test]
        public void TheServerCarriesEveryLoreFile()
        {
            if (OtherData)
                Assert.Ignore("LORE_DATA_DIR: checking another branch's data, not this build's");
            string root = RepoRoot();
            string[] disk = DiskFiles(root).Select(f => f.Path).ToArray();
            string[] embedded = LoreBible.EmbeddedFiles().Select(f => f.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
            Assert.That(embedded, Is.EqualTo(disk), "GameServer.csproj embeds data/lore/**/*.json");
            Assert.That(LoreBible.Current.Locations.Length, Is.EqualTo(disk.Count(p => p.StartsWith("locations/"))));
        }

        [Test]
        public void TheAddsMapMatchesTheCatalog()
        {
            string root = RepoRoot();
            var expected = CatalogFrom(root).AddsByClass.Where(p => p.Value.Length > 0).ToDictionary(p => p.Key, p => p.Value);
            var built = LoreBossWatch.ParseAdds(File.ReadAllText(Path.Combine(root, "source", "server", "GameServer", "bots", "autonomous", "data", "encounter_adds.json")));
            Assert.That(built.Keys.OrderBy(k => k, StringComparer.Ordinal), Is.EqualTo(expected.Keys.OrderBy(k => k, StringComparer.Ordinal)),
                "out of date: run python3 tools/lore/build_encounter_adds.py");
            foreach (var pair in expected)
                Assert.That(built[pair.Key], Is.EqualTo(pair.Value), pair.Key + ": run python3 tools/lore/build_encounter_adds.py");
            Assert.That(LoreBossWatch.Adds.ContainsKey("scurceolhyrde"), Is.True, "the map is built into the server");
            var partners = LoreBossWatch.ParseAdds(File.ReadAllText(Path.Combine(root, "source", "server", "GameServer", "bots", "autonomous", "data", "encounter_adds.json")), "partners");
            Assert.That(partners, Is.EquivalentTo(CatalogFrom(root).PartnersByClass), "partners: run python3 tools/lore/build_encounter_adds.py");
            Assert.That(LoreBossWatch.Partners["lordsanguis"], Is.EqualTo(new[] { "lichlordsanguis" }));
        }

        [Test]
        public void ScriptsThatSpeakAreRealBossClasses()
        {
            var catalog = CatalogFrom(RepoRoot());
            foreach (var pair in LoreBossWatch.ScriptSpeaks)
            {
                Assert.That(catalog.AddsByClass.ContainsKey(pair.Key), Is.True, pair.Key);
                Assert.That(pair.Value, Is.SubsetOf(new[] { "aggro", "death", "slay" }), pair.Key);
            }
        }

        #endregion

        #region Parsing and indexes

        [Test]
        public void TheTestFixturesPassTheSchema()
        {
            string root = RepoRoot();
            Assert.That(Fixture().Validate(CatalogFrom(root)), Is.Empty);
        }

        [Test]
        public void TheFixturesAreIndexedByRegionClassAndId()
        {
            LoreBible bible = Fixture();
            Assert.That(bible.ParseProblems, Is.Empty);
            Assert.That(bible.LocationsInRegion(61).Select(l => l.Id), Is.EqualTo(new[] { "krondon" }));
            Assert.That(bible.BossForClass("ScurceolHyrde")?.Id, Is.EqualTo("scurceol-hyrde"));
            Assert.That(bible.HasBossClass("Orylle"), Is.False);
            Assert.That(bible.LocationOf(bible.BossById("scurceol-hyrde"))?.Name, Is.EqualTo("Krondon"));
            Assert.That(bible.BossesAt("krondon").Length, Is.EqualTo(1));
            var area = bible.LocationById("krondon").Areas.Single();
            Assert.That((area.Region, area.Radius, area.Lines.Length), Is.EqualTo(((ushort)61, 900, 1)));
            Assert.That(LoreBible.ClassKeyOf("krondon/scurceolhyrde"), Is.EqualTo("scurceolhyrde"));
            Assert.That(LoreBible.ClassKeyOf("ScurceolHyrde"), Is.EqualTo("scurceolhyrde"));
        }

        [Test]
        public void BrokenJsonOrAFileInTheWrongFolderIsAProblemNotACrash()
        {
            LoreBible bible = LoreBible.Parse(new[] { ("locations/a.json", "{ not json"), ("misc/b.json", "{}"), ("bosses/c.json", "[]") });
            Assert.That(bible.ParseProblems.Count, Is.EqualTo(3));
            Assert.That(bible.Locations, Is.Empty);
        }

        #endregion

        #region What the check catches

        [Test]
        public void AnAtmosphereLineOver60CharactersFails() =>
            Assert.That(ProblemsWith(l => l.Replace("Old words are carved here. The ogres cannot read them.",
                "Old words are carved here, and the ogres who live here cannot read a single one of them.")),
                Has.Some.Contains("max 60"));

        [Test]
        public void AnUnknownProvenanceOrAMissingSourceFails()
        {
            Assert.That(ProblemsWith(l => l.Replace("\"provenance\": \"legend\"", "\"provenance\": \"hearsay\"")), Has.Some.Contains("provenance \"hearsay\""));
            Assert.That(ProblemsWith(l => l.Replace("\"source\": \"docs/RAID-SIZES.md (Freddyshouse, Feb 2003)\"", "\"source\": \"\"")),
                Has.Some.Contains("needs its source"));
        }

        [Test]
        public void ACatalogKeyMustResolve()
        {
            Assert.That(ProblemsWith(editBoss: b => b.Replace("krondon/scurceolhyrde", "krondon/scurceolhyrdee")), Has.Some.Contains("no id or script class"));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("krondon/scurceolhyrde", "ScurceolHyrde")), Is.Empty, "the script class works too");
        }

        [Test]
        public void ALocationIdMustResolveBothWays()
        {
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"locationId\": \"krondon\"", "\"locationId\": \"krondun\"")),
                Has.Some.Contains("locationId \"krondun\""));
            // A boss the location lists that has no file yet (another writer's branch) is a link: a warning, never a failure.
            var warnings = new List<string>();
            Assert.That(ProblemsWith(l => l.Replace("\"bosses\": [\"scurceol-hyrde\"]", "\"bosses\": [\"scurceol-hyrde\", \"orylle\"]"), null, null, warnings), Is.Empty);
            Assert.That(warnings, Has.Some.Contains("boss \"orylle\" has no file").And.Some.Contains("not in this set"));
            warnings.Clear();
            Assert.That(ProblemsWith(l => l.Replace("\"bosses\": [\"scurceol-hyrde\"]", "\"bosses\": []"), null, null, warnings), Is.Empty,
                "a boss names its place; the place's list is an index");
            Assert.That(warnings, Has.Some.Contains("does not list it in \"bosses\""));
        }

        [Test]
        public void DuplicateIdsAndCatalogKeysFail()
        {
            string root = RepoRoot();
            string boss = File.ReadAllText(Path.Combine(FixtureDir(root), "bosses/scurceol-hyrde.json"));
            var problems = ProblemsWith(null, null, ("bosses/scurceol-hyrde-2.json", boss.Replace("\"id\": \"scurceol-hyrde\"", "\"id\": \"scurceol-hyrde-2\"")));
            Assert.That(problems, Has.Some.Contains("catalogKey \"krondon/scurceolhyrde\" is used by"));
            problems = ProblemsWith(null, null, ("bosses/x.json", boss));
            Assert.That(problems, Has.Some.Contains("boss id \"scurceol-hyrde\" is used by"));
            Assert.That(problems, Has.Some.Contains("must be named bosses/scurceol-hyrde.json"));
        }

        [Test]
        public void ChoiceSyntaxAndChatVoiceAreChecked()
        {
            Assert.That(ProblemsWith(l => l.Replace("[bring 50|take a full raid]", "[bring 50 take a full raid]")), Has.Some.Contains("has no \"|\""));
            Assert.That(ProblemsWith(l => l.Replace("[bring 50|take a full raid]", "[bring [50|60]|x]")), Has.Some.Contains("do not nest"));
            Assert.That(ProblemsWith(l => l.Replace("krondon? ogres", "Krondon? ogres")), Has.Some.Contains("lower case"));
            Assert.That(ProblemsWith(l => l.Replace("[bring 50|take a full raid]", "[bring 50|take a FULL raid]")), Is.Empty, "shouting in caps is 2003 chat");
        }

        [Test]
        public void PhaseTriggersMustParseAndAddTriggersNeedAdds()
        {
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"trigger\": \"orbDeath\"", "\"trigger\": \"health<=150\"")),
                Has.Some.Contains("is not health<=N"));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("krondon/scurceolhyrde", "krondon/orylle")), Has.Some.Contains("needs adds"));
        }

        [Test]
        public void UnknownFieldsFail() =>
            Assert.That(ProblemsWith(l => l.Replace("\"summary\":", "\"sumary\": \"x\", \"summary\":")), Has.Some.Contains("unknown field \"sumary\""));

        /// <summary>Hibernia writer (PR #91): umbrella locations over a realm's open world, and shrouded-isles with no region (Gnat).</summary>
        [Test]
        public void UmbrellaLocationsAndLocationsWithoutRegionsPass()
        {
            Assert.That(ProblemsWith(l => l.Replace("\"regions\": [61]", "\"regions\": [61, 200]")), Is.Empty, "an open-world umbrella without zones");
            Assert.That(ProblemsWith(l => l.Replace("\"regions\": [61]", "\"regions\": [61, 200], \"zones\": [204]")), Is.Empty);
            Assert.That(ProblemsWith(l => l.Replace("\"regions\": [61]", "\"regions\": []")), Is.Empty, "no regions; the area names its own");
            Assert.That(ProblemsWith(l => l.Replace("\"regions\": [61]", "\"regions\": []").Replace("\"region\": 61, ", "")),
                Has.Some.Contains("needs its \"region\""));
            Assert.That(ProblemsWith(l => l.Replace("\"radius\": 900", "\"radius\": 24000")), Is.Empty, "a whole-zone area");
            Assert.That(ProblemsWith(l => l.Replace("\"radius\": 900", "\"radius\": 40000")), Has.Some.Contains("radius 40000"));
            LoreBible bible = LoreBible.Parse(new[] { ("locations/isles.json", "{\"id\":\"isles\",\"name\":\"Isles\",\"realm\":\"Neutral\",\"regions\":[]," +
                "\"atmosphere\":{\"areas\":[{\"name\":\"a\",\"region\":51,\"x\":1,\"y\":1,\"radius\":500,\"lines\":[\"x\"]}]}}") });
            Assert.That(bible.AreasInRegion(51).Select(a => a.Location.Id), Is.EqualTo(new[] { "isles" }), "found by the area's region");
            Assert.That(bible.LocationsInRegion(51), Is.Empty);
        }

        [Test]
        public void MythIsAProvenanceWithASource()
        {
            Assert.That(ProblemsWith(l => l.Replace("\"provenance\": \"legend\"", "\"provenance\": \"myth\"")), Is.Empty);
            Assert.That(ProblemsWith(l => l.Replace("\"provenance\": \"legend\", \"source\": \"docs/RAID-SIZES.md (Freddyshouse, Feb 2003)\"",
                "\"provenance\": \"myth\", \"source\": \"\"")), Has.Some.Contains("myth fact needs its source"));
        }

        [Test]
        public void TheWritersTriggersAreKnown()
        {
            foreach (string trigger in new[] { "health<=25", "addsSpawn", "addSpawn", "addsDead", "orbDeath", "death", "wake", "formChange",
                         "playerDeath", "teleport", "berserk", "breath", "glare" })
                Assert.That(ProblemsWith(editBoss: b => b.Replace("\"trigger\": \"orbDeath\"", $"\"trigger\": \"{trigger}\"")), Is.Empty, trigger);
        }

        [Test]
        public void LoremasterKeywordsMustHaveATopicHereOrInABoss()
        {
            Assert.That(ProblemsWith(l => l.Replace("[warrior queen]", "[ogre queen]")), Has.Some.Contains("[ogre queen]"));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"keyword\": \"storm-keeper\"", "\"keyword\": \"storm keeper\"")),
                Has.Some.Contains("[storm-keeper]"));
        }

        [Test]
        public void ChronicleSlotsAreOnlyRealmLeaderAndMinutes() =>
            Assert.That(ProblemsWith(editBoss: b => b.Replace("{minutes} minutes", "{time}")), Has.Some.Contains("slot {time}"));

        [Test]
        public void PartnerDeadNeedsPartners() =>
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"trigger\": \"orbDeath\"", "\"trigger\": \"partnerDead\"")),
                Has.Some.Contains("needs partners"));

        /// <summary>Neutral writer (PR #98): an order file (tier "order") keeps each named member's post, story, yells, lair line and topic.</summary>
        [Test]
        public void OrderFilesKeepTheirNamedMembers()
        {
            const string Order = "\"tier\": \"order\", \"npcNames\": [\"Knight Ain\"], \"members\": [{\"name\": \"Knight Ain\", \"level\": 70, " +
                "\"post\": \"the east ward\", \"story\": \"A knight of the chapter, posted to the east ward.\", \"yells\": {\"aggro\": [\"Kneel.\"], " +
                "\"death\": [\"The Roll...\"], \"slay\": [\"Struck off.\"]}, \"atmosphere\": {\"lairEntry\": [\"A knight stands in the ward.\"]}, " +
                "\"loremasterTopic\": {\"keyword\": \"knight ain\", \"text\": \"A knight of the chapter.\"}}], \"locationId\": \"krondon\"";
            string With(string boss, string order) => boss.Replace("\"locationId\": \"krondon\"", order);
            Assert.That(ProblemsWith(editBoss: b => With(b, Order)), Is.Empty);
            Assert.That(ProblemsWith(editBoss: b => With(b, Order.Replace("\"tier\": \"order\", ", ""))), Has.Some.Contains("belong to a tier \"order\" file"));
            Assert.That(ProblemsWith(editBoss: b => With(b, Order.Replace("[\"Knight Ain\"]", "[\"Knight Haag\"]"))), Has.Some.Contains("one of the file's npcNames"));
            Assert.That(ProblemsWith(editBoss: b => With(b, Order.Replace("\"post\":", "\"rank\": \"x\", \"post\":"))), Has.Some.Contains("unknown field \"rank\""));
            Assert.That(ProblemsWith(editBoss: b => With(b, Order.Replace("A knight stands in the ward.", new string('x', 61)))), Has.Some.Contains("lairEntry"));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"locationId\": \"krondon\"", "\"tier\": \"guild\", \"locationId\": \"krondon\"")),
                Has.Some.Contains("must be minor, major, epic or order"));

            string root = RepoRoot();
            string dir = FixtureDir(root);
            LoreBible bible = LoreBible.Parse(new[]
            {
                ("locations/krondon.json", File.ReadAllText(Path.Combine(dir, "locations/krondon.json"))),
                ("bosses/scurceol-hyrde.json", With(File.ReadAllText(Path.Combine(dir, "bosses/scurceol-hyrde.json")), Order)),
            });
            LoreBible.Boss order = bible.BossById("scurceol-hyrde");
            Assert.That(order.IsOrder, Is.True);
            Assert.That(order.Members.Select(m => (m.Name, m.Level, m.Post, m.LairEntry.Length, m.LoremasterKeyword)),
                Is.EqualTo(new[] { ("Knight Ain", 70, "the east ward", 1, "knight ain") }));
            Assert.That(bible.BossForName("Knight Ain")?.Id, Is.EqualTo("scurceol-hyrde"), "the order is found by a member's spawn name");
        }

        /// <summary>Camelot Hills is zone 0 of region 1: "zones" may hold 0, "regions" may not.</summary>
        [Test]
        public void ZoneZeroIsAZoneButRegionZeroIsNot()
        {
            Assert.That(ProblemsWith(l => l.Replace("\"regions\": [61]", "\"regions\": [61, 1], \"zones\": [0]")), Is.Empty);
            Assert.That(ProblemsWith(l => l.Replace("\"regions\": [61]", "\"regions\": [0, 61]")), Has.Some.Contains("holds 0, not a region or zone number"));
        }

        #endregion

        #region Depth: lengths, books, history, links (owner 2026-11-06, LORE-1.2-DEPTH.md)

        [Test]
        public void LengthsAreAdvisory()
        {
            string longStory = string.Join(" ", Enumerable.Repeat("word", 2_000));
            var warnings = new List<string>();
            Assert.That(ProblemsWith(l => System.Text.RegularExpressions.Regex.Replace(l, "\"story\": \"[^\"]*\"", $"\"story\": \"{longStory}\""), null, null, warnings), Is.Empty,
                "no upper limit");
            Assert.That(ProblemsWith(l => System.Text.RegularExpressions.Regex.Replace(l, "\"story\": \"[^\"]*\"", "\"story\": \"Too short.\""), null, null, warnings), Is.Empty,
                "a short story is a warning");
            Assert.That(warnings, Has.Some.Contains("story is 2 words"));
        }

        [Test]
        public void ABookOverSixteenPagesIsBoundInVolumesNotRefused()
        {
            string page = "\"" + string.Join(" ", Enumerable.Repeat("page", 110)) + "\"";
            string forty = "\"pages\": [" + string.Join(", ", Enumerable.Repeat(page, 40)) + "]";
            string sixteen = "\"pages\": [" + string.Join(", ", Enumerable.Repeat(page, 16)) + "]";
            var warnings = new List<string>();
            Assert.That(ProblemsWith(l => System.Text.RegularExpressions.Regex.Replace(l, "\"pages\": \\[[^\\]]*\\]", forty), null, null, warnings), Is.Empty);
            Assert.That(warnings, Has.Some.Contains("40 book pages: bound in 3 volumes of 16"));
            warnings.Clear();
            Assert.That(ProblemsWith(l => System.Text.RegularExpressions.Regex.Replace(l, "\"pages\": \\[[^\\]]*\\]", sixteen), null, null, warnings), Is.Empty);
            Assert.That(warnings, Has.None.Contains("volumes"));
            Assert.That(LoreBible.MaxBookPages, Is.EqualTo(LoreChannels.LoreBooks.VolumePages), "the schema and the binder agree");
        }

        #endregion

        #region Coverage: npcNames, tiers, zone files (coverage audit)

        [Test]
        public void ABossMatchesByCatalogKeyOrExactSpawnName()
        {
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"catalogKey\": \"krondon/scurceolhyrde\",", "\"npcNames\": [\"Scurceol Hyrde\"],")
                .Replace("\"trigger\": \"orbDeath\"", "\"trigger\": \"health<=50\"").Replace("\"trigger\": \"addsDead\"", "\"trigger\": \"health<=25\"")), Is.Empty,
                "npcNames instead of a catalogKey");
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"catalogKey\": \"krondon/scurceolhyrde\",", "")), Has.Some.Contains("or npcNames"));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"catalogKey\"", "\"tier\": \"legendary\", \"catalogKey\"")), Has.Some.Contains("tier \"legendary\""));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"catalogKey\"", "\"tier\": \"minor\", \"catalogKey\"")), Is.Empty);
            string dir = FixtureDir(RepoRoot());
            string boss = File.ReadAllText(Path.Combine(dir, "bosses/scurceol-hyrde.json"));
            var named = boss.Replace("\"catalogKey\": \"krondon/scurceolhyrde\",", "\"npcNames\": [\"Thrawn Ogre Chief\", \"Thrawn Ogre Warlord\"],");
            var twice = ProblemsWith(l => l.Replace("\"bosses\": [\"scurceol-hyrde\"]", "\"bosses\": [\"scurceol-hyrde\", \"ogre-a\", \"ogre-b\"]"), null,
                ("bosses/ogre-a.json", named.Replace("\"id\": \"scurceol-hyrde\"", "\"id\": \"ogre-a\"")),
                ("bosses/ogre-b.json", named.Replace("\"id\": \"scurceol-hyrde\"", "\"id\": \"ogre-b\"")));
            Assert.That(twice, Has.Some.Contains("npcNames \"Thrawn Ogre Chief\" is claimed by"));
            LoreBible bible = LoreBible.Parse(new[] { ("bosses/ogre-a.json", named.Replace("\"id\": \"scurceol-hyrde\"", "\"id\": \"ogre-a\"")) });
            Assert.That(bible.BossFor("GameNPC", "Thrawn Ogre Warlord")?.Id, Is.EqualTo("ogre-a"), "a plain spawn by its exact name");
            Assert.That(bible.BossFor("GameNPC", "thrawn ogre warlord"), Is.Null, "exact names only");
            Assert.That(Fixture().BossFor("ScurceolHyrde", "anything")?.Id, Is.EqualTo("scurceol-hyrde"), "the class first");
            Assert.That(LoreChannels.LoreSourceExtensions.FindBossFor(new LoreBibleSource(bible), "GameNPC", "Thrawn Ogre Chief")?.Id, Is.EqualTo("ogre-a"), "the channels match the name too");
        }

        [Test]
        public void TheNarrowerFileWinsARegion()
        {
            const string Umbrella = "{\"id\":\"hibernia-wilds\",\"name\":\"Wilds\",\"realm\":\"Hibernia\",\"regions\":[200, 181]}";
            const string Zone = "{\"id\":\"connacht\",\"name\":\"Connacht\",\"realm\":\"Hibernia\",\"regions\":[200]}";
            const string Lair = "{\"id\":\"sheeroe\",\"name\":\"Sheeroe\",\"realm\":\"Hibernia\",\"regions\":[200],\"zones\":[204]}";
            LoreBible bible = LoreBible.Parse(new[] { ("locations/hibernia-wilds.json", Umbrella), ("locations/connacht.json", Zone), ("locations/sheeroe.json", Lair) });
            Assert.That(LoreEntryRules.LocationAt(bible, 200, 201)?.Id, Is.EqualTo("connacht"), "fewer regions than the umbrella");
            Assert.That(LoreEntryRules.LocationAt(bible, 200, 204)?.Id, Is.EqualTo("sheeroe"), "the zone file first");
            Assert.That(LoreEntryRules.LocationAt(bible, 181, 1)?.Id, Is.EqualTo("hibernia-wilds"), "the umbrella where nothing narrower is");
            const string Same = "{\"id\":\"a-zone\",\"name\":\"A\",\"realm\":\"Hibernia\",\"regions\":[200],\"atmosphere\":{\"areas\":[{\"name\":\"x\",\"x\":5,\"y\":5,\"radius\":500,\"lines\":[\"x\"]}]}}";
            LoreBible tie = LoreBible.Parse(new[] { ("locations/connacht.json", Zone), ("locations/a-zone.json", Same.Replace("a-zone", "z-zone")) });
            Assert.That(LoreEntryRules.LocationAt(tie, 200, 201)?.Id, Is.EqualTo("z-zone"), "same width: the one with placed areas");
        }

        #endregion

        #region Epic tier: chapters, long books, pieces

        private static string Paragraphs(int count, int words, string word = "word") =>
            string.Join("\\n", Enumerable.Range(0, count).Select(p => string.Join(" ", Enumerable.Range(0, words).Select(i => $"{word}{p}x{i}")) + "."));

        private static string WithChapters(string json, int chapters = 5) =>
            json.TrimEnd().TrimEnd('}') + ", \"tier\": \"epic\", \"chapters\": [" +
            string.Join(", ", Enumerable.Range(1, chapters).Select(i => $"{{\"title\": \"Part {i}\", \"text\": \"{Paragraphs(4, 90)}\"}}")) + "]}";

        [Test]
        public void ChaptersAndTheEpicTierParseAndAreChecked()
        {
            Assert.That(ProblemsWith(l => WithChapters(l)), Is.Empty);
            Assert.That(ProblemsWith(editBoss: b => WithChapters(b)), Is.Empty);
            Assert.That(ProblemsWith(l => WithChapters(l).Replace("\"title\": \"Part 2\"", "\"title\": \"Part 1\"")), Has.Some.Contains("\"Part 1\" is there twice"));
            Assert.That(ProblemsWith(l => WithChapters(l).Replace("\"tier\": \"epic\"", "\"tier\": \"minor\"")), Is.Empty, "a minor place (Tier B zone)");
            Assert.That(ProblemsWith(l => WithChapters(l).Replace("\"tier\": \"epic\"", "\"tier\": \"legendary\"")), Has.Some.Contains("location's tier"));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"bookPage\": \"\"", "\"bookPage\": [\"" + Paragraphs(1, 110) + "\", \"" + Paragraphs(1, 110) + "\"]")), Is.Empty,
                "bookPage as a list");
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"bookPage\": \"\"", "\"bookPages\": [\"" + Paragraphs(1, 110) + "\", \"" + Paragraphs(1, 110) + "\"]")), Is.Empty,
                "bookPages, the Neutral writer's spelling");
            Assert.That(LoreBible.Parse(new[] { ("bosses/x.json", "{\"id\": \"x\", \"bookPages\": [\"one.\", \"two.\"]}") }).Bosses[0].BookPages, Is.EqualTo(new[] { "one.", "two." }));
            Assert.That(LoreChannels.LoreJson.ParseBoss("{\"id\": \"x\", \"bookPages\": [\"one.\", \"two.\"]}").BookPages, Is.EqualTo(new[] { "one.", "two." }),
                "the channels read it too");
            string dir = FixtureDir(RepoRoot());
            LoreBible bible = LoreBible.Parse(new[]
            {
                ("locations/krondon.json", WithChapters(File.ReadAllText(Path.Combine(dir, "locations/krondon.json")))),
                ("bosses/scurceol-hyrde.json", File.ReadAllText(Path.Combine(dir, "bosses/scurceol-hyrde.json"))
                    .Replace("\"bookPage\": \"\"", "\"bookPage\": [\"first page.\", \"second page.\"]")),
                ("history/albion.json", File.ReadAllText(Path.Combine(dir, "history/albion.json"))),
            });
            Assert.That(bible.LocationById("krondon").IsEpic && bible.LocationById("krondon").Chapters.Length == 5, Is.True);
            Assert.That(bible.BossById("scurceol-hyrde").BookPages, Is.EqualTo(new[] { "first page.", "second page." }));
            Assert.That(bible.DepthReport(), Has.Some.EqualTo("epic locations: 4-10 chapters: 0 of 1 below"));
            // Bots stay on their short lines: no chapter text reaches the corpus or an answer.
            Assert.That(LoreChat.CorpusLines(bible).Any(l => l.Text.Contains("word0x")), Is.False);
            Assert.That(bible.TryAnswer("whats in krondon?", new Random(1), out string answer) && !answer.Contains("word0x"), Is.True);
        }

        [Test]
        public void PiecesKeepEveryWordInOrderAndStayUnderTheLimit()
        {
            var random = new Random(7);
            for (int round = 0; round < 50; round++)
            {
                int max = random.Next(60, 600);
                var paragraphs = Enumerable.Range(0, random.Next(1, 8)).Select(_ =>
                    string.Join(" ", Enumerable.Range(0, random.Next(1, 160)).Select(i => new string('a', random.Next(1, 14)) + (random.Next(9) == 0 ? "." : ""))));
                string text = string.Join("\n", paragraphs);
                List<string> pieces = LoreChannels.LorePieces.Split(text, max);
                Assert.That(pieces.All(p => p.Length <= max), Is.True, $"a piece over {max}");
                Assert.That(pieces.All(p => p.Length > 0 && p == p.Trim()), Is.True);
                string[] words = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                string[] said = pieces.SelectMany(p => p.Split(' ', StringSplitOptions.RemoveEmptyEntries)).ToArray();
                Assert.That(said, Is.EqualTo(words), "every word, whole and in order");
            }
            Assert.That(LoreChannels.LorePieces.Split("One. Two.\nThree.", 100), Is.EqualTo(new[] { "One. Two. Three." }), "short paragraphs share a piece");
            string longParagraph = string.Join(" ", Enumerable.Repeat("Sentence of words here.", 10));
            Assert.That(LoreChannels.LorePieces.Split("Short one.\n" + longParagraph + "\nAfter.", 100).First(), Is.EqualTo("Short one."),
                "a paragraph that needs pieces of its own starts a new one");
            Assert.That(LoreChannels.LorePieces.Split(longParagraph, 100).All(p => p.EndsWith(".")), Is.True, "cut between sentences");
            Assert.That(LoreChannels.LorePieces.Split(new string('x', 250), 100).Select(p => p.Length), Is.EqualTo(new[] { 100, 100, 50 }), "only a word longer than a piece is cut");
            Assert.That(LoreChannels.LorePieces.Split("  ", 100), Is.Empty);
        }

        [Test]
        public void AnEpicChapterReachesThePlayerInReadablePieces()
        {
            string dir = FixtureDir(RepoRoot());
            string chapterText = string.Join("\n", Enumerable.Range(0, 6).Select(p => string.Join(" ", Enumerable.Range(0, 110).Select(i => $"w{p}-{i}")) + "."));
            var location = LoreChannels.LoreJson.ParseLocation(File.ReadAllText(Path.Combine(dir, "locations/krondon.json"))) with { };
            location = location with { Chapters = [new("The Making", chapterText), new("The First Raid", "Short. Then the raid came.")] };
            var source = new LoreChannels.LoreLibrary([location], []);
            var dialogue = new LoreChannels.LoremasterDialogue(source, LoreChannels.LoreRealm.Albion, "Loremaster Aldwin");
            string place = dialogue.Resolve("krondon").Text;
            Assert.That(place, Does.Contain("[the tale of Krondon]").And.Contain("[The Making]").And.Contain("[The First Raid]"));
            Assert.That(dialogue.Resolve("the tale of Krondon").Text, Does.StartWith(location.Story.Trim()[..40]), "the summary first");
            string making = dialogue.Resolve("the making").Text;
            Assert.That(making, Does.StartWith("w0-0").And.EndWith("Then ask of [The First Raid]."));
            var pieces = LoreChannels.LoremasterNPC.Pieces(making).ToList();
            Assert.That(pieces.Count, Is.GreaterThan(5));
            Assert.That(pieces.All(p => p.Length <= LoreChannels.LorePieces.SayChars), Is.True);
            Assert.That(string.Join(" ", pieces).Split(' '), Is.EqualTo(making.Split((char[])null, StringSplitOptions.RemoveEmptyEntries)));
            // A book page longer than one text window turns into more pages, each window inside the 2,048-byte packet.
            var book = location with { Book = new LoreChannels.LoreBookText("Of the Hall", [chapterText, "A short page."]) };
            List<string> pages = LoreChannels.LoreBooks.Pages(new LoreChannels.LoreLibrary([book], []), book);
            Assert.That(pages.Count, Is.GreaterThan(2));
            Assert.That(string.Join(" ", pages).Split(' '), Is.EqualTo((chapterText.Replace("\n", " ") + " A short page.").Split(' ')));
            for (int i = 0; i < pages.Count; i++)
            {
                var (caption, lines) = LoreChannels.LoreBooks.PageWindow("Of the Hall under the Hill, a book of many pages", pages, i);
                Assert.That(LoreChannels.LorePieces.WindowBytes(caption, lines), Is.LessThan(LoreChannels.LorePieces.PacketBytes), $"page {i + 1}");
                Assert.That(lines.All(l => l.Length <= 255), Is.True);
            }
        }

        [Test]
        public void EveryLoremasterPieceFitsThePopupPacket()
        {
            // PacketProcessor.ValidatePacketSize discards any packet over 2,048 bytes whole; SayTo is one Message packet a piece.
            string chapter = string.Join("\n", Enumerable.Range(0, 8).Select(p => string.Join(" ", Enumerable.Range(0, 140).Select(i => $"word{p}{i}")) + "."));
            string longWord = new string('x', 2_000);
            foreach (string speaker in LoreChannels.Loremasters.Definitions.Select(d => d.Name).Append(new string('N', 60)))
                foreach (string piece in LoreChannels.LoremasterNPC.Pieces(chapter + "\n" + longWord))
                {
                    Assert.That(piece.Length, Is.LessThanOrEqualTo(LoreChannels.LorePieces.SayChars));
                    Assert.That(LoreChannels.LorePieces.SayBytes(speaker, piece), Is.LessThan(LoreChannels.LorePieces.PacketBytes), speaker);
                }
            Assert.That(LoreChannels.LorePieces.SayBytes("Loremaster Aldwin", new string('a', LoreChannels.LorePieces.SayChars)), Is.LessThan(600),
                "a full piece is a quarter of the packet");
            // A full page window: every line at the wrap width, the longest caption.
            var (caption, lines) = LoreChannels.LoreBooks.PageWindow(new string('T', 60) + ", Volume XII", [new string('a', 3) + string.Concat(Enumerable.Repeat(" word", LoreChannels.LorePieces.PageChars / 5 - 1))], 0, "Next");
            Assert.That(LoreChannels.LorePieces.WindowBytes(caption, lines), Is.LessThan(LoreChannels.LorePieces.PacketBytes), "a page at its longest fits one window");
        }

        [Test]
        public void GameTalkInChaptersStoryAndBooksIsAWarningNotAFailure()
        {
            Assert.That(LoreBible.GameTalkPatterns, Has.Length.EqualTo(38), "the same patterns as the writers' chapter_lint.py");
            Assert.That(LoreBible.GameTalk("No blade has bitten her away from the Bed; the drovers say the moor takes every blow for her."), Is.Empty,
                "the world truth behind a mechanic, told as history, passes");
            Assert.That(LoreBible.GameTalk("The hall stood. That is why raids fight her in the lair. Then night fell.").Single(),
                Is.EqualTo(("raids", "That is why raids fight her in the lair.")), "the sentence that flags, and the word");
            var warnings = new List<string>();
            Assert.That(ProblemsWith(l => WithChapters(l).Replace("{\"title\": \"Part 2\", \"text\": \"", "{\"title\": \"Part 2\", \"text\": \"The healers must stand back. "),
                b => b.Replace("\"bookPage\": \"\"", "\"bookPage\": [\"" + Paragraphs(1, 110) + " Every fight ends at the lights.\"]"), null, warnings), Is.Empty,
                "a warning for the writers' review, never a failure");
            Assert.That(warnings, Has.Some.Contains("game talk in chapter \"Part 2\" ('healers'"));
            Assert.That(warnings, Has.Some.Contains("game talk in bookPage 1 ('Every fight'"));
            Assert.That(warnings, Has.None.Contains("game talk in legend"), "fight advice belongs in the legend, unchecked");
            warnings.Clear();
            ProblemsWith(l => System.Text.RegularExpressions.Regex.Replace(l, "\"pages\": \\[\\s*\"", "\"pages\": [\"If you go, you must bring a tank. "), null, null, warnings);
            Assert.That(warnings, Has.Some.Contains("game talk in book page 1 ('If you'"));
        }

        #endregion

        #region Depth: history and links

        [Test]
        public void HistoryFilesAndLinksResolve()
        {
            LoreBible bible = Fixture();
            Assert.That(bible.Histories.Single().Eras.Select(e => e.Id), Is.EqualTo(new[] { "albion-old-halls", "albion-war-of-the-isles" }), "in order");
            Assert.That(bible.EraById("albion-old-halls")?.Events.Single().Places, Is.EqualTo(new[] { "krondon" }));
            Assert.That(bible.FactionById("krondon-keepers")?.Bosses, Is.EqualTo(new[] { "scurceol-hyrde" }));
            Assert.That(bible.LocationById("krondon").Era, Is.EqualTo("albion-old-halls"));
            Assert.That(bible.BossById("scurceol-hyrde").RelatedTo.Single().Relation, Is.EqualTo("keeps its weather"));
            Assert.That(ProblemsWith(l => l.Replace("\"era\": \"albion-old-halls\"", "\"era\": \"albion-old-hall\"")), Has.Some.Contains("era \"albion-old-hall\" is in no history file"));
            Assert.That(ProblemsWith(editBoss: b => b.Replace("\"factions\": [\"krondon-keepers\"]", "\"factions\": [\"keepers\"]")), Has.Some.Contains("faction \"keepers\""));
            // Links to places and bosses may lead to another writer's branch: warnings until the files meet.
            var links = new List<string>();
            Assert.That(ProblemsWith(null, b => b.Replace("{\"id\": \"krondon\", \"relation\"", "{\"id\": \"krondun\", \"relation\""), null, links), Is.Empty);
            Assert.That(links, Has.Some.Contains("related \"krondun\" is no location or boss"));
            Assert.That(ProblemsWith(null, null, h => h.Replace("\"places\": [\"krondon\"], \"bosses\": [\"scurceol-hyrde\"]}]}", "\"places\": [\"caer-sidi\"], \"bosses\": []}]}"), links),
                Is.Empty);
            Assert.That(links, Has.Some.Contains("place \"caer-sidi\" has no location file"));
            Assert.That(ProblemsWith(null, null, h => h.Replace("\"id\": \"krondon-keepers\"", "\"id\": \"albion-old-halls\""), null), Has.Some.Contains("both an era and a faction"));
        }

        /// <summary>The Midgard writer's shape (pass 2): no id (the realm's), "summary", eras with "order", facts as events, places.</summary>
        private const string MidgardHistory = "{\"realm\": \"Midgard\", \"summary\": \"The north remembers the giants.\", \"eras\": [" +
            "{\"id\": \"midgard-now\", \"order\": 2, \"name\": \"the Present\", \"summary\": \"The war of the realms.\", \"events\": [], \"places\": [], \"bosses\": []}, " +
            "{\"id\": \"midgard-ymir\", \"order\": 1, \"name\": \"Ymir's Age\", \"summary\": \"Before the gods.\", " +
            "\"events\": [{\"text\": \"The rime-giants descend from Bergelmir.\", \"provenance\": \"myth\", \"source\": \"Snorri, Gylfaginning\"}], \"places\": [\"krondon\"], \"bosses\": []}], " +
            "\"factions\": [{\"id\": \"krondon-keepers\", \"name\": \"the Keepers, as the skalds tell it\", \"summary\": \"x\", \"places\": [\"krondon\"], \"bosses\": []}]}";

        [Test]
        public void TheWritersHistoryShapeParsesAndFactionsMayBeShared()
        {
            var warnings = new List<string>();
            Assert.That(ProblemsWith(null, null, null, warnings, ("history/midgard.json", MidgardHistory)), Is.Empty, "a faction id shared by two realms");
            Assert.That(warnings, Has.Some.Contains("faction id \"krondon-keepers\" is in history/albion.json, history/midgard.json"),
                "outside the agreed shared ids it is a warning: one power or two?");
            // The agreed shared ids (summoners, lords-of-the-undead, sidhe-of-the-hall, dark-ones) pass quietly, and each
            // realm's readers meet its own telling.
            warnings.Clear();
            Func<string, string> summoners = t => t.Replace("krondon-keepers", "summoners");
            Assert.That(ProblemsWith(summoners, summoners, summoners, warnings, ("history/midgard.json", summoners(MidgardHistory))), Is.Empty);
            Assert.That(warnings, Has.None.Contains("faction id \"summoners\""));
            string fixtures = FixtureDir(RepoRoot());
            var shared = new LoreBibleSource(LoreBible.Parse(new[] { "locations/krondon.json", "bosses/scurceol-hyrde.json", "history/albion.json" }
                .Select(p => (p, summoners(File.ReadAllText(Path.Combine(fixtures, p))))).Append(("history/midgard.json", summoners(MidgardHistory)))));
            Assert.That(LoreChannels.LoreSourceExtensions.FindFaction(LoreChannels.LoreRealmView.For(shared, LoreChannels.LoreRealm.Midgard), "summoners")?.Name,
                Is.EqualTo("the Keepers, as the skalds tell it"));
            Assert.That(LoreChannels.LoreSourceExtensions.FindFaction(LoreChannels.LoreRealmView.For(shared, LoreChannels.LoreRealm.Albion), "summoners")?.Name,
                Is.EqualTo("the Keepers of Krondon"));
            warnings.Clear();
            string dir = FixtureDir(RepoRoot());
            LoreBible bible = LoreBible.Parse(new[] { "locations/krondon.json", "bosses/scurceol-hyrde.json", "history/albion.json" }
                .Select(p => (p, File.ReadAllText(Path.Combine(dir, p)))).Append(("history/midgard.json", MidgardHistory)));
            LoreBible.History midgard = bible.HistoryFor("Midgard");
            Assert.That((midgard.Id, midgard.Intro), Is.EqualTo(("midgard", "The north remembers the giants.")));
            Assert.That(midgard.Eras.Select(e => e.Id), Is.EqualTo(new[] { "midgard-ymir", "midgard-now" }), "by order");
            Assert.That(midgard.Eras[0].Facts.Single().Provenance, Is.EqualTo("myth"));
            Assert.That(midgard.Eras[0].Places, Is.EqualTo(new[] { "krondon" }));
            Assert.That(ProblemsWith(null, null, null, null, ("history/midgard.json", MidgardHistory.Replace("midgard-now", "albion-old-halls"))),
                Has.Some.Contains("era id \"albion-old-halls\" is also used in"));
            var unwritten = new List<string>();
            Assert.That(ProblemsWith(null, null, null, unwritten, ("history/midgard.json", MidgardHistory.Replace("\"places\": [\"krondon\"], \"bosses\": []}],", "\"places\": [\"jordheim\"], \"bosses\": []}],"))),
                Is.Empty);
            Assert.That(unwritten, Has.Some.Contains("era \"midgard-ymir\" place \"jordheim\""));
            var lenient = LoreChannels.LoreJson.ParseHistory(MidgardHistory);
            Assert.That(lenient.Eras.Select(e => e.Id), Is.EqualTo(new[] { "midgard-ymir", "midgard-now" }));
            Assert.That(lenient.Eras[0].Places, Is.EqualTo(new[] { "krondon" }));
        }

        [Test]
        public void TheDepthReportCountsEntriesBelowTheFloor()
        {
            List<string> report = Fixture().DepthReport();
            Assert.That(report, Has.Some.EqualTo("locations: story 500+ words: 1 of 1 below"));
            Assert.That(report, Has.Some.EqualTo("locations: an era and a faction: 0 of 1 below"));
            Assert.That(report, Has.Some.EqualTo("history: Albion: 2 eras, 1 factions"));
            Assert.That(report, Has.Some.EqualTo("history: Midgard: no file yet"));
        }

        [Test]
        public void TheLoremasterAndCodexTellHistoryAndFollowLinks()
        {
            var source = new LoreBibleSource(Fixture());
            var dialogue = new LoreChannels.LoremasterDialogue(source, LoreChannels.LoreRealm.Albion, "Loremaster Aldwin");
            string history = dialogue.Resolve("history").Text;
            Assert.That(history, Does.Contain("[the Old Halls]").And.Contain("[the War of the Isles]").And.Contain("[the Keepers of Krondon]"));
            Assert.That(history.IndexOf("Old Halls", StringComparison.Ordinal), Is.LessThan(history.IndexOf("War of the Isles", StringComparison.Ordinal)), "oldest first");
            Assert.That(dialogue.Resolve("the Old Halls").Text, Does.Contain("[Krondon]").And.Contain("[Scurceol Hyrde]"));
            Assert.That(dialogue.Resolve("the Keepers of Krondon").Text, Does.Contain("Its places: [Krondon]"));
            Assert.That(dialogue.Resolve("krondon").Text, Does.Contain("Its tale belongs to [the Old Halls]").And.Contain("[the Keepers of Krondon]"));
            Assert.That(dialogue.Resolve("storm-keeper").Text, Does.Contain("[Krondon] (keeps its weather)"));
            var midgard = new LoreChannels.LoremasterDialogue(source, LoreChannels.LoreRealm.Midgard, "Loremaster Sigrun");
            Assert.That(midgard.Resolve("the Old Halls"), Is.Null, "a realm's history is its own loremaster's");
            var page = LoreChannels.LoreCodex.Find(source, LoreChannels.LoreRealm.Albion, [], "history");
            Assert.That(page.Lines, Has.Some.Contains("[the Old Halls]"));
            Assert.That(LoreChannels.LoreCodex.Find(source, LoreChannels.LoreRealm.Albion, [], "the old halls").Lines, Has.Some.Contains("[Krondon]"));
            Assert.That(LoreChannels.LoreCodex.Pages(source, LoreChannels.LoreRealm.Albion, [], "krondon").SelectMany(p => p.Lines), Has.Some.Contains("Its age: [the Old Halls]"));
            var lenient = LoreChannels.LoreLibrary.Parse(new[] { "locations/krondon.json", "bosses/scurceol-hyrde.json", "history/albion.json" }
                .Select(p => (p, File.ReadAllText(Path.Combine(FixtureDir(RepoRoot()), p)))));
            Assert.That(lenient.Histories.Single().Factions.Single().Name, Is.EqualTo("the Keepers of Krondon"), "the launcher's reader skips history files as places");
            Assert.That(lenient.AllLocations.Select(l => l.Id), Is.EqualTo(new[] { "krondon" }));
        }

        #endregion

        #region Realm takes (Midgard writer, PR #93)

        private const string Takes = ", \"realmTakes\": {" +
            "\"midgard\": {\"legend\": \"Jordheim's raid leaders tell it their own way: the ogres are only the door, the keepers are the house, and the deepest keeper is the storm itself. Count the lights before you count the ogres, they say, and you will come home.\", " +
            "\"botLines\": {\"tell\": [\"krondon is an alb hole. ogres and old keepers, [heard|so they say]\"], \"lore\": [\"the albs lost a hall to ogres and still brag about it\"]}, " +
            "\"loremaster\": {\"intro\": \"Krondon, the Albions' old hall. Ask of its [keepers].\", \"topics\": {\"keepers\": \"Old masters of weather and law.\"}}}}";

        private static string WithTakes(string json) => json.TrimEnd().TrimEnd('}') + Takes + "}";

        [Test]
        public void RealmTakesParseAndAreChecked()
        {
            Assert.That(ProblemsWith(WithTakes), Is.Empty);
            Assert.That(ProblemsWith(l => WithTakes(l).Replace("\"midgard\": {", "\"Midgard\": {")), Is.Empty, "a realm key in any case");
            Assert.That(ProblemsWith(l => WithTakes(l).Replace("\"midgard\": {", "\"norse\": {")), Has.Some.Contains("realmTakes \"norse\""));
            Assert.That(ProblemsWith(l => WithTakes(l).Replace("\"krondon is an alb hole", "\"Krondon is an alb hole")), Has.Some.Contains("realmTakes.midgard.tell"));
            Assert.That(ProblemsWith(l => WithTakes(l).Replace("Ask of its [keepers]", "Ask of its [wardens]")), Has.Some.Contains("[wardens]"));
            Assert.That(ProblemsWith(l => WithTakes(l).Replace("\"legend\": \"Jordheim's", "\"lgend\": \"Jordheim's")), Has.Some.Contains("unknown field \"lgend\""));
        }

        private static LoreBible TakesBible()
        {
            string dir = FixtureDir(RepoRoot());
            return LoreBible.Parse(new[]
            {
                // As a neutral place, so every realm's loremaster tells it.
                ("locations/krondon.json", WithTakes(File.ReadAllText(Path.Combine(dir, "locations/krondon.json"))).Replace("\"realm\": \"Albion\"", "\"realm\": \"Neutral\"")),
                ("bosses/scurceol-hyrde.json", File.ReadAllText(Path.Combine(dir, "bosses/scurceol-hyrde.json"))),
            });
        }

        [Test]
        public void EachRealmHearsItsOwnTake()
        {
            LoreBible bible = TakesBible();
            var random = new Random(2);
            Assert.That(bible.TryAnswer("whats in krondon?", random, out string answer, eRealm.Midgard) && answer.StartsWith("krondon is an alb hole"), Is.True);
            Assert.That(bible.TryAnswer("whats in krondon?", random, out answer, eRealm.Albion) && answer.StartsWith("krondon? ogres"), Is.True);
            var lines = LoreChat.CorpusLines(bible);
            Assert.That(lines.Single(l => l.Text.StartsWith("the albs lost a hall")).Realm, Is.EqualTo("mid"));
        }

        [Test]
        public void TheChannelsReadTheEngineBibleWithRealmTakes()
        {
            var source = new LoreBibleSource(TakesBible());
            LoreChannels.LoreLocation krondon = source.GetLocation("krondon");
            Assert.That(krondon.Loremaster.Topics.Select(t => t.Key), Is.EquivalentTo(new[] { "names", "warrior queen" }));
            Assert.That(krondon.Book.Pages.Length, Is.EqualTo(2));
            Assert.That(krondon.Areas.Single().Radius, Is.EqualTo(900));
            Assert.That(source.GetBoss("scurceol-hyrde").LoremasterTopic.Keyword, Is.EqualTo("storm-keeper"));
            Assert.That(source.GetBoss("scurceol-hyrde").Chronicle.Win, Does.Contain("{raidLeader}"));
            Assert.That(source.GetBoss("scurceol-hyrde").IsClass("ScurceolHyrde"), Is.True);
            var midgard = LoreChannels.LoreRealmView.For(source, LoreChannels.LoreRealm.Midgard);
            Assert.That(midgard.GetLocation("krondon").Legend, Does.StartWith("Jordheim's raid leaders"));
            Assert.That(midgard.GetLocation("krondon").Story, Is.EqualTo(krondon.Story), "what a take leaves out stays shared");
            Assert.That(LoreChannels.LoreRealmView.For(source, LoreChannels.LoreRealm.Albion).GetLocation("krondon").Legend, Is.EqualTo(krondon.Legend));
            var dialogue = new LoreChannels.LoremasterDialogue(source, LoreChannels.LoreRealm.Midgard, "Loremaster Sigrun");
            Assert.That(dialogue.Resolve("keepers")?.Text, Is.EqualTo("Old masters of weather and law."), "the realm's own topics");
            Assert.That(dialogue.Resolve("warrior queen")?.Text, Does.Contain("ogres"), "and the shared ones");
            var lenient = LoreChannels.LoreJson.ParseLocation(WithTakes(File.ReadAllText(Path.Combine(FixtureDir(RepoRoot()), "locations/krondon.json"))));
            Assert.That(lenient.ForRealm(LoreChannels.LoreRealm.Midgard).Loremaster.Intro, Does.StartWith("Krondon, the Albions'"), "the launcher's reader too");
        }

        #endregion

        #region Lines

        [Test]
        public void ChoicesExpandParseAndPick()
        {
            Assert.That(LoreText.Expand("[kill|put out] the lights [first|]"),
                Is.EquivalentTo(new[] { "kill the lights first", "kill the lights", "put out the lights first", "put out the lights" }));
            Assert.That(LoreText.Expand("no choices here"), Is.EqualTo(new[] { "no choices here" }));
            Assert.That(LoreText.Expand("[a|b|c] [d|e|f] [g|h|i] [j|k|l]").Length, Is.EqualTo(LoreText.FamilyCap));
            Assert.That(LoreText.TryParse("[a|b] c", out _), Is.True);
            Assert.That(LoreText.TryParse("[a b] c", out _), Is.False);
            Assert.That(LoreText.TryParse("a] c", out _), Is.False);
            var random = new Random(3);
            for (int i = 0; i < 20; i++)
                Assert.That(LoreText.Pick("[x|y], ok", random), Is.AnyOf("x, ok", "y, ok"));
        }

        [Test]
        public void QuestionsAboutAPlaceOrABossGetALoreAnswer()
        {
            LoreBible bible = Fixture();
            var random = new Random(1);
            Assert.That(bible.Subject("whats in krondon?").Location?.Id, Is.EqualTo("krondon"));
            Assert.That(bible.Subject("who is the storm keeper").Boss?.Id, Is.EqualTo("scurceol-hyrde"));
            Assert.That(bible.Subject("anyone know who scurceol hyrde is?").Boss?.Id, Is.EqualTo("scurceol-hyrde"));
            Assert.That(bible.Subject("where is krondon?"), Is.EqualTo(((LoreBible.Location)null, (LoreBible.Boss)null)), "directions are another route");
            Assert.That(bible.Subject("krondon tonight lol").Location, Is.Null, "not a question");
            Assert.That(bible.TryAnswer("what's in krondon?", random, out string answer), Is.True);
            Assert.That(answer, Does.StartWith("krondon? ogres"));
            Assert.That(bible.TryAnswer("who is the storm keeper?", random, out answer), Is.True);
            Assert.That(answer, Does.Contain("four lights"));
            Assert.That(bible.TryAnswer("what is a bard?", random, out _), Is.False);
        }

        [Test]
        public void BotLinesJoinTheLorePoolWithTheirRealm()
        {
            AutonomousBotVoiceCorpus.Line[] lines = LoreChat.CorpusLines(Fixture());
            Assert.That(lines, Is.Not.Empty);
            Assert.That(lines.All(l => l.Kind == "ambient" && l.Topic == "lore" && l.About && l.Realm == "alb"), Is.True);
            Assert.That(lines.Select(l => l.Id).Distinct().Count(), Is.EqualTo(lines.Length));
            Assert.That(lines.All(l => l.Id.Length <= 32 && !l.Text.Contains('[')), Is.True);
            Assert.That(lines.Any(l => l.Text.Contains("warrior queen")), Is.True, "a location rumour");
            Assert.That(lines.Any(l => l.Text.Contains("storm keeper in krondon")), Is.True, "a boss rumour");
            Assert.That(lines.Any(l => l.Text.StartsWith("krondon? ogres")), Is.False, "tell lines are answers, not pool lines");
        }

        [Test]
        public void ABriefingIsOneOrTwoDifferentLines()
        {
            var random = new Random(5);
            string[] pool = ["first line", "second line", "third line"];
            for (int i = 0; i < 50; i++)
            {
                string[] picked = LoreChat.PickBriefing(pool, random);
                Assert.That(picked.Length, Is.InRange(1, 2));
                Assert.That(picked.Distinct().Count(), Is.EqualTo(picked.Length));
            }
            Assert.That(LoreChat.PickBriefing([], random), Is.Empty);
            Assert.That(LoreChat.PickBriefing(null, random), Is.Empty);
        }

        #endregion
    }
}
