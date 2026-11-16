using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using DOL.Events;
using DOL.GS;
using DOL.GS.CraftingEconomy;
using DOL.GS.PacketHandler;
using DOL.GS.ServerProperties;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>The launcher's Crafting skill rate option (owner 2026-11-12): a skill gain gives the rate in points.
    /// Ported with CraftingRate165 from shadowofze's OfflineDAoC-True165 (00f9fbf), GPL-3.0, for Crafting 2.0 WP1; the
    /// rate is read from CraftingEconomyProperties (same key, crafting_skill_rate).</summary>
    [TestFixture, NonParallelizable]
    public sealed class UT_CraftingRate165
    {
        // a player whose skills, region and gains are kept by the test (made with GetUninitializedObject)
        private sealed class GainPlayer : GamePlayer
        {
            public GainPlayer() : base(null, null) { }
            public Dictionary<eCraftingSkill, int> Skills;
            public Region Region;
            public IPacketLib Packets;
            public override int GetCraftingSkillValue(eCraftingSkill skill) => Skills.TryGetValue(skill, out int v) ? v : -1;
            public override bool GainCraftingSkill(eCraftingSkill skill, int count)
            {
                Skills[skill] = GetCraftingSkillValue(skill) + count;
                return true;
            }
            public override Region CurrentRegion { get => Region; }
            public override int CraftingSkillBonus => 100;    // with a capital city: every non-grey craft gains
            public override IPacketLib Out => Packets;
        }

        private sealed class CapitalRegion : Region
        {
            public CapitalRegion() : base(null) { }
            public override bool IsCapitalCity => true;
        }

        private static readonly IObjectDatabase Empty = DispatchProxy.Create<IObjectDatabase, UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => Empty; }
        private GameServer _previous;
        private double _rate;

        [SetUp]
        public void Setup()
        {
            _previous = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
            _rate = CraftingEconomyProperties.CRAFTING_SKILL_RATE;
        }

        [TearDown]
        public void Cleanup()
        {
            GameServer.LoadTestDouble(_previous);
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = _rate;
        }

        // ---- the arithmetic

        [Test]
        public void RateOneIsTheOriginalSinglePoint()
        {
            Assert.That(CraftingRate165.Points(500, 1), Is.EqualTo(1));
            Assert.That(CraftingRate165.Points(1299, 1, AbstractCraftingSkill.subSkillCap), Is.EqualTo(1));
            Assert.That(CraftingRate165.Points(1300, 1, AbstractCraftingSkill.subSkillCap), Is.EqualTo(0));
            Assert.That(CraftingRate165.Points(-1, 1), Is.EqualTo(1), "a skill the player lacks: as before");
        }

        [TestCase(2)]
        [TestCase(3)]
        [TestCase(5)]
        [TestCase(10)]
        public void EveryLauncherPresetGivesThatManyPoints(int rate)
        {
            Assert.That(CraftingRate165.Points(500, rate), Is.EqualTo(rate));
            Assert.That(CraftingRate165.Points(500, rate, AbstractCraftingSkill.subSkillCap), Is.EqualTo(rate));
        }

        [Test]
        public void NeverPastTheCap()
        {
            Assert.That(CraftingRate165.Points(1298, 5, AbstractCraftingSkill.subSkillCap), Is.EqualTo(2));
            Assert.That(CraftingRate165.Points(1300, 5, AbstractCraftingSkill.subSkillCap), Is.EqualTo(0));
            Assert.That(CraftingRate165.Points(1350, 10, AbstractCraftingSkill.subSkillCap), Is.EqualTo(0));
            Assert.That(CraftingRate165.Points(int.MaxValue - 2, 10), Is.EqualTo(2), "no overflow without a cap");
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        [TestCase(-2.0)]
        [TestCase(0.0)]
        [TestCase(0.5)]
        public void BadStoredRateCountsAsOne(double bad)
        {
            Assert.That(CraftingRate165.Sanitize(bad), Is.EqualTo(1));
            Assert.That(CraftingRate165.Points(500, bad), Is.EqualTo(1));
        }

        [Test]
        public void WholePointsOnlyAndAHugeRateIsHeld()
        {
            Assert.That(CraftingRate165.Sanitize(2.9), Is.EqualTo(2));
            Assert.That(CraftingRate165.Sanitize(1e9), Is.EqualTo(CraftingRate165.MaxRate));
        }

        [Test]
        public void PlayerRateReadsTheProperty()
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 1;
            Assert.That(CraftingRate165.PlayerRate, Is.EqualTo(1));
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 5;
            Assert.That(CraftingRate165.PlayerRate, Is.EqualTo(5));
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 0;
            Assert.That(CraftingRate165.PlayerRate, Is.EqualTo(1));
        }

        [Test]
        public void TitlesAreNotSteppedOver()
        {
            // one point: exactly the old "new value is a multiple of 100, up to 1000" check
            for (int v = 0; v <= 1100; v++)
            {
                List<int> one = CraftingRate165.TitlesReached(v - 1, v);
                bool old = v <= 1000 && v % 100 == 0;
                Assert.That(one.Count == 1 && one[0] == v, Is.EqualTo(old), $"value {v}");
                Assert.That(one.Count, Is.LessThanOrEqualTo(1));
            }
            Assert.That(CraftingRate165.TitlesReached(98, 103), Is.EqualTo(new[] { 100 }));
            Assert.That(CraftingRate165.TitlesReached(100, 105), Is.Empty, "already past 100");
            Assert.That(CraftingRate165.TitlesReached(995, 1005), Is.EqualTo(new[] { 1000 }));
            Assert.That(CraftingRate165.TitlesReached(1000, 1010), Is.Empty, "no titles after 1000");
        }

        // ---- the real gain code (a capital city with a +100 bonus, so every non-grey craft gains)

        private static GainPlayer Player(Dictionary<eCraftingSkill, int> skills)
        {
            var player = (GainPlayer)RuntimeHelpers.GetUninitializedObject(typeof(GainPlayer));
            player.Skills = skills;
            player.Region = (CapitalRegion)RuntimeHelpers.GetUninitializedObject(typeof(CapitalRegion));
            player.Packets = DispatchProxy.Create<IPacketLib, UT_RealmExchangeEconomy.NoOpPacketLib>();
            return player;
        }

        private static T Trade<T>(eCraftingSkill id) where T : AbstractCraftingSkill
        {
            var skill = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            skill.eSkill = id;
            return skill;
        }

        private static DbItemTemplate Material(string name, int model) =>
            new DbItemTemplate { Id_nb = name.Replace(' ', '_'), Name = name, Model = model, Price = 10, PackSize = 1 };

        private static Recipe Robe(int level) => new Recipe(
            new DbItemTemplate { Id_nb = "robe" + level, Name = "robe", Object_Type = (int)eObjectType.Cloth, Price = 100, PackSize = 1 },
            new List<Ingredient> { new(20, Material("cloth squares", 522)), new(10, Material("heavy thread", 537)), new(5, Material("leather squares", 521)) },
            eCraftingSkill.Tailoring, level, true);

        [Test]
        public void TailoringGainAtRateOneIsUnchanged()
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 1;
            GainPlayer player = Player(new() { [eCraftingSkill.Tailoring] = 300, [eCraftingSkill.ClothWorking] = 300, [eCraftingSkill.LeatherCrafting] = 300 });
            Trade<Tailoring>(eCraftingSkill.Tailoring).GainCraftingSkillPoints(player, Robe(300));
            Assert.That(player.Skills[eCraftingSkill.Tailoring], Is.EqualTo(301));
            Assert.That(player.Skills[eCraftingSkill.ClothWorking], Is.EqualTo(302), "one point per cloth / thread entry");
            Assert.That(player.Skills[eCraftingSkill.LeatherCrafting], Is.EqualTo(301));
        }

        [Test]
        public void TailoringGainAtRateFiveGivesFive()
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 5;
            GainPlayer player = Player(new() { [eCraftingSkill.Tailoring] = 300, [eCraftingSkill.ClothWorking] = 300, [eCraftingSkill.LeatherCrafting] = 1298 });
            Trade<Tailoring>(eCraftingSkill.Tailoring).GainCraftingSkillPoints(player, Robe(300));
            Assert.That(player.Skills[eCraftingSkill.Tailoring], Is.EqualTo(305));
            Assert.That(player.Skills[eCraftingSkill.ClothWorking], Is.EqualTo(310), "five per cloth / thread entry");
            Assert.That(player.Skills[eCraftingSkill.LeatherCrafting], Is.EqualTo(1300), "secondaries stop at 1300");
        }

        [Test]
        public void GreyRecipeStillGivesNothing()
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 10;
            GainPlayer player = Player(new() { [eCraftingSkill.Tailoring] = 400, [eCraftingSkill.ClothWorking] = 400, [eCraftingSkill.LeatherCrafting] = 400 });
            Trade<Tailoring>(eCraftingSkill.Tailoring).GainCraftingSkillPoints(player, Robe(350));
            Assert.That(player.Skills[eCraftingSkill.Tailoring], Is.EqualTo(400));
            Assert.That(player.Skills[eCraftingSkill.ClothWorking], Is.EqualTo(400));
        }

        [Test]
        public void SecondaryTradeStopsAtItsCap()
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 10;
            var bars = new Recipe(new DbItemTemplate { Id_nb = "bars", Name = "bars", Price = 10, PackSize = 1 },
                new List<Ingredient> { new(2, Material("ore", 1)) }, eCraftingSkill.MetalWorking, 1290, true);
            GainPlayer player = Player(new() { [eCraftingSkill.MetalWorking] = 1295 });
            Trade<MetalWorking>(eCraftingSkill.MetalWorking).GainCraftingSkillPoints(player, bars);
            Assert.That(player.Skills[eCraftingSkill.MetalWorking], Is.EqualTo(1300));
        }

        [Test]
        public void AlchemyRaisesHerbcraftByTheRateToo()
        {
            CraftingEconomyProperties.CRAFTING_SKILL_RATE = 3;
            var poison = new Recipe(new DbItemTemplate { Id_nb = "poison", Name = "poison", Price = 10, PackSize = 1 },
                new List<Ingredient> { new(1, Material("vial", 1)) }, eCraftingSkill.Alchemy, 200, true);
            GainPlayer player = Player(new() { [eCraftingSkill.Alchemy] = 200, [eCraftingSkill.HerbalCrafting] = 200 });
            Trade<Alchemy>(eCraftingSkill.Alchemy).GainCraftingSkillPoints(player, poison);
            Assert.That(player.Skills[eCraftingSkill.Alchemy], Is.EqualTo(203));
            Assert.That(player.Skills[eCraftingSkill.HerbalCrafting], Is.EqualTo(203));
        }

        [Test]
        public void TitlesSteppedOverLeaveOutTheOneLandedOn()
        {
            Assert.That(CraftingRate165.TitlesSteppedOver(98, 103), Is.EqualTo(new[] { 100 }));
            Assert.That(CraftingRate165.TitlesSteppedOver(97, 100), Is.Empty, "GamePlayer announces the title it lands on");
            Assert.That(CraftingRate165.TitlesSteppedOver(95, 300), Is.EqualTo(new[] { 100, 200 }));
            Assert.That(CraftingRate165.TitlesSteppedOver(99, 100), Is.Empty, "a one-point gain adds nothing");
        }

        [Test]
        public void AGainOfSeveralPointsAnnouncesTheTitleItSteppedOver()
        {
            var reached = new List<int>();
            DOLEventHandler handler = (e, sender, args) => reached.Add(((NextCraftingTierReachedEventArgs)args).Points);
            GameEventMgr.AddHandler(GamePlayerEvent.NextCraftingTierReached, handler);
            try
            {
                CraftingEconomyProperties.CRAFTING_SKILL_RATE = 5;
                GainPlayer player = Player(new() { [eCraftingSkill.Tailoring] = 298, [eCraftingSkill.ClothWorking] = 1300, [eCraftingSkill.LeatherCrafting] = 1300 });
                Trade<Tailoring>(eCraftingSkill.Tailoring).GainCraftingSkillPoints(player, Robe(298));
                Assert.That(player.Skills[eCraftingSkill.Tailoring], Is.EqualTo(303));
                Assert.That(reached, Is.EqualTo(new[] { 300 }));

                reached.Clear();
                CraftingEconomyProperties.CRAFTING_SKILL_RATE = 1;
                player.Skills[eCraftingSkill.Tailoring] = 399;
                Trade<Tailoring>(eCraftingSkill.Tailoring).GainCraftingSkillPoints(player, Robe(399));
                Assert.That(reached, Is.Empty, "at pace 1 only GamePlayer announces (the test player does not)");
            }
            finally
            {
                GameEventMgr.RemoveHandler(GamePlayerEvent.NextCraftingTierReached, handler);
            }
        }
    }
}
