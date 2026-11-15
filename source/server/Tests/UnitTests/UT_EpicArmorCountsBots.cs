using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// An epic boss's armor drops with the number of attackers (AttackerTracker's epic timer: 1.6 for a GameEpicBoss, 0.8
    /// for a GameEpicNPC; -0.04 per player, -0.01 per pet up to its cap, never below 0.4). Gamebots used to be left out
    /// (GameBot is neither GamePlayer nor GameSummonedPet), so a 60-bot raid met every epic boss at the full 1.6. The
    /// owner's rule (2026-11-14): bots count at half strength, -0.02 each, and bots alone take off at most half of the
    /// range (a GameEpicBoss 1.6 to 1.0), so an epic never becomes a zerg fest; players and pets stack on top down to 0.4.
    /// Server properties epic_armor_counts_bots (on) and epic_armor_bot_strength_percent (50).
    /// DutyLeaseFixture puts an empty database behind GameObject's static constructor so this runs on its own.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_EpicArmorCountsBots : DutyLeaseFixture
    {
        private const double Boss = 1.6, EpicNpc = 0.8;
        private const int BossPetCap = 24, NpcPetCap = 16;
        private bool _countsBots;
        private int _strength;

        [SetUp]
        public void Remember()
        {
            _countsBots = DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS;
            _strength = DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT;
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = true;
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT = 50;
        }

        [TearDown]
        public void Restore()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = _countsBots;
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT = _strength;
        }

        private static T Make<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static List<GameLiving> Many<T>(int count) where T : GameLiving =>
            Enumerable.Range(0, count).Select(_ => (GameLiving)Make<T>()).ToList();

        private static List<GameLiving> Mix(int players, int bots, int pets = 0)
        {
            var all = Many<GamePlayer>(players);
            all.AddRange(Many<GameBot>(bots));
            all.AddRange(Many<GameSummonedPet>(pets));
            return all;
        }

        private static double Factor(int players, int bots, int pets = 0) =>
            AttackerTracker.ScaledArmorFactor(Boss, BossPetCap, Mix(players, bots, pets));

        [Test]
        public void TheSwitchesDefaultToHalfStrengthBots()
        {
            var props = typeof(DOL.GS.ServerProperties.Properties);
            var on = (DOL.GS.ServerProperties.ServerPropertyAttribute)props.GetField(nameof(DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS))
                .GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false).Single();
            var share = (DOL.GS.ServerProperties.ServerPropertyAttribute)props.GetField(nameof(DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT))
                .GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false).Single();
            Assert.That((on.Key, on.DefaultValue), Is.EqualTo(("epic_armor_counts_bots", (object)true)));
            Assert.That((share.Key, share.DefaultValue), Is.EqualTo(("epic_armor_bot_strength_percent", (object)50)));
        }

        [Test]
        public void TenBotsTakeOffHalfOfWhatTenPlayersDo()
        {
            Assert.That(Factor(0, 10), Is.EqualTo(1.4).Within(1e-9), "0.02 a bot");
            Assert.That(Factor(10, 0), Is.EqualTo(1.2).Within(1e-9), "0.04 a player, unchanged");
        }

        [Test]
        public void BotsAloneNeverTakeAnEpicBossBelowOne()
        {
            Assert.That(Factor(0, 30), Is.EqualTo(1.0).Within(1e-9), "30 bots reach the bots' cap");
            Assert.That(Factor(0, 100), Is.EqualTo(1.0).Within(1e-9), "100 bots: 1.0, not the 0.4 floor");
            Assert.That(Factor(0, 29), Is.EqualTo(1.6 - 29 * 0.02).Within(1e-9));
        }

        [Test]
        public void AnEpicNpcScalesTheSameWay()
        {
            Assert.That(AttackerTracker.ScaledArmorFactor(EpicNpc, NpcPetCap, Many<GameBot>(100)), Is.EqualTo(0.6).Within(1e-9),
                "half of its 0.8-to-0.4 range");
            Assert.That(AttackerTracker.ScaledArmorFactor(EpicNpc, NpcPetCap, Many<GameBot>(5)), Is.EqualTo(0.7).Within(1e-9));
        }

        [Test]
        public void PlayersAndPetsStackOnTopDownToTheFloor()
        {
            Assert.That(Factor(15, 30), Is.EqualTo(0.4).Within(1e-9), "bots' 1.0, then 15 players' 0.6");
            Assert.That(Factor(1, 7), Is.EqualTo(1.6 - 0.04 - 7 * 0.02).Within(1e-9), "a player and his seven companions");
            Assert.That(Factor(30, 100), Is.EqualTo(0.4), "never below the floor");
            Assert.That(Factor(0, 100, 40), Is.EqualTo(1.0 - 25 * 0.01).Within(1e-9), "pets keep their capped share (cap + 1, as before)");
        }

        [Test]
        public void OffIsTodaysPlayersOnlyRule()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = false;
            Assert.That(Factor(0, 100), Is.EqualTo(1.6));
            Assert.That(Factor(5, 60), Is.EqualTo(1.4).Within(1e-9));
            Assert.That(Factor(40, 0), Is.EqualTo(0.4));
            Assert.That(Factor(0, 0, 40), Is.EqualTo(1.6 - 25 * 0.01).Within(1e-9));
        }

        [Test]
        public void TheStrengthSetsBothTheStepAndTheCap()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT = 100;
            Assert.That(Factor(0, 10), Is.EqualTo(1.2).Within(1e-9), "100: a bot is a player");
            Assert.That(Factor(0, 100), Is.EqualTo(0.4).Within(1e-9));
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT = 25;
            Assert.That(Factor(0, 10), Is.EqualTo(1.5).Within(1e-9), "0.01 a bot");
            Assert.That(Factor(0, 100), Is.EqualTo(1.3).Within(1e-9), "a quarter of the range");
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT = 0;
            Assert.That(Factor(0, 100), Is.EqualTo(1.6));
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_BOT_STRENGTH_PERCENT = 400;
            Assert.That(Factor(0, 100), Is.EqualTo(0.4).Within(1e-9), "clamped to 100");
        }

        [Test]
        public void OnlyPlayersBotsAndPetsCount()
        {
            Assert.That(AttackerTracker.ScaledArmorFactor(Boss, BossPetCap, Many<GameNPC>(50)), Is.EqualTo(1.6), "monsters and guards never count");
            Assert.That(AttackerTracker.ScaledArmorFactor(Boss, BossPetCap, new List<GameLiving>()), Is.EqualTo(1.6));
        }
    }
}
