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

        private static string LoreDir(string root) => Path.Combine(root, "source", "server", "GameServer", "bots", "autonomous", "data", "lore");

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
            }
            return new LoreBible.Catalog(ids, adds);
        }

        private static LoreBible Fixture()
        {
            string root = RepoRoot();
            string dir = LoreDir(root);
            return LoreBible.Parse(new[] { "locations/krondon.json", "bosses/scurceol-hyrde.json" }
                .Select(p => (p, File.ReadAllText(Path.Combine(dir, p)))));
        }

        /// <summary>Krondon and Scurceol Hyrde as committed, changed by <paramref name="edit"/> (a JSON tweak), for the failure cases.</summary>
        private static List<string> ProblemsWith(Func<string, string> editLocation = null, Func<string, string> editBoss = null,
            params (string Path, string Json)[] extra)
        {
            string root = RepoRoot();
            string dir = LoreDir(root);
            string location = File.ReadAllText(Path.Combine(dir, "locations/krondon.json"));
            string boss = File.ReadAllText(Path.Combine(dir, "bosses/scurceol-hyrde.json"));
            var files = new List<(string, string)>
            {
                ("locations/krondon.json", editLocation?.Invoke(location) ?? location),
                ("bosses/scurceol-hyrde.json", editBoss?.Invoke(boss) ?? boss),
            };
            files.AddRange(extra);
            return LoreBible.Parse(files).Validate(CatalogFrom(root));
        }

        #region The writers' check

        [Test]
        public void EveryLoreFilePassesTheSchema()
        {
            string root = RepoRoot();
            LoreBible bible = LoreBible.Parse(DiskFiles(root));
            Assert.That(bible.Locations.Length, Is.GreaterThan(0), "no lore locations found");
            List<string> problems = bible.Validate(CatalogFrom(root));
            Assert.That(problems, Is.Empty, "docs/LORE.md lists the rules:\n" + string.Join("\n", problems));
        }

        [Test]
        public void TheServerCarriesEveryLoreFile()
        {
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
            Assert.That(ProblemsWith(l => l.Replace("\"bosses\": [\"scurceol-hyrde\"]", "\"bosses\": [\"scurceol-hyrde\", \"orylle\"]")),
                Has.Some.Contains("boss \"orylle\" has no file"));
        }

        [Test]
        public void DuplicateIdsAndCatalogKeysFail()
        {
            string root = RepoRoot();
            string boss = File.ReadAllText(Path.Combine(LoreDir(root), "bosses/scurceol-hyrde.json"));
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
            foreach (string trigger in new[] { "health<=25", "addsSpawn", "addSpawn", "addsDead", "orbDeath", "death", "wake", "formChange" })
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
