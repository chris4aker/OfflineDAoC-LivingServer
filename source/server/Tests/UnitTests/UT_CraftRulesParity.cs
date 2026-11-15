using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using DOL.Database;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP0: CraftRules and SpellcraftRules are verbatim copies of today's AbstractCraftingSkill and
    /// SpellCrafting maths. Each is checked against the original method over a grid (the originals run on a test
    /// player whose skill, region, speed and trade window are set), and against the tables the design doc records.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_CraftRulesParity
    {
        private sealed class CraftPlayer : GamePlayer
        {
            private CraftPlayer() : base(null, null) { }
            public readonly Dictionary<eCraftingSkill, int> Skills = new();
            public Region TestRegion;
            public int TestBonus;
            public double TestSpeed = 1.0;
            public GameClient TestClient;
            public override int GetCraftingSkillValue(eCraftingSkill skill) => Skills.TryGetValue(skill, out int value) ? value : 0;
            public override Region CurrentRegion { get => TestRegion; set { } }
            public override int CraftingSkillBonus => TestBonus;
            public override double CraftingSpeed => TestSpeed;
            public override GameClient Client => TestClient;
            public override eRealm Realm { get; set; } = eRealm.Albion;
        }

        private sealed class CapitalRegion : Region
        {
            private CapitalRegion() : base(null) { }
            public bool Capital;
            public override bool IsCapitalCity => Capital;
        }

        /// <summary>Opens the protected spellcraft maths of the original.</summary>
        private sealed class OpenSpellCrafting : SpellCrafting
        {
            public int MaxImbue(DbInventoryItem item) => GetItemMaxImbuePoints(item);
            public int Gem(int type, int value) => GetGemImbuePoints(type, value);
            public int Total(GamePlayer player, DbInventoryItem item) => GetTotalImbuePoints(player, item);
            public int Overcharge(GamePlayer player, DbInventoryItem item, int max, int bonus) => CalculateChanceToOverchargeItem(player, item, max, bonus);
            public int Preserve(GamePlayer player, DbInventoryItem item, int max, int bonus) => CalculateChanceToPreserveItem(player, item, max, bonus);
            public int Penalty(int max, int bonus) => GetOverchargePenality(max, bonus);
        }

        public class TradeWindowProxy : DispatchProxy
        {
            public ArrayList Items = new();
            public readonly Lock Gate = new();
            protected override object Invoke(MethodInfo method, object[] args) => method.Name switch
            {
                "get_TradeItems" => Items,
                "get_Lock" => Gate,
                _ => method.ReturnType.IsValueType && method.ReturnType != typeof(void) ? Activator.CreateInstance(method.ReturnType) : null,
            };
        }

        private int _savedMaxCraftTime;
        private bool _savedKeepBonuses;

        [SetUp]
        public void SetUp()
        {
            _savedMaxCraftTime = Properties.MAX_CRAFT_TIME;
            _savedKeepBonuses = Properties.USE_LIVE_KEEP_BONUSES;
            Properties.USE_LIVE_KEEP_BONUSES = false;
        }

        [TearDown]
        public void TearDown()
        {
            Properties.MAX_CRAFT_TIME = _savedMaxCraftTime;
            Properties.USE_LIVE_KEEP_BONUSES = _savedKeepBonuses;
        }

        private static readonly int[] SkillGrid = Enumerable.Range(0, 160).Select(i => i * 7).ToArray();      // 0..1113
        private static readonly int[] LevelGrid = Enumerable.Range(0, 90).Select(i => i * 13).ToArray();      // 0..1157

        [Test]
        public void Con_MatchesGetItemCon_OverTheGrid()
        {
            AbstractCraftingSkill craft = Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting);
            foreach (int skill in SkillGrid)
                foreach (int level in LevelGrid)
                    Assert.That((int)CraftRules.Con(skill, level), Is.EqualTo(craft.GetItemCon(skill, level)), $"skill {skill} level {level}");
        }

        [Test]
        public void ChanceToMake_MatchesCalculateChanceToMakeItem_OverTheGrid()
        {
            AbstractCraftingSkill craft = Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting);
            CraftPlayer player = Player();
            foreach (int skill in SkillGrid)
            {
                player.Skills[eCraftingSkill.ArmorCrafting] = skill;
                foreach (int level in LevelGrid)
                    Assert.That(CraftRules.ChanceToMake(skill, level), Is.EqualTo(craft.CalculateChanceToMakeItem(player, level)), $"skill {skill} level {level}");
            }
        }

        [TestCase(false, 0)]
        [TestCase(true, 5)]
        [TestCase(true, 98)]
        [TestCase(true, -50)]
        public void ChanceToGainPoint_MatchesCalculateChanceToGainPoint(bool capital, int bonus)
        {
            AbstractCraftingSkill craft = Craft<Tailoring>(eCraftingSkill.Tailoring);
            CraftPlayer player = Player(capital);
            player.TestBonus = bonus;
            foreach (int skill in SkillGrid)
            {
                player.Skills[eCraftingSkill.Tailoring] = skill;
                foreach (int level in LevelGrid)
                {
                    var context = new CraftContext(skill, level, capital, bonus);
                    Assert.That(CraftRules.ChanceToGainPoint(context), Is.EqualTo(craft.CalculateChanceToGainPoint(player, level)),
                        $"skill {skill} level {level}");
                }
            }
        }

        [TestCase(1.0, 0)]
        [TestCase(1.5, 0)]
        [TestCase(2.3, 0)]
        [TestCase(0.7, 0)]
        [TestCase(1.0, 9)]
        public void CraftingTime_MatchesGetCraftingTime(double speed, int maxCraftTime)
        {
            Properties.MAX_CRAFT_TIME = maxCraftTime;
            AbstractCraftingSkill craft = Craft<WeaponCrafting>(eCraftingSkill.WeaponCrafting);
            CraftPlayer player = Player();
            player.TestSpeed = speed;
            int[][] ingredientSets = [[1], [4, 10], [12, 3, 2], [150, 7], [250, 101, 99], [40, 40, 40, 40]];
            foreach (int[] counts in ingredientSets)
                foreach (int level in LevelGrid.Where(level => level % 2 == 0))
                {
                    Recipe recipe = RecipeOf(level, counts);
                    foreach (int skill in new[] { 0, level - 60, level - 25, level, level + 20, level + 40, level + 49, level + 60, 1000 })
                    {
                        player.Skills[eCraftingSkill.WeaponCrafting] = Math.Max(0, skill);
                        var context = new CraftContext(Math.Max(0, skill), level, SpeedMultiplier: speed, MaxCraftTimeSeconds: maxCraftTime);
                        Assert.That(CraftRules.CraftingTimeSeconds(context, CraftRules.IngredientCounts(recipe)),
                            Is.EqualTo(craft.GetCraftingTime(player, recipe)), $"level {level} skill {skill} counts {string.Join(",", counts)}");
                    }
                }
        }

        [Test]
        public void CraftingTime_IsZeroForAGm_AsTheOriginal()
        {
            AbstractCraftingSkill craft = Craft<WeaponCrafting>(eCraftingSkill.WeaponCrafting);
            CraftPlayer player = Player(privLevel: ePrivLevel.GM);
            Recipe recipe = RecipeOf(300, [10, 5]);
            Assert.That(craft.GetCraftingTime(player, recipe), Is.Zero);
            Assert.That(CraftRules.CraftingTimeSeconds(new CraftContext(300, 300, InstantGm: true), [10, 5]), Is.Zero);
        }

        [Test]
        public void CraftingTime_RelicsAndKeepBonusAndConFactor_FollowTheFormula()
        {
            // (floor(450 / 100) + 1) x 14 / 4 = 17; a relic: x 0.95 = 16; keep 5%: / 1.05 = 15; blue (x 0.8) = 12.
            int[] counts = [10, 4];
            Assert.That(CraftRules.CraftingTimeSeconds(new CraftContext(450, 450), counts), Is.EqualTo(17));
            Assert.That(CraftRules.CraftingTimeSeconds(new CraftContext(450, 450, RealmRelics: 1), counts), Is.EqualTo(16));
            Assert.That(CraftRules.CraftingTimeSeconds(new CraftContext(450, 450, RealmRelics: 1,
                KeepTimerDivisor: CraftRules.KeepTimerDivisor(true, false)), counts), Is.EqualTo(15));
            Assert.That(CraftRules.CraftingTimeSeconds(new CraftContext(470, 450, RealmRelics: 1,
                KeepTimerDivisor: 1.05), counts), Is.EqualTo(12));
            Assert.That(CraftRules.CraftingTimeSeconds(new CraftContext(1000, 1, MaxCraftTimeSeconds: 5), [1]), Is.EqualTo(1),
                "never under one second");
            Assert.That(CraftRules.KeepTimerDivisor(false, true), Is.EqualTo(1.03));
            Assert.That(CraftRules.KeepTimerDivisor(false, false), Is.EqualTo(1.0));
        }

        [Test]
        public void DocumentedTables_ChanceToMakeAndToGain_ByCon()
        {
            // docs/CRAFTING-AND-ECONOMY.md 3.1: grey, green and blue always; yellow 92%; orange 84%; red 68%; purple never.
            // Gain: green 15%, blue 30%, yellow 45%, orange 55%, red 65%, grey and purple nothing.
            (int Diff, CraftCon Con, int Make, int Gain)[] rows =
            [
                (-60, CraftCon.Grey, 100, 0), (-40, CraftCon.Green, 100, 15), (-20, CraftCon.Blue, 100, 30),
                (-5, CraftCon.Yellow, 92, 45), (10, CraftCon.Orange, 84, 55), (30, CraftCon.Red, 68, 65), (60, CraftCon.Purple, 0, 0),
            ];
            foreach ((int diff, CraftCon con, int make, int gain) in rows)
            {
                Assert.That(CraftRules.Con(500, 500 + diff), Is.EqualTo(con));
                Assert.That(CraftRules.ChanceToMake(500, 500 + diff), Is.EqualTo(make), con.ToString());
                Assert.That(CraftRules.ChanceToGainPoint(new CraftContext(500, 500 + diff)), Is.EqualTo(gain), con.ToString());
                Assert.That(CraftRules.ChanceToGainPoint(new CraftContext(500, 500 + diff, true, 5)),
                    Is.EqualTo(gain == 0 ? 0 : gain + 5), "the capital adds its 5 only where a point can be gained");
            }
            // The band edges of GetItemCon
            Assert.That(CraftRules.Con(100, 50), Is.EqualTo(CraftCon.Grey));
            Assert.That(CraftRules.Con(100, 51), Is.EqualTo(CraftCon.Green));
            Assert.That(CraftRules.Con(100, 69), Is.EqualTo(CraftCon.Green));
            Assert.That(CraftRules.Con(100, 70), Is.EqualTo(CraftCon.Blue));
            Assert.That(CraftRules.Con(100, 89), Is.EqualTo(CraftCon.Blue));
            Assert.That(CraftRules.Con(100, 90), Is.EqualTo(CraftCon.Yellow));
            Assert.That(CraftRules.Con(100, 100), Is.EqualTo(CraftCon.Yellow));
            Assert.That(CraftRules.Con(100, 101), Is.EqualTo(CraftCon.Orange));
            Assert.That(CraftRules.Con(100, 119), Is.EqualTo(CraftCon.Orange));
            Assert.That(CraftRules.Con(100, 120), Is.EqualTo(CraftCon.Red));
            Assert.That(CraftRules.Con(100, 149), Is.EqualTo(CraftCon.Red));
            Assert.That(CraftRules.Con(100, 150), Is.EqualTo(CraftCon.Purple));
        }

        [TestCase(1000, 500, true)]    // legendary
        [TestCase(500, 400, true)]     // grey
        [TestCase(500, 495, false)]    // yellow
        [TestCase(500, 515, false)]    // orange
        [TestCase(500, 470, false)]    // blue
        [TestCase(500, 540, false)]    // red
        public void Quality_OnlyNinetySixToNinetyNine_AndMasterpiecesOnlyAtLegendaryOrGrey(int skill, int level, bool masterpieces)
        {
            var random = new Random(4127);
            var counts = new int[101];
            const int Rolls = 100_000;
            for (int i = 0; i < Rolls; i++)
                counts[CraftRules.RollQuality(skill, level, random)]++;
            Assert.That(counts.Take(96).Sum(), Is.Zero, "never under 96 (the 1.87 rule)");
            double masterpieceShare = counts[100] / (double)Rolls;
            if (masterpieces)
                Assert.That(masterpieceShare, Is.InRange(0.015, 0.025), "a 2% masterpiece");
            else
                Assert.That(counts[100], Is.Zero, "no masterpiece below 1000 on a coloured recipe");
        }

        [TestCase(1000, 500)]
        [TestCase(500, 400)]
        [TestCase(500, 495)]
        [TestCase(500, 515)]
        [TestCase(500, 470)]
        [TestCase(500, 540)]
        public void Quality_DistributionMatchesTheOriginalGetQuality(int skill, int level)
        {
            AbstractCraftingSkill craft = Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting);
            CraftPlayer player = Player();
            player.Skills[eCraftingSkill.ArmorCrafting] = skill;
            MethodInfo original = typeof(AbstractCraftingSkill).GetMethod("GetQuality", BindingFlags.Instance | BindingFlags.NonPublic);
            const int Rolls = 60_000;
            var ours = new int[101];
            var theirs = new int[101];
            var random = new Random(77);
            for (int i = 0; i < Rolls; i++)
            {
                ours[CraftRules.RollQuality(skill, level, random)]++;
                theirs[(int)original.Invoke(craft, [player, level])]++;
            }
            for (int quality = 96; quality <= 100; quality++)
                Assert.That(ours[quality] / (double)Rolls, Is.EqualTo(theirs[quality] / (double)Rolls).Within(0.015), $"{quality}%");
            Assert.That(ours.Take(96).Sum() + theirs.Take(96).Sum(), Is.Zero);
        }

        [Test]
        public void Quality_IsRepeatableWithASeed()
        {
            int[] first = Enumerable.Range(0, 200).Select(_ => 0).ToArray();
            var a = new Random(9);
            var b = new Random(9);
            for (int i = 0; i < first.Length; i++)
                Assert.That(CraftRules.RollQuality(480, 500, a), Is.EqualTo(CraftRules.RollQuality(480, 500, b)));
        }

        [Test]
        public void SecondaryMinimumLevel_MatchesEveryProfession_ForEveryObjectType()
        {
            (eCraftingSkill Skill, AbstractCraftingSkill Craft)[] crafts =
            [
                (eCraftingSkill.ArmorCrafting, Craft<ArmorCrafting>(eCraftingSkill.ArmorCrafting)),
                (eCraftingSkill.WeaponCrafting, Craft<WeaponCrafting>(eCraftingSkill.WeaponCrafting)),
                (eCraftingSkill.Tailoring, Craft<Tailoring>(eCraftingSkill.Tailoring)),
                (eCraftingSkill.Fletching, Craft<Fletching>(eCraftingSkill.Fletching)),
                (eCraftingSkill.Alchemy, Craft<Alchemy>(eCraftingSkill.Alchemy)),
                (eCraftingSkill.SpellCrafting, Craft<SpellCrafting>(eCraftingSkill.SpellCrafting)),
                (eCraftingSkill.SiegeCrafting, Craft<SiegeCrafting>(eCraftingSkill.SiegeCrafting)),
            ];
            foreach (eObjectType type in Enum.GetValues<eObjectType>())
                foreach (int level in new[] { 1, 15, 60, 300, 755, 1000 })
                {
                    Recipe recipe = RecipeOf(level, [5], (int)type);
                    foreach ((eCraftingSkill skill, AbstractCraftingSkill craft) in crafts)
                        Assert.That(CraftRules.SecondaryMinimumLevel(skill, (int)type, level),
                            Is.EqualTo(craft.GetSecondaryCraftingSkillMinimumLevel(recipe)), $"{skill} {type} level {level}");
                }
        }

        [Test]
        public void SecondarySkillForMaterial_FollowsTheMaterialModels()
        {
            Assert.That(CraftRules.SecondarySkillForMaterial(522), Is.EqualTo(eCraftingSkill.ClothWorking));
            Assert.That(CraftRules.SecondarySkillForMaterial(537), Is.EqualTo(eCraftingSkill.ClothWorking));
            Assert.That(CraftRules.SecondarySkillForMaterial(521), Is.EqualTo(eCraftingSkill.LeatherCrafting));
            Assert.That(CraftRules.SecondarySkillForMaterial(519), Is.EqualTo(eCraftingSkill.MetalWorking));
            Assert.That(CraftRules.SecondarySkillForMaterial(520), Is.EqualTo(eCraftingSkill.WoodWorking));
            Assert.That(CraftRules.SecondarySkillForMaterial(1), Is.EqualTo(eCraftingSkill.NoCrafting));
            Assert.That(CraftRules.SubSkillCap, Is.EqualTo(AbstractCraftingSkill.subSkillCap));
        }

        // ---- Spellcraft ----

        [Test]
        public void MaxImbuePoints_MatchesGetItemMaxImbuePoints_AndTheDocumentedFigures()
        {
            OpenSpellCrafting spellcraft = Spellcraft();
            for (int level = 0; level <= 60; level++)
                for (int quality = 80; quality <= 101; quality++)
                {
                    DbInventoryItem item = ItemOf(level, quality);
                    Assert.That(SpellcraftRules.MaxImbuePoints(level, quality), Is.EqualTo(spellcraft.MaxImbue(item)), $"level {level} quality {quality}");
                }
            // docs/CRAFTING-AND-ECONOMY.md 1: a 100% level-51 piece holds 32 points, 99% 28, 96% 18.
            Assert.That(SpellcraftRules.MaxImbuePoints(51, 100), Is.EqualTo(32));
            Assert.That(SpellcraftRules.MaxImbuePoints(51, 99), Is.EqualTo(28));
            Assert.That(SpellcraftRules.MaxImbuePoints(51, 96), Is.EqualTo(18));
        }

        [Test]
        public void GemImbuePoints_MatchesGetGemImbuePoints()
        {
            OpenSpellCrafting spellcraft = Spellcraft();
            for (int type = 0; type <= (int)eProperty.Skill_Last + 20; type++)
                for (int value = -2; value <= 40; value++)
                    Assert.That(SpellcraftRules.GemImbuePoints(type, value), Is.EqualTo(spellcraft.Gem(type, value)), $"type {type} value {value}");
        }

        [Test]
        public void ChanceToOvercharge_MatchesCalculateChanceToOverchargeItem()
        {
            OpenSpellCrafting spellcraft = Spellcraft();
            CraftPlayer player = Player();
            for (int skill = 0; skill <= 1150; skill += 25)
            {
                player.Skills[eCraftingSkill.SpellCrafting] = skill;
                for (int quality = 90; quality <= 102; quality++)
                    foreach (int max in new[] { 0, 5, 18, 28, 32 })
                        for (int bonus = max - 2; bonus <= max + 7; bonus++)
                            Assert.That(SpellcraftRules.ChanceToOvercharge(quality, skill, max, bonus),
                                Is.EqualTo(spellcraft.Overcharge(player, ItemOf(51, quality), max, bonus)),
                                $"skill {skill} quality {quality} max {max} bonus {bonus}");
            }
            for (int max = 0; max < 10; max++)
                for (int bonus = max; bonus <= max + 7; bonus++)
                    Assert.That(SpellcraftRules.OverchargePenalty(max, bonus), Is.EqualTo(spellcraft.Penalty(max, bonus)));
        }

        [Test]
        public void TotalImbueAndPreserve_MatchTheOriginals_WithGemsInTheTradeWindow()
        {
            OpenSpellCrafting spellcraft = Spellcraft();
            CraftPlayer player = Player();
            ITradeWindow window = DispatchProxy.Create<ITradeWindow, TradeWindowProxy>();
            TradeWindowProxy proxy = (TradeWindowProxy)(object)window;
            player.TradeWindow = window;
            var random = new Random(31);
            int[] types = [(int)eProperty.Strength, (int)eProperty.MaxMana, (int)eProperty.MaxHealth, (int)eProperty.Resist_Heat,
                (int)eProperty.Skill_Slashing, (int)eProperty.Skill_Last + 3, 0];
            for (int round = 0; round < 400; round++)
            {
                DbInventoryItem item = ItemOf(51, 94 + random.Next(7));
                item.Bonus1Type = types[random.Next(types.Length)];
                item.Bonus1 = random.Next(1, 30);
                item.Bonus2Type = types[random.Next(types.Length)];
                item.Bonus2 = random.Next(1, 30);
                item.Bonus3Type = types[random.Next(types.Length)];
                item.Bonus3 = random.Next(1, 30);
                proxy.Items.Clear();
                int gems = random.Next(5);
                for (int g = 0; g < gems; g++)
                {
                    DbInventoryItem gem = ItemOf(1, 94 + random.Next(7));
                    gem.Bonus1Type = types[random.Next(types.Length - 1)];
                    gem.Bonus1 = random.Next(1, 30);
                    proxy.Items.Add(gem);
                }
                int skill = random.Next(0, 1101);
                player.Skills[eCraftingSkill.SpellCrafting] = skill;
                var existing = new[] { (item.Bonus1Type, item.Bonus1), (item.Bonus2Type, item.Bonus2), (item.Bonus3Type, item.Bonus3) };
                var gemBonuses = proxy.Items.Cast<DbInventoryItem>().Select(gem => (gem.Bonus1Type, gem.Bonus1)).ToArray();
                Assert.That(SpellcraftRules.TotalImbuePoints(existing, gemBonuses), Is.EqualTo(spellcraft.Total(player, item)), $"round {round}");
                Assert.That(SpellcraftRules.ChanceToPreserve(item.Quality, proxy.Items.Cast<DbInventoryItem>().Select(gem => gem.Quality), skill),
                    Is.EqualTo(spellcraft.Preserve(player, item, 20, 24)), $"round {round}");
            }
        }

        // ---- helpers ----

        private static T Craft<T>(eCraftingSkill skill) where T : AbstractCraftingSkill
        {
            T craft = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(AbstractCraftingSkill).GetField("m_eskill", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(craft, skill);
            return craft;
        }

        private static OpenSpellCrafting Spellcraft() => Craft<OpenSpellCrafting>(eCraftingSkill.SpellCrafting);

        private static CraftPlayer Player(bool capital = false, ePrivLevel privLevel = ePrivLevel.Player)
        {
            CraftPlayer player = (CraftPlayer)RuntimeHelpers.GetUninitializedObject(typeof(CraftPlayer));
            typeof(CraftPlayer).GetField(nameof(CraftPlayer.Skills)).SetValue(player, new Dictionary<eCraftingSkill, int>());
            player.TestSpeed = 1.0;
            CapitalRegion region = (CapitalRegion)RuntimeHelpers.GetUninitializedObject(typeof(CapitalRegion));
            region.Capital = capital;
            player.TestRegion = region;
            player.TestClient = new GameClient((Socket)null) { Account = new DbAccount { PrivLevel = (uint)privLevel } };
            return player;
        }

        private static Recipe RecipeOf(int level, int[] counts, int objectType = (int)eObjectType.Plate)
        {
            DbItemTemplate product = new() { Id_nb = "parity_product", Name = "parity product", Object_Type = objectType, Level = 10 };
            List<Ingredient> ingredients = counts.Select((count, index) =>
                new Ingredient(count, new DbItemTemplate { Id_nb = $"m{index}", Name = $"material {index}", Model = 519, Price = 10 })).ToList();
            return new Recipe(product, ingredients, eCraftingSkill.WeaponCrafting, level, false);
        }

        private static DbInventoryItem ItemOf(int level, int quality)
        {
            DbItemTemplate template = new() { Id_nb = Guid.NewGuid().ToString(), Name = "parity item", Level = level, Quality = quality };
            DbInventoryItem item = GameInventoryItem.Create(template);
            item.Level = level;
            item.Quality = quality;
            return item;
        }
    }
}
