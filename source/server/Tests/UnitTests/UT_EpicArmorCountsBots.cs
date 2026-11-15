using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using DOL.GS;
using NUnit.Framework;

namespace DOL.UnitTests
{
    /// <summary>
    /// An epic boss's armor drops with the number of players fighting it (AttackerTracker's epic timer: 1.6 for a
    /// GameEpicBoss, -0.04 per player, -0.01 per pet up to its cap, never below 0.4). Gamebots used to be left out
    /// (GameBot is neither GamePlayer nor GameSummonedPet), so a 60-bot raid met every epic boss at the full 1.6.
    /// The owner's rule: bots stand in for players and count like them (server property epic_armor_counts_bots, on).
    /// DutyLeaseFixture puts an empty database behind GameObject's static constructor so this runs on its own.
    /// </summary>
    [TestFixture, NonParallelizable]
    public class UT_EpicArmorCountsBots : DutyLeaseFixture
    {
        private bool _previous;

        [SetUp]
        public void Remember() => _previous = DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS;

        [TearDown]
        public void Restore() => DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = _previous;

        private static T Make<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        private static IEnumerable<GameLiving> Many<T>(int count) where T : GameLiving =>
            Enumerable.Range(0, count).Select(_ => (GameLiving)Make<T>()).ToList();

        [Test]
        public void TheSwitchIsOnByDefault()
        {
            var field = typeof(DOL.GS.ServerProperties.Properties).GetField(nameof(DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS));
            var attribute = (DOL.GS.ServerProperties.ServerPropertyAttribute)field.GetCustomAttributes(typeof(DOL.GS.ServerProperties.ServerPropertyAttribute), false).Single();
            Assert.That(attribute.Key, Is.EqualTo("epic_armor_counts_bots"));
            Assert.That(attribute.DefaultValue, Is.EqualTo(true));
        }

        [Test]
        public void BotsCountLikePlayers()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = true;
            Assert.That(AttackerTracker.CountsAsPlayerAttacker(Make<GamePlayer>()), Is.True);
            Assert.That(AttackerTracker.CountsAsPlayerAttacker(Make<GameBot>()), Is.True);
            Assert.That(AttackerTracker.CountsAsPlayerAttacker(Make<GameNPC>()), Is.False, "a monster or guard never counts");
            Assert.That(AttackerTracker.CountsAsPlayerAttacker(Make<GameSummonedPet>()), Is.False, "pets keep their own share");

            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GameBot>(10)), Is.EqualTo(1.2).Within(1e-9));
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GameBot>(10)),
                Is.EqualTo(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GamePlayer>(10))).Within(1e-9),
                "ten bots lower it exactly as ten players would");
        }

        [Test]
        public void ASixtyBotRaidMeetsTheFloor()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = true;
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GameBot>(60)), Is.EqualTo(0.4));
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GameBot>(30)), Is.EqualTo(0.4).Within(1e-9), "30 attackers reach it");
            Assert.That(AttackerTracker.ScaledArmorFactor(0.8, 16, Many<GameBot>(60)), Is.EqualTo(0.4), "an epic NPC too");
        }

        [Test]
        public void ThePlayerAndHisCompanionsCountTogether()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = true;
            var group = new List<GameLiving> { Make<GamePlayer>() };
            group.AddRange(Many<GameBot>(7));
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, group), Is.EqualTo(1.6 - 8 * 0.04).Within(1e-9));
        }

        [Test]
        public void OffIsTheStockPlayersOnlyRule()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = false;
            Assert.That(AttackerTracker.CountsAsPlayerAttacker(Make<GameBot>()), Is.False);
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GameBot>(60)), Is.EqualTo(1.6));
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GamePlayer>(5)), Is.EqualTo(1.4).Within(1e-9));
        }

        [Test]
        public void PetsKeepTheirCappedShare()
        {
            DOL.GS.ServerProperties.Properties.EPIC_ARMOR_COUNTS_BOTS = true;
            // The stock loop counts a pet while petCount <= cap, so cap + 1 pets lower it.
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, Many<GameSummonedPet>(40)), Is.EqualTo(1.6 - 25 * 0.01).Within(1e-9));
            var mixed = new List<GameLiving>(Many<GameBot>(5));
            mixed.AddRange(Many<GameSummonedPet>(3));
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, mixed), Is.EqualTo(1.6 - 0.2 - 0.03).Within(1e-9));
        }

        [Test]
        public void NobodyFightingLeavesTheDefault()
        {
            Assert.That(AttackerTracker.ScaledArmorFactor(1.6, 24, new List<GameLiving>()), Is.EqualTo(1.6));
        }
    }
}
