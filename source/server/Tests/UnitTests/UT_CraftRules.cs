using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP1: AbstractCraftingSkill, the professions, Salvage and Repair now take every number from CraftRules,
    /// so a gamebot crafter rolls as a player does. These tests check the player's methods give CraftRules' answers,
    /// pin a few literal results of the formulas (WP0's parity tests compared against the old bodies, which are gone),
    /// and cover the rules WP1 added: the pace on the secondary skills, salvage and repair, and the upkeep durability rule.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_CraftRules
    {
        private sealed class CraftPlayer : GamePlayer
        {
            private CraftPlayer() : base(null, null) { }
            public Dictionary<eCraftingSkill, int> Skills;
            public Region TestRegion;
            public int TestBonus;
            public double TestSpeed = 1.0;
            public GameClient TestClient;
            public IPacketLib Packets;
            public override int GetCraftingSkillValue(eCraftingSkill skill) => Skills.TryGetValue(skill, out int value) ? value : 0;
            public override bool GainCraftingSkill(eCraftingSkill skill, int count)
            {
                Skills[skill] = GetCraftingSkillValue(skill) + count;
                return true;
            }
            public override Region CurrentRegion { get => TestRegion; set { } }
            public override int CraftingSkillBonus => TestBonus;
            public override double CraftingSpeed => TestSpeed;
            public override GameClient Client => TestClient;
            public override IPacketLib Out => Packets;
            public override eRealm Realm { get; set; } = eRealm.Albion;
        }

        private sealed class CapitalRegion : Region
        {
            private CapitalRegion() : base(null) { }
            public bool Capital;
            public override bool IsCapitalCity => Capital;
        }

        private sealed class Server : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => Empty;
        }

        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();

        private GameServer _previous;
        private double _rate;
        private int _maxCraftTime;
        private bool _keepBonuses;
        private string _upkeep;

        [SetUp]
        public void SetUp()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _rate = CraftingEconomyProperties.CRAFTING_SKILL_RATE;
            _maxCraftTime = Properties.MAX_CRAFT_TIME;
            _keepBonuses = Properties.USE_LIVE_KEEP_BONUSES;
            _upkeep = CraftingEconomyProperties.PLAYER_UPKEEP;
            Properties.USE_LIVE_KEEP_BONUSES = false;
            Properties.MAX_CRAFT_TIME = 0;
        }

        [TearDown]
        public void TearDown()
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = _rate;
            CraftingEconomyProperties.PLAYER_UPKEEP = _upkeep;
            Properties.MAX_CRAFT_TIME = _maxCraftTime;
            Properties.USE_LIVE_KEEP_BONUSES = _keepBonuses;
            GameServer.LoadTestDouble(_previous);
        }

        private static readonly int[] SkillGrid = Enumerable.Range(0, 80).Select(i => i * 14).ToArray();
        private static readonly int[] LevelGrid = Enumerable.Range(0, 60).Select(i => i * 19).ToArray();

        [TestCase(false, 0)]
        [TestCase(true, 5)]
        [TestCase(true, 60)]
        public void ThePlayersChancesAreTheRules(bool capital, int bonus)
        {
            ArmorCrafting craft = Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting);
            CraftPlayer player = Player(capital);
            player.TestBonus = bonus;
            foreach (int skill in SkillGrid)
            {
                player.Skills[eCraftingSkill.ArmorCrafting] = skill;
                foreach (int level in LevelGrid)
                {
                    Assert.That(craft.CalculateChanceToMakeItem(player, level), Is.EqualTo(CraftRules.ChanceToMake(skill, level)));
                    Assert.That(craft.ChanceToMakeAt(skill, level), Is.EqualTo(CraftRules.ChanceToMake(skill, level)));
                    Assert.That(craft.BaseChanceToGainAt(skill, level), Is.EqualTo(CraftRules.BaseChanceToGainPoint(skill, level)));
                    Assert.That(craft.CalculateChanceToGainPoint(player, level),
                        Is.EqualTo(CraftRules.ChanceToGainPoint(new CraftContext(skill, level, capital, bonus))));
                    Assert.That(craft.GetItemCon(skill, level), Is.EqualTo((int)CraftRules.Con(skill, level)));
                }
            }
        }

        [Test]
        public void ThePlayersClockIsTheRules_AndDividedRealmsCraftingTimeAtMatchesAtSpeedOne()
        {
            ArmorCrafting craft = Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting);
            CraftPlayer player = Player();
            Recipe recipe = RecipeOf(515, 12, 4);
            foreach (int skill in SkillGrid)
            {
                player.Skills[eCraftingSkill.ArmorCrafting] = skill;
                int time = craft.GetCraftingTime(player, recipe);
                Assert.That(time, Is.EqualTo(CraftRules.CraftingTimeSeconds(craft.CraftContextFor(player, recipe), CraftRules.IngredientCounts(recipe))));
                Assert.That(craft.CraftingTimeAt(skill, recipe, eRealm.Albion), Is.EqualTo(time), $"skill {skill}");
            }
        }

        [Test]
        public void LiteralTimes_OfTheDocumentedFormula()
        {
            // (floor(515 / 100) + 1) x (12 + 4) / 4 = 24 seconds; orange at 500 (x1), grey at 600 (x0.4 = 9), halved by speed 2.
            ArmorCrafting craft = Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting);
            CraftPlayer player = Player();
            Recipe recipe = RecipeOf(515, 12, 4);
            player.Skills[eCraftingSkill.ArmorCrafting] = 500;
            Assert.That(craft.GetCraftingTime(player, recipe), Is.EqualTo(24));
            player.Skills[eCraftingSkill.ArmorCrafting] = 600;
            Assert.That(craft.GetCraftingTime(player, recipe), Is.EqualTo(9));
            player.Skills[eCraftingSkill.ArmorCrafting] = 500;
            player.TestSpeed = 2.0;
            Assert.That(craft.GetCraftingTime(player, recipe), Is.EqualTo(12));
            Properties.MAX_CRAFT_TIME = 10;
            Assert.That(craft.GetCraftingTime(player, recipe), Is.EqualTo(10));
            Assert.That(craft.GetCraftingTime(Player(privLevel: ePrivLevel.GM), recipe), Is.Zero, "a GM crafts instantly");
        }

        [TestCase(1, 1)]
        [TestCase(3, 3)]
        public void SecondarySkillsGainAtThePlayerPace(double rate, int points)
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = rate;
            ArmorCrafting craft = Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting);
            CraftPlayer player = Player(capital: true);
            player.TestBonus = 100; // a capital with a +100 bonus: every non-grey craft wins its gain
            player.Skills[eCraftingSkill.ArmorCrafting] = 300;
            player.Skills[eCraftingSkill.MetalWorking] = 300;
            player.Skills[eCraftingSkill.LeatherCrafting] = 1299;

            craft.GainCraftingSkillPoints(player, RecipeOf(300, 12, 4)); // bars (519) and leather (521)

            Assert.That(player.Skills[eCraftingSkill.ArmorCrafting], Is.EqualTo(300 + points), "the trade");
            Assert.That(player.Skills[eCraftingSkill.MetalWorking], Is.EqualTo(300 + points), "a secondary");
            Assert.That(player.Skills[eCraftingSkill.LeatherCrafting], Is.EqualTo(1300), "secondaries stop at 1300");
        }

        [Test]
        public void SalvageYieldRules()
        {
            Assert.Multiple(() =>
            {
                Assert.That(CraftRules.SalvageSkillPercent(500, 1000), Is.EqualTo(50));
                Assert.That(CraftRules.SalvageSkillPercent(990, 1000), Is.EqualTo(99));
                Assert.That(CraftRules.SalvageSkillPercent(1300, 1000), Is.EqualTo(100));
                Assert.That(CraftRules.SalvageSkillPercent(-1, 500), Is.Zero, "untrained");
                Assert.That(CraftRules.SalvageSkillPercent(10, 0), Is.EqualTo(100), "no craft level: no division by zero");
                Assert.That(CraftRules.SalvageYield(500, 1000, 10), Is.EqualTo(5));
                Assert.That(CraftRules.SalvageYield(1, 1000, 10), Is.EqualTo(1), "at least one");
                Assert.That(CraftRules.SalvageYield(1000, 1000, 10), Is.EqualTo(10));
            });
        }

        [Test]
        public void AMagicalItemSalvagesAsClothAndKeepsItsType()
        {
            CraftPlayer player = Player();
            player.Skills[eCraftingSkill.ClothWorking] = 300;
            DbInventoryItem item = GameInventoryItem.Create(new DbItemTemplate
            {
                Id_nb = "magic_robe", Name = "magic robe", Object_Type = (int)eObjectType.Magical, Item_Type = (int)eInventorySlot.TorsoArmor, Level = 20,
            });
            item.Object_Type = (int)eObjectType.Magical;
            item.Level = 20;

            DbInventoryItem asCloth = GameInventoryItem.Create(new DbItemTemplate
            {
                Id_nb = "cloth_robe", Name = "cloth robe", Object_Type = (int)eObjectType.Cloth, Item_Type = (int)eInventorySlot.TorsoArmor, Level = 20,
            });
            asCloth.Object_Type = (int)eObjectType.Cloth;
            asCloth.Level = 20;
            int clothLevel = CraftingMgr.GetItemCraftLevel(asCloth);

            int yield = Salvage.GetYieldPenalty(player, item, 10);

            Assert.That(item.Object_Type, Is.EqualTo((int)eObjectType.Magical), "the type is changed for the calculation only (c7ee233d0)");
            Assert.That(yield, Is.EqualTo(CraftRules.SalvageYield(300, clothLevel, 10)));
        }

        [Test]
        public void RepairRulesDelegate()
        {
            Assert.Multiple(() =>
            {
                foreach (int skill in new[] { -1, 0, 1, 250, 499, 500, 750, 1000 })
                    foreach (int level in new[] { -5, 0, 1, 500, 1000 })
                    {
                        Assert.That(Repair.RepairChancePercent(skill, level), Is.EqualTo(CraftRules.RepairChancePercent(skill, level)));
                        Assert.That(Repair.HasSkillToRepair(skill, level, 50), Is.EqualTo(CraftRules.HasSkillToRepair(skill, level, 50)));
                    }
                Assert.That(CraftRules.RepairChancePercent(500, 1000), Is.EqualTo(10), "half the craft level: 10%");
                Assert.That(CraftRules.RepairChancePercent(1000, 1000), Is.EqualTo(100));
                Assert.That(Repair.SiegeRepairChancePercent(0), Is.EqualTo(40));
                Assert.That(Repair.SiegeRepairChancePercent(51), Is.EqualTo(90));
            });
        }

        [TestCase("classic", 100)]
        [TestCase("light", 0)]
        [TestCase("free", 0)]
        public void ARepairSpendsDurabilityOnlyUnderClassicUpkeep(string upkeep, int spent)
        {
            CraftingEconomyProperties.PLAYER_UPKEEP = upkeep;
            DbInventoryItem item = GameInventoryItem.Create(new DbItemTemplate { Id_nb = "sword", Name = "sword", MaxCondition = 1000, MaxDurability = 1000 });
            item.MaxCondition = 1000;
            item.Condition = 900;
            item.MaxDurability = 1000;
            item.Durability = 1000;

            Assert.That(Repair.ModifyConditionAndDurability(item), Is.True);
            Assert.That(item.Condition, Is.EqualTo(1000));
            Assert.That(item.Durability, Is.EqualTo(1000 - spent));
        }

        [Test]
        public void TheDefaultUpkeepIsLight_SoRepairsKeepDurability()
        {
            Assert.That(CraftingEconomyProperties.ParseUpkeep(_upkeep), Is.EqualTo(UpkeepPolicy.Mode.Light), "the default");
            Assert.That(UpkeepPolicy.ConsumesDurability(UpkeepPolicy.Mode.Light), Is.False);
        }

        // ---- helpers ----

        private static T Craft<T>(eCraftingSkill skill) where T : AbstractCraftingSkill
        {
            T craft = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            craft.eSkill = skill;
            return craft;
        }

        private static CraftPlayer Player(bool capital = false, ePrivLevel privLevel = ePrivLevel.Player)
        {
            CraftPlayer player = (CraftPlayer)RuntimeHelpers.GetUninitializedObject(typeof(CraftPlayer));
            player.Skills = new Dictionary<eCraftingSkill, int>();
            player.TestSpeed = 1.0;
            CapitalRegion region = (CapitalRegion)RuntimeHelpers.GetUninitializedObject(typeof(CapitalRegion));
            region.Capital = capital;
            player.TestRegion = region;
            player.TestClient = new GameClient((Socket)null) { Account = new DbAccount { PrivLevel = (uint)privLevel } };
            player.Packets = DispatchProxy.Create<IPacketLib, UT_RealmExchangeEconomy.NoOpPacketLib>();
            return player;
        }

        private static Recipe RecipeOf(int level, int bars, int leather)
        {
            DbItemTemplate product = new() { Id_nb = "rules_product", Name = "rules product", Object_Type = (int)eObjectType.Plate, Level = 10 };
            var ingredients = new List<Ingredient>
            {
                new(bars, new DbItemTemplate { Id_nb = "bars", Name = "bars", Model = 519, Price = 10 }),
                new(leather, new DbItemTemplate { Id_nb = "leather", Name = "leather", Model = 521, Price = 10 }),
            };
            return new Recipe(product, ingredients, eCraftingSkill.ArmorCrafting, level, false);
        }
    }
}
