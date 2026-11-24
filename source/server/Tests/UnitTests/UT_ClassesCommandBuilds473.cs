using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS.Commands;
using DOL.GS.ServerRules;
using DOL.UnitTests;
using NUnit.Framework;

namespace DOL.GS.Tests
{
    /// <summary>
    /// #473: /classes shows the builds /spawn accepts, labels the party role as a role, and says how to pick a build. Every
    /// build name it prints resolves through /spawn's TryResolve.
    /// </summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_ClassesCommandBuilds473
    {
        // Build names come from SkillBase, which needs a server and its language strings (as UT_TemporaryGroupSpecCatalog).
        private sealed class CatalogTestServer : GameServer
        {
            protected override IServerRules ServerRulesImpl => new NormalServerRules();
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private static readonly IObjectDatabase EmptyDatabase =
            DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private GameServer _previousServer;
        private PetTestLanguageScope _language;

        [SetUp]
        public void SetUp()
        {
            _previousServer = GameServer.Instance;
            _language = new PetTestLanguageScope();
            GameServer.LoadTestDouble((CatalogTestServer)RuntimeHelpers.GetUninitializedObject(typeof(CatalogTestServer)));
        }

        [TearDown]
        public void TearDown()
        {
            GameServer.LoadTestDouble(_previousServer);
            _language.Dispose();
        }

        private static readonly eRealm[] Realms = [eRealm.Albion, eRealm.Midgard, eRealm.Hibernia];

        [Test]
        public void EveryBuildClassesShowsResolvesThroughSpawn()
        {
            foreach (eRealm realm in Realms)
            {
                var classLines = ClassesCommandHandler.Lines(realm).Where(line => line.Contains(" - builds: ")).ToArray();
                Assert.That(classLines.Length, Is.EqualTo(TemporaryGroupClassCatalog.ForRealm(realm).Count()), realm.ToString());
                foreach (string line in classLines)
                {
                    string className = line[..line.IndexOf(" - builds: ")];
                    string builds = line[(line.IndexOf(" - builds: ") + 11)..line.IndexOf(" - role: ")];
                    Assert.That(SpawnTemporaryGroupBotCommandHandler.TryResolve(realm, className, out eCharacterClass resolved,
                        out _, out string classProblem), Is.True, $"{line}: {classProblem}");
                    if (builds == "single build")
                    {
                        Assert.That(TemporaryGroupSpecCatalog.For(resolved), Is.Empty, line);
                        continue;
                    }
                    string[] names = builds.Split(", ");
                    Assert.That(names, Is.EqualTo(TemporaryGroupSpecCatalog.For(resolved).Select(option => option.Label).ToArray()), line);
                    foreach (string build in names)
                    {
                        Assert.That(SpawnTemporaryGroupBotCommandHandler.TryResolve(realm, $"{className} {build}", out eCharacterClass withBuild,
                            out TemporaryGroupSpecCatalog.Option spec, out string problem), Is.True, $"/spawn {className} {build}: {problem}");
                        Assert.That(withBuild, Is.EqualTo(resolved));
                        Assert.That(spec?.Label, Is.EqualTo(build));
                        Assert.That(ClassesCommandHandler.Lines(realm).Count(l => l.StartsWith(className + " - ")), Is.EqualTo(1));
                    }
                }
            }
        }

        [Test]
        public void TheRoleIsLabelledAsARoleAndTheFooterSaysHowToPickABuild()
        {
            var lines = ClassesCommandHandler.Lines(eRealm.Midgard);
            string shaman = lines.Single(line => line.StartsWith("Shaman - "));
            Assert.That(shaman, Does.Contain("role: "));
            Assert.That(shaman, Does.Not.Contain("(Buffer)").And.Not.Contain("("));
            Assert.That(shaman, Does.Contain(TemporaryGroupSpecCatalog.Describe(eCharacterClass.Shaman)));
            string footer = lines[^1];
            Assert.That(footer, Does.Contain("/spawn <class> [build]"));
            Assert.That(footer, Does.Contain("/spawn specs"));
        }

        [Test]
        public void TheFooterExampleResolves()
        {
            foreach (eRealm realm in Realms)
            {
                string footer = ClassesCommandHandler.Lines(realm)[^1];
                int start = footer.IndexOf("for example /spawn ") + "for example /spawn ".Length;
                string example = footer[start..footer.IndexOf('.', start)];
                Assert.That(SpawnTemporaryGroupBotCommandHandler.TryResolve(realm, example, out _, out var spec, out string problem),
                    Is.True, $"{realm}: /spawn {example}: {problem}");
                Assert.That(spec, Is.Not.Null, example);
            }
        }

        [Test]
        public void ASingleBuildClassSaysSo()
        {
            // Every Classic + SI helper class has several builds today; a class with none to choose from says so.
            Assert.That(ClassesCommandHandler.BuildText(""), Is.EqualTo("single build"));
            Assert.That(ClassesCommandHandler.BuildText("Augmentation, Mending"), Is.EqualTo("Augmentation, Mending"));
            foreach (eRealm realm in Realms)
                foreach ((eCharacterClass characterClass, _) in TemporaryGroupClassCatalog.ForRealm(realm))
                    Assert.That(ClassesCommandHandler.BuildText(characterClass) == "single build",
                        Is.EqualTo(TemporaryGroupSpecCatalog.For(characterClass).Count == 0), characterClass.ToString());
        }

        [Test]
        public void ARoleTypedAsABuildIsCalledARoleAndTheBuildsAreNamed()
        {
            Assert.That(SpawnTemporaryGroupBotCommandHandler.TryResolve(eRealm.Midgard, "shaman buffer", out _, out _,
                out string problem), Is.False);
            Assert.That(problem, Does.Contain("party role, not a build"));
            Assert.That(problem, Does.Contain(TemporaryGroupSpecCatalog.Describe(eCharacterClass.Shaman)));
            Assert.That(SpawnTemporaryGroupBotCommandHandler.IsRoleWord("Tank"), Is.True);
            Assert.That(SpawnTemporaryGroupBotCommandHandler.IsRoleWord("Augmentation"), Is.False);
            // No build is named like a role, so a role word never hides a real build.
            foreach (eRealm realm in Realms)
                foreach ((eCharacterClass characterClass, _) in TemporaryGroupClassCatalog.ForRealm(realm))
                    foreach (TemporaryGroupSpecCatalog.Option option in TemporaryGroupSpecCatalog.For(characterClass))
                        Assert.That(SpawnTemporaryGroupBotCommandHandler.IsRoleWord(option.Label), Is.False,
                            $"{characterClass} ({option.Label})");
        }
    }
}
