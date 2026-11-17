using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using DOL.UnitTests;
using NUnit.Framework;
using OfflineDaoc.Shared;

namespace DOL.GS.Tests
{
    /// <summary>
    /// Era builds 1.2, WP0 (docs/ERA-BUILDS-1.2.md, "Validation"): the shipped data file against the game's own classes,
    /// spec lines, point budgets and build families, and the owner's review of 2026-11-17 (D3 A', D5, D7, D8; bots never
    /// play the custom class).
    /// </summary>
    [TestFixture]
    public class UT_EraBuildData
    {
        private static EraBuildCatalog Catalog => EraBuildCatalog.Default;

        private static JsonElement Raw()
        {
            using Stream stream = typeof(EraBuildCatalog).Assembly.GetManifestResourceStream(EraBuildCatalog.ResourceName);
            Assert.That(stream, Is.Not.Null, "the data file is embedded in GameServer.dll");
            using var reader = new StreamReader(stream);
            return JsonDocument.Parse(reader.ReadToEnd()).RootElement.Clone();
        }

        private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

        [Test]
        public void TheFileLoadsWholeWithNothingDropped()
        {
            Assert.Multiple(() =>
            {
                Assert.That(Catalog.IsLoaded, Is.True);
                Assert.That(Catalog.Problems, Is.Empty);
                Assert.That(Catalog.Classes, Has.Count.EqualTo(39));
                Assert.That(Catalog.BuildCount, Is.EqualTo(106));
                Assert.That(Catalog.DroppedBuilds, Is.Zero);
                Assert.That(Catalog.Status, Does.StartWith("approved 2026-11-17"));
                Assert.That(Catalog.OffMetaPercent, Is.EqualTo(10), "D5");
                Assert.That(Catalog.JitterPoints, Is.EqualTo(2), "D5");
                Assert.That(Catalog.LoadedLine(), Does.Contain("classes=39 builds=106 dropped=0 sharesSourced=0/3 weightsSourced=0/106"));
            });
        }

        [Test]
        public void EveryClassicAndShroudedIslesClassHasBuildsAndTheCustomClassHasNone()
        {
            foreach (eRealm realm in Realms)
            {
                var expected = AutonomousBotIdentityGenerator.GetEraClasses(realm).ToHashSet();
                var shares = Catalog.Shares((int)realm);
                Assert.That(shares.Select(s => (eCharacterClass)s.ClassId), Is.EquivalentTo(expected), realm.ToString());
                foreach (eCharacterClass characterClass in expected)
                {
                    EraClass eraClass = Catalog.GetClass((int)characterClass);
                    Assert.That(eraClass, Is.Not.Null, characterClass.ToString());
                    Assert.That(eraClass.Name, Is.EqualTo(characterClass.ToString()));
                    Assert.That(eraClass.Realm, Is.EqualTo(EraBuildCatalog.RealmName((int)realm)));
                    Assert.That(eraClass.Builds, Is.Not.Empty);
                }
            }
            Assert.That(Catalog.GetClass((int)eCharacterClass.Sluaghbinder), Is.Null);
            Assert.That(Catalog.GetClass("Sluaghbinder"), Is.Null);
            Assert.That(Catalog.Shares("Hibernia").Any(s => s.ClassId == (int)eCharacterClass.Sluaghbinder), Is.False);
            Assert.That(Raw().GetProperty("excludedClasses").TryGetProperty("Sluaghbinder", out _), Is.True);
        }

        [Test]
        public void EveryLineIsARealSpecOfTheClassAndEveryBuildFitsTheLevel50Budget()
        {
            var specNames = typeof(Specs).GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string)).Select(f => (string)f.GetRawConstantValue()).ToHashSet();
            var classTypes = typeof(GameBot).Assembly.GetTypes()
                .Select(t => (Type: t, Attr: t.GetCustomAttributes(typeof(CharacterClassAttribute), false).OfType<CharacterClassAttribute>().FirstOrDefault()))
                .Where(x => x.Attr != null && !x.Type.IsAbstract)
                .GroupBy(x => x.Attr.ID).ToDictionary(g => g.Key, g => g.First().Type);
            foreach (EraClass eraClass in Catalog.Classes)
            {
                Assert.That(eraClass.Lines, Is.SubsetOf(specNames), eraClass.Name);
                var game = (ICharacterClass)Activator.CreateInstance(classTypes[eraClass.ClassId]);
                Assert.That(eraClass.SpecMultiplier, Is.EqualTo(game.SpecPointsMultiplier), eraClass.Name);
                Assert.That(eraClass.PointBudget50, Is.EqualTo(EraBuildCatalog.PointBudget(game.SpecPointsMultiplier)));
                // The budget is what a level 50 player of the class has earned, and a build fits when that player can pay
                // for it: GamePlayer.VerifySpecPoints itself, autotrain lines included (owner 2026-11-17: "companions and
                // bots should be the same as a player").
                Assert.That(eraClass.PointBudget50, Is.EqualTo(PlayerSpecPointOracle.Earned(game, 50)), eraClass.Name);
                Assert.That(eraClass.AutoTrainLines, Is.EqualTo(game.GetAutotrainableSkills().Where(eraClass.Lines.Contains)),
                    $"{eraClass.Name}: the class's autotrain lines, in the game's order");
                foreach (EraBuild build in eraClass.Builds)
                {
                    Assert.That(build.Specs.Select(s => s.Key), Is.SubsetOf(eraClass.Lines), build.Id);
                    Assert.That(build.Specs.Select(s => s.Value), Has.All.InRange(1, 50), build.Id);
                    int left = PlayerSpecPointOracle.PointsLeft(game, 50, false, build.Specs);
                    Assert.That(left, Is.Not.Negative, $"{build.Id}: a level 50 player can train it");
                    Assert.That(build.Points, Is.EqualTo(eraClass.PointBudget50 - left), $"{build.Id}: what it costs a player");
                    Assert.That(build.AutoTrain, Is.EqualTo(eraClass.AutoTrainLines), build.Id);
                }
            }
        }

        [Test]
        public void EveryLineIsOneTheClassTrains()
        {
            // Review of WP0-2 (2026-11-17): a real Specs constant is not enough, the class must train it (the D7 fills,
            // Scout Longbows and Warden Blades, most of all). The data file's "lines" came from make_era_builds.py, so the
            // check is against the server's own class files: every line some build of the class trains today (each family,
            // every random variant and weapon). SkillBase (the class files' weapon lines) needs the test language tables.
            using var language = new DOL.UnitTests.PetTestLanguageScope();
            foreach (EraClass eraClass in Catalog.Classes)
            {
                var cls = (eCharacterClass)eraClass.ClassId;
                var trained = new HashSet<string>(StringComparer.Ordinal);
                foreach (eSpecType family in BotSpec.GetSpecializationChoices(cls))
                    for (int i = 0; i < 400; i++)
                        foreach (BotSpecLine line in BotSpec.GetSpec(cls, family).SpecLines)
                            trained.Add(line.Spec);
                foreach (EraBuild build in eraClass.Builds)
                    foreach (string line in build.Specs.Select(s => s.Key))
                        Assert.That(trained, Does.Contain(line), $"{build.Id}: {line} is not a line any {eraClass.Name} build trains");
            }
        }

        [Test]
        public void EveryFamilyIsOneOfTheClassFilesBuildsAndMatchesItsWeaponLines()
        {
            string[] dualLines = [Specs.Dual_Wield, Specs.Left_Axe, Specs.Celtic_Dual, Specs.HandToHand];
            foreach (EraClass eraClass in Catalog.Classes)
            {
                var choices = BotSpec.GetSpecializationChoices((eCharacterClass)eraClass.ClassId);
                foreach (EraBuild build in eraClass.Builds)
                {
                    Assert.That(Enum.TryParse(build.Family, out eSpecType family), Is.True, build.Id);
                    Assert.That(choices, Does.Contain(family), $"{build.Id} family {build.Family}");
                    if (family is eSpecType.DualWield or eSpecType.LeftAxe)
                        Assert.That(build.Specs.Any(s => dualLines.Contains(s.Key)), Is.True, $"{build.Id}: a dual family needs a dual line");
                    if (family is eSpecType.OneHandAndShield or eSpecType.DualWieldAndShield &&
                        eraClass.Lines.Contains(Specs.Shields))
                        Assert.That(build.Level(Specs.Shields), Is.GreaterThanOrEqualTo(20), $"{build.Id}: a shield family needs Shields 20+");
                }
            }
        }

        [Test]
        public void WeightsAndSharesSumToOneAndEveryNumberSaysItIsAnEstimate()
        {
            JsonElement raw = Raw();
            foreach (JsonProperty realm in raw.GetProperty("realms").EnumerateObject())
            {
                Assert.That(realm.Value.GetProperty("sharesSourced").GetBoolean(), Is.False, realm.Name);
                double total = 0;
                foreach (JsonProperty share in realm.Value.GetProperty("classShare").EnumerateObject())
                {
                    Assert.That(share.Value.GetProperty("sourced").GetBoolean(), Is.False, share.Name);
                    Assert.That(share.Value.GetProperty("basis").GetString(), Does.StartWith("estimate"), share.Name);
                    total += share.Value.GetProperty("share").GetDouble();
                }
                Assert.That(total, Is.EqualTo(1).Within(0.005), realm.Name);
            }
            foreach (JsonProperty cls in raw.GetProperty("classes").EnumerateObject())
            {
                double total = 0;
                foreach (JsonElement build in cls.Value.GetProperty("builds").EnumerateArray())
                {
                    string id = build.GetProperty("id").GetString();
                    Assert.That(build.GetProperty("weightSourced").GetBoolean(), Is.False, id);
                    Assert.That(build.GetProperty("weightBasis").GetString(), Does.StartWith("estimate"), id);
                    Assert.That(build.TryGetProperty("sources", out JsonElement sources) && sources.GetArrayLength() > 0, Is.True, id);
                    total += build.GetProperty("weight").GetDouble();
                }
                Assert.That(total, Is.EqualTo(1).Within(0.005), cls.Name);
            }
        }

        [Test]
        public void EveryTrimOrFillSaysSoAndKeepsThePostedPoints()
        {
            var changed = Catalog.Classes.SelectMany(c => c.Builds).Where(b => b.Change != null).ToArray();
            // D7's sheet trimmed 12 counting every level of every line; with a player's autotrain levels free, 3 of them fit
            // as posted (Shadowblade Axe/Left Axe and Axe/CS, Ranger Archery/Pathfinding) and 9 are still trimmed.
            Assert.That(changed, Has.Length.EqualTo(16), "D7: 9 trimmed, 7 filled");
            Assert.That(changed.Count(b => b.Change.StartsWith("trimmed to budget")), Is.EqualTo(9));
            foreach (string id in new[] { "shadowblade-axe-la", "shadowblade-axe-cs", "ranger-bow-path" })
                Assert.That(Catalog.GetBuild(id)?.Change, Is.Null, $"{id} fits as posted");
            Assert.That(changed.Count(b => b.Change.StartsWith("filled:")), Is.EqualTo(7));
            foreach (EraBuild build in Catalog.Classes.SelectMany(c => c.Builds))
            {
                bool differs = !build.Specs.OrderBy(s => s.Key).SequenceEqual(build.Posted.OrderBy(s => s.Key));
                Assert.That(differs, Is.EqualTo(build.Change != null), build.Id);
                Assert.That(build.SpecsSourced, Is.EqualTo(build.Change == null), build.Id);
                // a trim is honest: a level 50 player of the class could not pay for the posted points
                int postedLeft = PlayerSpecPointOracle.PointsLeft(
                    PlayerSpecPointOracle.ClassOf(Catalog.GetClass(build.ClassName).ClassId), 50, false, build.Posted);
                if (build.Change?.StartsWith("trimmed") == true)
                    Assert.That(postedLeft, Is.Negative, $"{build.Id} was over budget");
                else if (build.Change == null)
                    Assert.That(postedLeft, Is.Not.Negative, $"{build.Id} is as posted and fits");
            }
        }

        [TestCase("Albion", "Cleric:12 Infiltrator:11 Armsman:9 Minstrel:9 Paladin:8 Mercenary:8 Friar:7 Scout:7 Wizard:7 Theurgist:6 Sorcerer:5 Cabalist:4 Necromancer:4 Reaver:3")]
        [TestCase("Midgard", "Healer:15 Skald:12 Shadowblade:10 Warrior:10 Shaman:9 Runemaster:9 Berserker:8 Thane:8 Savage:6 Hunter:5 Spiritmaster:5 Bonedancer:3")]
        [TestCase("Hibernia", "Druid:12 Bard:11 Nightshade:11 Hero:10 Ranger:9 Eldritch:9 Warden:8 Blademaster:8 Enchanter:8 Champion:6 Mentalist:4 Valewalker:2 Animist:2")]
        public void TheSharesAreTheApprovedOnes(string realm, string approved)
        {
            var expected = approved.Split(' ').Select(p => p.Split(':')).ToDictionary(p => p[0], p => double.Parse(p[1]) / 100);
            var actual = Catalog.Shares(realm).ToDictionary(s => s.ClassName, s => s.Share);
            Assert.That(actual.Keys, Is.EquivalentTo(expected.Keys));
            foreach (var (cls, share) in expected)
                Assert.That(actual[cls], Is.EqualTo(share).Within(1e-9), cls);
        }

        [Test]
        public void TheReviewedBuildsAreTheApprovedOnes()
        {
            Assert.Multiple(() =>
            {
                // D8
                Assert.That(Catalog.GetBuild("wizard-fire-earth").Weight, Is.EqualTo(0.60).Within(1e-9));
                Assert.That(Catalog.GetBuild("wizard-earth-cold").Weight, Is.EqualTo(0.25).Within(1e-9));
                Assert.That(Catalog.GetBuild("wizard-cold-earth").Weight, Is.EqualTo(0.15).Within(1e-9));
                Assert.That(EraBuild.Format(Catalog.GetBuild("wizard-cold-earth").Specs), Is.EqualTo("Cold Magic:50,Earth Magic:14,Fire Magic:14"));
                // D7, one trim and one fill per realm
                Assert.That(EraBuild.Format(Catalog.GetBuild("minstrel-inst-stl").Specs), Is.EqualTo("Instruments:50,Stealth:33,Slash:29"));
                Assert.That(EraBuild.Format(Catalog.GetBuild("infiltrator-thrust-env").Specs),
                    Is.EqualTo("Thrust:50,Stealth:46,Envenom:36,Critical Strike:34,Dual Wield:10"));
                // as posted since autotrain is counted (the sheet had Left Axe 43): Stealth's first 12 levels are free
                Assert.That(EraBuild.Format(Catalog.GetBuild("shadowblade-axe-la").Specs),
                    Is.EqualTo("Axe:44,Left Axe:44,Envenom:35,Stealth:35,Critical Strike:10"));
                Assert.That(EraBuild.Format(Catalog.GetBuild("runemaster-supp-dark").Specs), Is.EqualTo("Suppression:50,Darkness:20"));
                Assert.That(EraBuild.Format(Catalog.GetBuild("warrior-axe-shld50").Specs), Is.EqualTo("Axe:50,Shields:50,Parry:28"));
                Assert.That(EraBuild.Format(Catalog.GetBuild("champion-lw-valor-blades").Specs),
                    Is.EqualTo("Large Weapons:50,Blades:36,Valor:35,Parry:28"));
                Assert.That(EraBuild.Format(Catalog.GetBuild("warden-nurt-regrow").Specs),
                    Is.EqualTo("Nurture:49,Regrowth:33,Blades:25,Parry:14"));
                // Roles WP5 reads for the RvR crowd-control slot
                Assert.That(Catalog.GetBuild("healer-pac-mend-aug").Role, Is.EqualTo("healer-cc"));
                Assert.That(Catalog.GetBuild("sorcerer-mind-body").Role, Is.EqualTo("cc-mezzer"));
                Assert.That(Catalog.GetBuild("bard-music-nurt").Role, Is.EqualTo("cc-mezzer"));
                Assert.That(Catalog.GetBuild("healer-mend-aug").Role, Is.EqualTo("healer"));
            });
        }
    }
}
