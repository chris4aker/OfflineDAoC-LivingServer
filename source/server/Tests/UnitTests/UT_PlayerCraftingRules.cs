using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.GS;
using DOL.GS.Crafting;
using DOL.GS.CraftingEconomy;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// Crafting 2.0 WP3 (the player's side, M1): era start never strips an existing crafting row and starts only new rows
    /// without an order; the six era orders are open and Basic Crafting and Siegecraft are not; secondary caps follow the
    /// Masters' own words (75% / 40%) and never lower a skill; coin by level band follows the player's XP rate, never
    /// lowers coin and never touches gamebots; the smith's prices by upkeep mode; the ported title changes (upstream 7b0df1b12,
    /// ad789ee4d); and every era order resolves to a working Master by the world's guild names.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_PlayerCraftingRules
    {
        private string _upkeep;
        private bool _coinByLevel, _eraStart, _caps;

        private GameServer _previousServer;

        // GameObject's type initializer reads the database (the data quest cache): an empty read-only double, as
        // UT_AutonomousLootFlow does.
        private sealed class CraftServer : GameServer
        {
            protected override IObjectDatabase DataBaseImpl => EmptyDatabase;
        }

        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, EmptyReadDatabase>();

        public class EmptyReadDatabase : DispatchProxy
        {
            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("Crafting rules test attempted a database mutation: " + method.Name);
                Type result = method.ReturnType;
                if (result.IsGenericType && typeof(IEnumerable).IsAssignableFrom(result))
                    return Activator.CreateInstance(typeof(List<>).MakeGenericType(result.GetGenericArguments()[0]));
                return null;
            }
        }

        [SetUp]
        public void Save()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((CraftServer) RuntimeHelpers.GetUninitializedObject(typeof(CraftServer)));
            _upkeep = CraftingEconomyProperties.PLAYER_UPKEEP;
            _coinByLevel = CraftingEconomyProperties.PLAYER_COIN_BY_LEVEL;
            _eraStart = CraftingEconomyProperties.CRAFTING_ERA_START;
            _caps = CraftingEconomyProperties.CRAFTING_SECONDARY_CAPS;
        }

        [TearDown]
        public void Restore()
        {
            GameServer.LoadTestDouble(_previousServer);
            CraftingEconomyProperties.PLAYER_UPKEEP = _upkeep;
            CraftingEconomyProperties.PLAYER_COIN_BY_LEVEL = _coinByLevel;
            CraftingEconomyProperties.CRAFTING_ERA_START = _eraStart;
            CraftingEconomyProperties.CRAFTING_SECONDARY_CAPS = _caps;
        }

        // ---- Era start ----

        [Test]
        public void TheDefaultsAreThePlansDefaults()
        {
            var fresh = typeof(CraftingEconomyProperties).GetFields()
                .Select(f => (f, a: (DOL.GS.ServerProperties.ServerPropertyAttribute) Attribute.GetCustomAttribute(f, typeof(DOL.GS.ServerProperties.ServerPropertyAttribute))))
                .Where(x => x.a != null).ToDictionary(x => x.a.Key, x => x.a.DefaultValue);
            Assert.That(fresh["crafting_era_start"], Is.EqualTo(true));
            Assert.That(fresh["crafting_secondary_caps"], Is.EqualTo(false));
            Assert.That(fresh["player_upkeep"], Is.EqualTo("light"));
            Assert.That(fresh["player_coin_by_level"], Is.EqualTo(true));
        }

        [Test]
        public void ANewRowStartsWithoutAnOrderOnlyWithEraStart()
        {
            Assert.That(PlayerCraftingRules.NewRowValues(true), Is.EqualTo((eCraftingSkill.NoCrafting, string.Empty)));
            (eCraftingSkill primary, string skills) = PlayerCraftingRules.NewRowValues(false);
            Assert.That(primary, Is.EqualTo(eCraftingSkill.BasicCrafting), "off: the pre-1.2 start");
            Assert.That(skills, Is.EqualTo(new DbAccountXCrafting().SerializedCraftingSkills));
            Assert.That(skills.Split(';'), Has.Length.EqualTo(14).And.All.EndsWith("|1"));

            DbAccountXCrafting era = PlayerCraftingRules.NewAccountRealmRow("acct", 2, true);
            Assert.That((era.AccountId, era.Realm, era.CraftingPrimarySkill, era.SerializedCraftingSkills), Is.EqualTo(("acct", 2, 0, "")));
            DbAccountXCrafting classic = PlayerCraftingRules.NewAccountRealmRow("acct", 3, false);
            Assert.That((classic.Realm, classic.CraftingPrimarySkill), Is.EqualTo((3, (int) eCraftingSkill.BasicCrafting)));
        }

        [Test]
        public void ALoadedRowKeepsItsOrderAndSkills()
        {
            // An existing 1.1.4 row: Basic Crafting or any order, kept exactly whatever the setting.
            foreach (eCraftingSkill stored in new[] { eCraftingSkill.BasicCrafting, eCraftingSkill.Tailoring, eCraftingSkill.SiegeCrafting })
                Assert.That(PlayerCraftingRules.LoadedPrimary(stored, 14), Is.EqualTo(stored));
            Assert.That(PlayerCraftingRules.LoadedPrimary(eCraftingSkill.NoCrafting, 0), Is.EqualTo(eCraftingSkill.NoCrafting), "an era-start row");
            // Skills without a primary would be saved as no skills (CraftingProgressMgr), so such a row plays as Basic Crafting.
            Assert.That(PlayerCraftingRules.LoadedPrimary(eCraftingSkill.NoCrafting, 3), Is.EqualTo(eCraftingSkill.BasicCrafting));
        }

        [Test]
        public void AddingASkillPicksAnEraOrderNotBasicCrafting()
        {
            Assert.That(PlayerCraftingRules.PrimaryAfterAdding(eCraftingSkill.NoCrafting, eCraftingSkill.Alchemy, true), Is.EqualTo(eCraftingSkill.Alchemy));
            Assert.That(PlayerCraftingRules.PrimaryAfterAdding(eCraftingSkill.NoCrafting, eCraftingSkill.MetalWorking, true), Is.EqualTo(eCraftingSkill.BasicCrafting),
                "a material skill alone still needs a primary, or the save drops it");
            Assert.That(PlayerCraftingRules.PrimaryAfterAdding(eCraftingSkill.NoCrafting, eCraftingSkill.Alchemy, false), Is.EqualTo(eCraftingSkill.BasicCrafting));
            Assert.That(PlayerCraftingRules.PrimaryAfterAdding(eCraftingSkill.Tailoring, eCraftingSkill.Alchemy, true), Is.EqualTo(eCraftingSkill.Tailoring));
        }

        [Test]
        public void SixOrdersAreOpenWithEraStartAndEveryOrderWithout()
        {
            Assert.That(PlayerCraftingRules.EraOrders, Is.EquivalentTo(new[]
            {
                eCraftingSkill.WeaponCrafting, eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring,
                eCraftingSkill.Fletching, eCraftingSkill.Alchemy, eCraftingSkill.SpellCrafting,
            }));
            Assert.That(PlayerCraftingRules.IsOpenOrder(eCraftingSkill.BasicCrafting, true), Is.False);
            Assert.That(PlayerCraftingRules.IsOpenOrder(eCraftingSkill.SiegeCrafting, true), Is.False);
            foreach (eCraftingSkill order in PlayerCraftingRules.EraOrders)
                Assert.That(PlayerCraftingRules.IsOpenOrder(order, true), Is.True, order.ToString());
            Assert.That(PlayerCraftingRules.IsOpenOrder(eCraftingSkill.BasicCrafting, false), Is.True);
            Assert.That(PlayerCraftingRules.IsOpenOrder(eCraftingSkill.SiegeCrafting, false), Is.True);
            Assert.That(PlayerCraftingRules.IsTrained(-1), Is.False, "GetCraftingSkillValue of an untrained skill");
            Assert.That(PlayerCraftingRules.IsTrained(1), Is.True);
        }

        [Test]
        public void EveryEraOrderHasAWorkingMasterByTheWorldsGuildNames()
        {
            // The world's Mob rows name masters by Guild; with use_npcguildscripts on (the default, and the shipped
            // world's value) Region.LoadFromDatabase makes a row with one of these guild names that master, whatever its
            // ClassType. M0's 57 "decoration" masters (ClassType DOL.GS.GameNPC) are 56 of these plus one Siegecrafters
            // Master in Domnann, which no class claims.
            (string Guild, Type Master, eCraftingSkill Order)[] expected =
            [
                ("Alchemists Master", typeof(AlchemistsMaster), eCraftingSkill.Alchemy),
                ("Armorsmiths Master", typeof(ArmorCraftingMaster), eCraftingSkill.ArmorCrafting),
                ("Fletchers Master", typeof(FletchingMaster), eCraftingSkill.Fletching),
                ("Spellcrafters Master", typeof(SpellCraftingMaster), eCraftingSkill.SpellCrafting),
                ("Tailors Master", typeof(TailoringMaster), eCraftingSkill.Tailoring),
                ("Weaponsmiths Master", typeof(WeaponCraftingMaster), eCraftingSkill.WeaponCrafting),
            ];
            foreach (eRealm realm in new[] { eRealm.Albion, eRealm.Midgard, eRealm.Hibernia })
                foreach (var (guild, master, order) in expected)
                {
                    Assert.That(ScriptMgr.FindNPCGuildScriptClass(guild, realm), Is.EqualTo(master), $"{guild} in {realm}");
                    var npc = (CraftNPC) RuntimeHelpers.GetUninitializedObject(master);
                    Assert.That(npc.TheCraftingSkill, Is.EqualTo(order));
                    Assert.That(npc.TrainedSkills, Does.Contain(order));
                }
            Assert.That(PlayerCraftingRules.EraOrders.Length, Is.EqualTo(expected.Length));
            Assert.That(ScriptMgr.FindNPCGuildScriptClass("Siegecrafters Master", eRealm.Hibernia), Is.Null, "Domnann's siegecraft master stays a decoration");
        }

        [Test]
        public void ClosedOrdersPointTheWayAndOpenOrdersAdmit()
        {
            CraftingEconomyProperties.CRAFTING_ERA_START = true;
            var basic = (CraftNPC) RuntimeHelpers.GetUninitializedObject(typeof(BasicCraftingMaster));
            var siege = (CraftNPC) RuntimeHelpers.GetUninitializedObject(typeof(SiegecraftingMaster));
            var tailor = (CraftNPC) RuntimeHelpers.GetUninitializedObject(typeof(TailoringMaster));
            Assert.That((basic.AdmitsNewMembers, siege.AdmitsNewMembers, tailor.AdmitsNewMembers), Is.EqualTo((false, false, true)));
            Assert.That(basic.ClosedOrderSentence, Does.Contain("Tailors").And.Contain("Spellcrafters"));
            CraftingEconomyProperties.CRAFTING_ERA_START = false;
            Assert.That((basic.AdmitsNewMembers, siege.AdmitsNewMembers), Is.EqualTo((true, true)));
        }

        // ---- Secondary caps ----

        [Test]
        public void SecondaryCapsFollowTheMastersWords()
        {
            // "If a tailor, fletching can only be raised to 75% of your tailoring skill, and all other trade skills 40%".
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Tailoring, eCraftingSkill.Fletching), Is.EqualTo(75));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Tailoring, eCraftingSkill.WeaponCrafting), Is.EqualTo(40));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Tailoring, eCraftingSkill.ArmorCrafting), Is.EqualTo(40));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Tailoring, eCraftingSkill.SiegeCrafting), Is.EqualTo(40), "a small bit");
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.ArmorCrafting, eCraftingSkill.Tailoring), Is.EqualTo(75));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.ArmorCrafting, eCraftingSkill.WeaponCrafting), Is.EqualTo(75));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.ArmorCrafting, eCraftingSkill.Fletching), Is.EqualTo(40));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.WeaponCrafting, eCraftingSkill.ArmorCrafting), Is.EqualTo(75));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.WeaponCrafting, eCraftingSkill.SiegeCrafting), Is.EqualTo(75), "a great bit");
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.WeaponCrafting, eCraftingSkill.Fletching), Is.EqualTo(40));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Fletching, eCraftingSkill.WeaponCrafting), Is.EqualTo(75));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Fletching, eCraftingSkill.Tailoring), Is.EqualTo(40));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Alchemy, eCraftingSkill.SpellCrafting), Is.EqualTo(75));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.SpellCrafting, eCraftingSkill.Alchemy), Is.EqualTo(75));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.SpellCrafting, eCraftingSkill.SiegeCrafting), Is.EqualTo(40));
            // Never capped: the primary, the material skills, and anyone whose primary is not an era order.
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.Tailoring, eCraftingSkill.Tailoring), Is.EqualTo(100));
            foreach (eCraftingSkill material in new[] { eCraftingSkill.MetalWorking, eCraftingSkill.LeatherCrafting, eCraftingSkill.ClothWorking,
                         eCraftingSkill.WoodWorking, eCraftingSkill.GemCutting, eCraftingSkill.HerbalCrafting })
                Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.ArmorCrafting, material), Is.EqualTo(100), material.ToString());
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.BasicCrafting, eCraftingSkill.Fletching), Is.EqualTo(100));
            Assert.That(PlayerCraftingRules.SecondaryCapPercent(eCraftingSkill.NoCrafting, eCraftingSkill.Fletching), Is.EqualTo(100));
        }

        [Test]
        public void ACapStopsGainsButNeverLowersASkill()
        {
            Assert.That(PlayerCraftingRules.SkillCap(eCraftingSkill.Tailoring, 400, eCraftingSkill.Fletching, capsOn: false), Is.EqualTo(int.MaxValue));
            Assert.That(PlayerCraftingRules.SkillCap(eCraftingSkill.Tailoring, 400, eCraftingSkill.Fletching, capsOn: true), Is.EqualTo(300));
            Assert.That(PlayerCraftingRules.SkillCap(eCraftingSkill.Tailoring, 400, eCraftingSkill.WeaponCrafting, capsOn: true), Is.EqualTo(160));
            Assert.That(PlayerCraftingRules.SkillCap(eCraftingSkill.Tailoring, 1, eCraftingSkill.WeaponCrafting, capsOn: true), Is.EqualTo(1), "a new member keeps the 1 the Master gave");
            Assert.That(PlayerCraftingRules.SkillCap(eCraftingSkill.Tailoring, 400, eCraftingSkill.MetalWorking, capsOn: true), Is.EqualTo(int.MaxValue));

            Assert.That(PlayerCraftingRules.PointsAllowed(100, 1, int.MaxValue), Is.EqualTo(1));
            Assert.That(PlayerCraftingRules.PointsAllowed(159, 3, 160), Is.EqualTo(1), "a pace of 3 stops at the cap");
            Assert.That(PlayerCraftingRules.PointsAllowed(160, 1, 160), Is.EqualTo(0));
            Assert.That(PlayerCraftingRules.PointsAllowed(700, 1, 160), Is.EqualTo(0), "trained before the caps: kept, not cut");
            Assert.That(PlayerCraftingRules.PointsAllowed(10, 0, 160), Is.EqualTo(0));
            Assert.That(PlayerCraftingRules.PointsAllowed(10, 5, int.MaxValue), Is.EqualTo(5));
        }

        // ---- Coin by level band ----

        [Test]
        public void CoinFollowsTheXpRateAndNeverDrops()
        {
            Assert.That(PlayerCraftingRules.CoinMultiplier(false, 12), Is.EqualTo(1.0));
            Assert.That(PlayerCraftingRules.CoinMultiplier(true, 12), Is.EqualTo(12.0));
            Assert.That(PlayerCraftingRules.CoinMultiplier(true, 1), Is.EqualTo(1.0));
            Assert.That(PlayerCraftingRules.CoinMultiplier(true, 0.5), Is.EqualTo(1.0), "a slow XP rate never lowers coin");
            Assert.That(PlayerCraftingRules.CoinMultiplier(true, double.NaN), Is.EqualTo(1.0));
            Assert.That(PlayerCraftingRules.CoinMultiplier(true, 5000), Is.EqualTo(LevelRangeXpRates.MaximumRate));
            Assert.That(PlayerCraftingRules.ScaleCoin(2733, 12), Is.EqualTo(32796));
            Assert.That(PlayerCraftingRules.ScaleCoin(10, 2.55), Is.EqualTo(25), "rounded down");
            Assert.That(PlayerCraftingRules.ScaleCoin(10, 1), Is.EqualTo(10));
            Assert.That(PlayerCraftingRules.ScaleCoin(0, 12), Is.EqualTo(0));
            Assert.That(PlayerCraftingRules.ScaleCoin(long.MaxValue / 2, 1000), Is.EqualTo(long.MaxValue));
        }

        [Test]
        public void OnlyARealPlayersKillIsScaled()
        {
            var player = (GamePlayer) RuntimeHelpers.GetUninitializedObject(typeof(GamePlayer));
            var gamebot = Npc<GameBot>();
            var companion = Npc<GameBot>();
            typeof(GameBot).GetProperty(nameof(GameBot.IsTemporaryGroupHelper))!.SetValue(companion, true);
            var monster = Npc<GameNPC>();
            Assert.That(PlayerCraftingRules.CoinBeneficiary(player), Is.SameAs(player));
            Assert.That(PlayerCraftingRules.CoinBeneficiary(gamebot), Is.Null, "a gamebot's coin is unchanged");
            Assert.That(PlayerCraftingRules.CoinBeneficiary(monster), Is.Null);
            Assert.That(PlayerCraftingRules.CoinBeneficiary(companion), Is.Null, "a companion outside a player's group");
            Assert.That(PlayerCraftingRules.CoinBeneficiary(null), Is.Null);

            CraftingEconomyProperties.PLAYER_COIN_BY_LEVEL = true;
            Assert.That(PlayerCraftingRules.CoinForKiller(500, gamebot), Is.EqualTo(500));
            Assert.That(PlayerCraftingRules.CoinForKiller(500, null), Is.EqualTo(500));
            CraftingEconomyProperties.PLAYER_COIN_BY_LEVEL = false;
            Assert.That(PlayerCraftingRules.CoinForKiller(500, player), Is.EqualTo(500), "off: classic coin");
        }

        private static T Npc<T>() where T : GameNPC
        {
            var npc = (T) RuntimeHelpers.GetUninitializedObject(typeof(T));
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(npc, new ArrayList());
            return npc;
        }

        // ---- Upkeep ----

        [Test]
        public void TheSmithChargesByTheUpkeepSetting()
        {
            CraftingEconomyProperties.PLAYER_UPKEEP = "classic";
            Assert.That(PlayerCraftingRules.SmithCost(1001), Is.EqualTo(1001));
            CraftingEconomyProperties.PLAYER_UPKEEP = "light";
            Assert.That(PlayerCraftingRules.SmithCost(1001), Is.EqualTo(501));
            Assert.That(PlayerCraftingRules.SmithCost(1), Is.EqualTo(1));
            CraftingEconomyProperties.PLAYER_UPKEEP = "free";
            Assert.That(PlayerCraftingRules.SmithCost(1001), Is.EqualTo(0));
            CraftingEconomyProperties.PLAYER_UPKEEP = "nonsense";
            Assert.That(PlayerCraftingRules.SmithCost(1001), Is.EqualTo(501), "anything unknown is light");
        }

        // ---- Titles (upstream 7b0df1b12, ad789ee4d) ----

        [Test]
        public void NoLegendaryGrandmasterAndNoBasicCraftingTitle()
        {
            Assert.That(AbstractProfession.GetTitleFormat(0), Is.EqualTo("CraftersTitle.Helper"));
            Assert.That(AbstractProfession.GetTitleFormat(999), Is.EqualTo("CraftersTitle.Grandmaster"));
            Assert.That(AbstractProfession.GetTitleFormat(1000), Is.EqualTo("CraftersTitle.Legendary"));
            Assert.That(AbstractProfession.GetTitleFormat(1100), Is.EqualTo("CraftersTitle.Legendary"), "the post-era rung is gone");
            Assert.That(AbstractProfession.GetTitleFormat(1300), Is.EqualTo("CraftersTitle.Legendary"));
            Assert.Throws<ArgumentOutOfRangeException>(() => AbstractProfession.GetTitleFormat(-1));

            using (new PetTestLanguageScope())
                Assert.That(new BasicCrafting().GetTitle(null, 500), Is.Empty);

            string dir = TestContext.CurrentContext.TestDirectory;
            while (dir != null && !File.Exists(Path.Combine(dir, "source/server/GameServer/language/EN/Crafting.txt")))
                dir = Path.GetDirectoryName(dir);
            if (dir == null)
                Assert.Ignore("Server source not found beside the tests.");
            foreach (string language in new[] { "EN", "DE", "FR" })
                Assert.That(File.ReadAllLines(Path.Combine(dir, $"source/server/GameServer/language/{language}/Crafting.txt")),
                    Has.None.StartsWith("CraftersTitle.LegendaryGrandmaster"), language);
        }
    }
}
