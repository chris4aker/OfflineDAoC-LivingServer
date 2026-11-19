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
    /// <summary>/spawn specs (owner 2026-10-28): builds named after the spec line each one really trains highest.</summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_TemporaryGroupSpecCatalog
    {
        // Weapon lines resolve through SkillBase, which needs a server and its language strings (as UT_SavageBotCombat).
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

        private static string[] Labels(eCharacterClass characterClass) =>
            TemporaryGroupSpecCatalog.For(characterClass).Select(option => option.Label).OrderBy(label => label).ToArray();

        [Test]
        public void BuildsAreNamedAfterWhatTheyTrainHighest() => Assert.Multiple(() =>
        {
            // The Friar's "staff" family includes variants that train Enhancement highest, and its "rejuvenation" family
            // one that trains Staff and one Enhancement; the options are the lines really trained.
            Assert.That(Labels(eCharacterClass.Friar), Is.EqualTo(new[] { Specs.Enhancement, "Rebirth", Specs.Staff }));
            Assert.That(Labels(eCharacterClass.Healer), Is.EqualTo(new[] { Specs.Augmentation, Specs.Mending, Specs.Pacification }));
            Assert.That(Labels(eCharacterClass.Druid), Is.EqualTo(new[] { Specs.Nature, Specs.Nurture, Specs.Regrowth }));
            Assert.That(Labels(eCharacterClass.Theurgist), Is.EqualTo(new[] { "Abrasion", "Refrigeration", "Vapormancy" }),
                "the class's own advanced lines, not the trainer's Earth, Cold and Wind Magic");
        });

        [Test]
        public void AnOptionSpawnsABuildThatTrainsItHighest()
        {
            foreach (eCharacterClass characterClass in new[] { eCharacterClass.Friar, eCharacterClass.Healer, eCharacterClass.Druid })
                foreach (TemporaryGroupSpecCatalog.Option option in TemporaryGroupSpecCatalog.For(characterClass))
                    for (int i = 0; i < 20; i++)
                        Assert.That(TemporaryGroupSpecCatalog.TopLine(TemporaryGroupSpecCatalog.Create(characterClass, option)),
                            Is.EqualTo(option.Line ?? option.Label), $"{characterClass} ({option.Label})");
        }

        [Test]
        public void SpecsAreTypedInFull()
        {
            Assert.That(TemporaryGroupSpecCatalog.TryResolve(eCharacterClass.Healer, "augmentation", out var option), Is.True);
            Assert.That(option.Label, Is.EqualTo(Specs.Augmentation));
            Assert.That(TemporaryGroupSpecCatalog.TryResolve(eCharacterClass.Theurgist, "Cold Magic", out _), Is.True);
            Assert.That(TemporaryGroupSpecCatalog.TryResolve(eCharacterClass.Healer, "aug", out _), Is.False);
        }

        [Test]
        public void WeaponSetupsKeepTheirNames() => Assert.Multiple(() =>
        {
            Assert.That(TemporaryGroupSpecCatalog.SetupLabel(eCharacterClass.Warrior, eSpecType.OneHandAndShield), Is.EqualTo("One Handed and Shield"));
            Assert.That(TemporaryGroupSpecCatalog.SetupLabel(eCharacterClass.Armsman, eSpecType.TwoHandHybrid), Is.EqualTo("Two Handed Hybrid"));
            Assert.That(TemporaryGroupSpecCatalog.SetupLabel(eCharacterClass.Paladin, eSpecType.TwoHandHybrid), Is.EqualTo("Two Handed"));
            Assert.That(TemporaryGroupSpecCatalog.SetupLabel(eCharacterClass.Blademaster, eSpecType.DualWield), Is.EqualTo("Celtic Dual"));
            Assert.That(TemporaryGroupSpecCatalog.SetupLabel(eCharacterClass.Savage, eSpecType.DualWield), Is.EqualTo("Hand to Hand"));
        });

        // Owner 2026-10-29 (issues 10/18): the one-build classes offer their variants by the line trained highest,
        // and four classes gained a build: Death Servant Necromancer, Soulrending Reaver, Regrowth Bard, Spear Hunter.
        [Test]
        public void OneBuildClassesOfferTheirVariants() => Assert.Multiple(() =>
        {
            Assert.That(Labels(eCharacterClass.Berserker), Does.Contain(Specs.Left_Axe));
            Assert.That(Labels(eCharacterClass.Infiltrator), Does.Contain(Specs.Critical_Strike).And.Contain(Specs.Dual_Wield));
            Assert.That(Labels(eCharacterClass.Nightshade), Does.Contain(Specs.Critical_Strike).And.Contain(Specs.Celtic_Dual));
            Assert.That(Labels(eCharacterClass.Ranger), Does.Contain(Specs.Pathfinding).And.Contain(Specs.Celtic_Dual));
            Assert.That(Labels(eCharacterClass.Necromancer), Does.Contain(Specs.Death_Servant));
            Assert.That(Labels(eCharacterClass.Reaver), Does.Contain(Specs.Soulrending));
            Assert.That(Labels(eCharacterClass.Bard), Does.Contain(Specs.Regrowth));
            Assert.That(Labels(eCharacterClass.Hunter), Does.Contain(Specs.Spear));
            foreach (eCharacterClass characterClass in new[] { eCharacterClass.Infiltrator, eCharacterClass.Minstrel,
                eCharacterClass.Reaver, eCharacterClass.Scout, eCharacterClass.Bard, eCharacterClass.Nightshade,
                eCharacterClass.Ranger, eCharacterClass.Valewalker, eCharacterClass.Berserker, eCharacterClass.Hunter })
                Assert.That(TemporaryGroupSpecCatalog.For(characterClass).Count, Is.GreaterThan(1), characterClass.ToString());
        });

        [Test]
        public void AddedBuildsTrainTheirLineHighest()
        {
            foreach (eCharacterClass characterClass in new[] { eCharacterClass.Necromancer, eCharacterClass.Reaver,
                eCharacterClass.Bard, eCharacterClass.Hunter, eCharacterClass.Infiltrator, eCharacterClass.Berserker })
                foreach (TemporaryGroupSpecCatalog.Option option in TemporaryGroupSpecCatalog.For(characterClass))
                    for (int i = 0; i < 10; i++)
                        Assert.That(TemporaryGroupSpecCatalog.TopLine(TemporaryGroupSpecCatalog.Create(characterClass, option)),
                            Is.EqualTo(option.Line ?? option.Label), $"{characterClass} ({option.Label})");
        }

        [Test]
        public void AddedBuildsKeepTheirGearSetup() => Assert.Multiple(() =>
        {
            Assert.That(BotSpec.GetSpec(eCharacterClass.Reaver, eSpecType.SoulrendingReaver).SpecType, Is.EqualTo(eSpecType.OneHandAndShield));
            Assert.That(BotSpec.GetSpec(eCharacterClass.Reaver, eSpecType.SoulrendingReaver).WeaponOneType, Is.EqualTo(eObjectType.Flexible));
            Assert.That(BotSpec.GetSpec(eCharacterClass.Bard, eSpecType.RegrowthBard).SpecType, Is.EqualTo(eSpecType.Instrument));
            Assert.That(BotSpec.GetSpec(eCharacterClass.Hunter, eSpecType.SpearHunter).WeaponOneType, Is.EqualTo(eObjectType.Spear));
            Assert.That(BotSpec.GetSpec(eCharacterClass.Hunter, eSpecType.SpearHunter).Is2H, Is.True);
        });
    }
}
